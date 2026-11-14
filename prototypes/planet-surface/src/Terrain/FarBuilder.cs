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
/// dissolve into each other by distance (flora.gdshader); a level-1 chunk carries it and small growth's
/// near drawing. Distant: the distant drawing of a share of the large plants, out to the detail
/// distance (<see cref="DetailReach"/>), which every level carries and the coarsest carries alone.
/// </summary>
public readonly record struct FloraBatch(Flora Kind, int Plant, int Variant, float[] Buffer, int Count, bool Far = false, bool Distant = false)
{
    /// <summary>The flora_layer it is drawn as (<see cref="TerrainStreamer.NearLayer"/> and the rest).</summary>
    public int Layer => Distant ? TerrainStreamer.DistantLayer : Far ? TerrainStreamer.FarLayer : FloraMeshes.IsLarge(Kind) ? TerrainStreamer.NearLayer : TerrainStreamer.SmallLayer;

    public void Deconstruct(out Flora kind, out float[] buffer, out int count)
    {
        kind = Kind;
        buffer = Buffer;
        count = Count;
    }
}

/// <summary>One far chunk's flora and river water: plain arrays built off the main thread.</summary>
public sealed class FarJob
{
    public int Fx, Fz, Lod;     // canonical far-chunk coordinates
    public int Version;

    /// <summary>The chunk's extent each way, m: <see cref="FarBuilder.Size"/>, or less for the last one before a seam (<see cref="FarBuilder.Span"/>).</summary>
    public int SpanX = FarBuilder.Size, SpanZ = FarBuilder.Size;

    /// <summary>
    /// Its grid's samples (heights, river levels, in the river), a ring round it, when the GPU rings already hold them
    /// (<see cref="ClipGround.Samples"/>): the same ground at the same spacing, so they are not worked out twice. Null: sampled here.
    /// </summary>
    public float[] Height, RiverLevel;
    public bool[] River;

    public Vector3[] WaterVertices;
    public int[] WaterIndices;
    public readonly List<FloraBatch> Flora = new();

    /// <summary>The solid flora (trunks, boulders) of a level-0 chunk, for the colliders near the player. Null at coarser levels.</summary>
    public List<FloraSolid> Solids;
    public double Milliseconds;
    public Exception Error;
}

/// <summary>
/// What lies on the ground beyond the voxel chunks, and its shape. The far chunks: the land sampled from the same
/// terrain source (planet generator heights plus stamps) at 2, 4 or 8 m, for its flora scatter and river water. Flora is
/// laid out here for the voxel area too, and skips spots that have been dug, covered by a pad or opened by a cave
/// mouth, so nothing ever floats. At the finest level it also lists the solid flora (trunks and boulders,
/// <see cref="FloraSolidRules"/>) for the colliders near the player, and leaves out trees that have been felled.
/// The far ground itself is drawn by the GPU rings (<see cref="ClipGround"/>), each tile of them shaped here
/// (<see cref="Shape"/>): slid onto its cliffs, lit and coloured with the same palette as the voxels.
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

            float[] height = job.Height, level = job.RiverLevel;
            bool[] river = job.River;
            if (height == null)
            {
                height = new float[g * (grid.Nz + 2)];
                level = new float[height.Length];
                river = new bool[height.Length];
                for (int j = 0; j < grid.Nz + 2; j++)
                for (int i = 0; i < g; i++)
                {
                    GroundSample s = source.Ground(x0 + (i - 1) * step, z0 + (j - 1) * step, step);
                    int k = j * g + i;
                    height[k] = s.Height;
                    level[k] = s.RiverLevel;
                    river[k] = s.InRiver;
                }
            }
            // How far each grid point faces up, from the slope across it.
            var up = new float[grid.Nx * grid.Nz];
            for (int j = 0; j < grid.Nz; j++)
            for (int i = 0; i < grid.Nx; i++)
            {
                int s = grid.Sample(i, j);
                float gx = (height[s + 1] - height[s - 1]) / (2f * step), gz = (height[s + g] - height[s - g]) / (2f * step);
                up[j * grid.Nx + i] = 1f / MathF.Sqrt(1f + gx * gx + gz * gz);
            }
            // (Only the finest level lays talus at the foot of cliffs, by the relief.)
            ReliefField relief = job.Lod == 0 ? Relief(job.SpanX, job.SpanZ, grid, step, x0, z0, height, source) : null;
            BuildRiverWater(job, grid, step, level, river, height, source, x0, z0);
            Scatter(job, source, palette, dug, felled, grid, step, height, river, up, relief);
            job.Milliseconds = watch.Elapsed.TotalMilliseconds;
        }
        catch (Exception e)
        {
            job.Error = e;
        }
    }

    /// <summary>The ground over a grid as it is drawn: each point slid onto the cliffs beside it (its position in the grid's frame, m), its normal, colour, glow (x of the flags) and relief.</summary>
    internal sealed record Surface(Vector3[] Vertices, Vector3[] Normals, Color[] Colors, Vector2[] Flags, Vector2[] Relief);

    /// <summary>
    /// Shapes the ground over a grid of points <paramref name="step"/> apart from (x0, z0) (<paramref name="spanX"/> by
    /// <paramref name="spanZ"/> m), its heights sampled with a ring round it. Every square is split along the grid's own
    /// diagonal (b-c), as the GPU rings' blocks are; where a point's slide would turn a triangle over, it eases off. The
    /// outer <paramref name="inset"/> rows each side are worked out only so far as the ones inside need them (their slides).
    /// </summary>
    internal static Surface Shape(Grid grid, int spanX, int spanZ, float step, double x0, double z0, float[] height, bool[] river, PlanetTerrainSource source, GroundPalette palette, int inset)
    {
        int points = grid.Nx * grid.Nz, g = grid.G;
        var surface = new Surface(new Vector3[points], new Vector3[points], new Color[points], new Vector2[points], new Vector2[points]);
        Vector3[] vertices = surface.Vertices;

        // Beside a cliff the grid's points slide onto it (Slides), each still a true sample of the ground.
        var slideX = new float[points];
        var slideZ = new float[points];
        var drop = new float[points];
        long t0 = Stopwatch.GetTimestamp(); // PROFILE (temporary)
        Slides(grid, step, x0, z0, height, river, source, slideX, slideZ, drop);
        Prof(ref t0, 0);
        var drawn = new float[points];
        void Place(int k)
        {
            int i = k % grid.Nx, j = k / grid.Nx;
            double x = x0 + i * step, z = z0 + j * step;
            drawn[k] = slideX[k] == 0f && slideZ[k] == 0f ? height[grid.Sample(i, j)]
                : source.Ground(Slid(x, slideX[k], step), Slid(z, slideZ[k], step), step).Height;
            vertices[k] = new Vector3((i + slideX[k]) * step, drawn[k], (j + slideZ[k]) * step);
        }
        for (int k = 0; k < points; k++)
        {
            Place(k);
            // A point that slid off its own top or foot onto the face eases back (Drift).
            float was = height[grid.Sample(k % grid.Nx, k / grid.Nx)];
            for (int n = 0; (slideX[k] != 0f || slideZ[k] != 0f) && MathF.Abs(drawn[k] - was) > Drift(drop[k]); n++)
            {
                float keep = n + 1 < Eases ? 0.5f : 0f;
                slideX[k] *= keep;
                slideZ[k] *= keep;
                Place(k);
            }
        }
        Prof(ref t0, 1);
        Unfold(vertices, grid, step, slideX, slideZ, Place);
        Prof(ref t0, 2);
        ReliefField field = Relief(spanX, spanZ, grid, step, x0, z0, height, source);
        Prof(ref t0, 3);
        double w = palette.Map.WidthMeters, h = palette.Map.HeightMeters;
        for (int j = inset; j < grid.Nz - inset; j++)
        for (int i = inset; i < grid.Nx - inset; i++)
        {
            int k = j * grid.Nx + i, s = grid.Sample(i, j);
            float gx = (height[s + 1] - height[s - 1]) / (2f * step), gz = (height[s + g] - height[s - g]) / (2f * step);
            double x = Mathx.WrapX(Slid(x0 + i * step, slideX[k], step), w), z = Mathx.Wrap(Slid(z0 + j * step, slideZ[k], step), h);
            surface.Normals[k] = new Vector3(-gx, 1f, -gz).Normalized();
            surface.Colors[k] = GroundColor(source, palette, x, z, drawn[k], CliffSlope2(height, s, g, step, gx * gx + gz * gz), river[s], out surface.Flags[k], step);
            surface.Relief[k] = field.At(vertices[k].X, vertices[k].Z, vertices[k].Y);
        }
        Prof(ref t0, 4);
        CliffNormals(vertices, surface.Normals, grid, step, height);
        Prof(ref t0, 5);
        return surface;
    }

    public static readonly long[] ProfTicks = new long[8]; // PROFILE (temporary): slides, place, unfold, relief, colours, normals, heights, all
    internal static void Prof(ref long t, int k)
    {
        long now = Stopwatch.GetTimestamp();
        System.Threading.Interlocked.Add(ref ProfTicks[k], now - t);
        t = now;
    }
    /// <summary>
    /// The squared slope that picks a grid point's colour. A point on a spur or in a gully on a jagged
    /// cliff has neighbours far below (or above) it on both sides, so the difference across it says it
    /// is flat, and a point by a cliff only across a diagonal does the same: it was painted as soil, and
    /// every long triangle down the cliff from it showed as a pale streak, which at a distance looks
    /// like a tear in the rock with the sky behind it. So the steepest of the triangle edges that can meet
    /// at a point (either diagonal: a square is split by the ground) counts too, where it is a cliff and the
    /// plain gradient misses it. Ordinary slopes and crests, whose gradient tells the truth, keep their colours.
    /// </summary>
    internal static float CliffSlope2(float[] height, int s, int g, float step, float plain2)
    {
        float h = height[s], steepest = 0f;
        void Edge(int n, float length) => steepest = MathF.Max(steepest, MathF.Abs(height[n] - h) / (length * step));
        Edge(s + 1, 1f);
        Edge(s - 1, 1f);
        Edge(s + g, 1f);
        Edge(s - g, 1f);
        Edge(s + 1 - g, Mathf.Sqrt2);
        Edge(s - 1 + g, Mathf.Sqrt2);
        Edge(s + 1 + g, Mathf.Sqrt2);
        Edge(s - 1 - g, Mathf.Sqrt2);
        float cliff = Smooth(CliffFrom, CliffFrom * 2f, steepest) * Smooth(0.8f, 0.5f, MathF.Sqrt(plain2) / MathF.Max(steepest, 1e-4f));
        return plain2 + (MathF.Max(plain2, steepest * steepest) - plain2) * cliff;
    }

    /// <summary>The rise per metre over a triangle edge from which a point can count as on a cliff.</summary>
    const float CliffFrom = 0.75f;

    /// <summary>The same soil-to-rock rule as the voxel source: soil thins out and slides off as slopes steepen.</summary>
    internal static Color GroundColor(PlanetTerrainSource source, GroundPalette pal, double x, double z, float h, float slope2, bool inRiver, out Vector2 flags, float spacing = 1f)
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
    internal readonly record struct Grid(int Nx, int Nz, int At = 0)
    {
        /// <summary>Whether a grid index is off the lines a point may only slide along: <see cref="At"/> is where the first point stands among them.</summary>
        public bool OffLine(int i, int per) => ((i + At) % per + per) % per != 0;

        /// <summary>The sampled grid's row length.</summary>
        public int G => Nx + 2;

        /// <summary>Grid point (i, j)'s index in the sampled grid.</summary>
        public int Sample(int i, int j) => (j + 1) * G + i + 1;
    }

    // ---- Cliffs ---------------------------------------------------------------------------------
    //
    // A heightfield's points stand on a fixed grid, but a cliff runs wherever it likes: sampled where they
    // are, the points beside one stand either on its top or at its foot, never on the line where it falls,
    // so its face is drawn as a staircase in plan as wide as a grid square, and with the grid's own diagonal
    // each square it crosses at a corner becomes a fin. Down a long wall those fins alternate into tall
    // teeth. So the points beside a cliff slide onto it: along each grid edge that falls steeply, the place
    // where the ground passes halfway down is found, and the point at each end moves toward it, stopping
    // just short on its own side. Each point is then sampled again where it stands, so the ground still lies
    // on the ground at every vertex: the top row of a face runs along the cliff's top, the bottom row along
    // its foot.
    //
    // What must not change: the GPU rings meet the voxels' square (which moves 16 m at a time) along lines
    // every 16 m, closed by the wall that dives under the voxels from the points on those lines; and a tile's
    // points are shaped as the tile beside it shapes them. So a point on such a line only ever slides along it.

    /// <summary>The spacing of the lines a point may only slide along, m: tile borders and every place the voxels' square can end.</summary>
    const int Line = 16;

    /// <summary>The fall per metre over a grid edge from which it crosses a cliff, and the least fall that counts.</summary>
    const float SteepFrom = 1.2f, SteepLeast = 2.5f;

    /// <summary>How far a point slides at most, and how far short of the halfway line it stops, in grid steps.</summary>
    const float MaxSlide = 0.42f, SlideShort = 0.04f;

    static bool Steep(float a, float b, float step)
    {
        float d = MathF.Abs(a - b);
        return d > SteepLeast && d > SteepFrom * step;
    }

    /// <summary>
    /// How far the ground rises above each grid point and falls below it within 16 m: the highest and
    /// lowest ground round each point of the planet's 8 m relief lattice, as the voxels measure it
    /// (<see cref="PlanetTerrainSource.Relief"/>), taken between the lattice points under each vertex.
    /// The lattice reuses the chunk's own samples where it falls on them.
    /// </summary>
    static ReliefField Relief(int spanX, int spanZ, Grid grid, float step, double x0, double z0, float[] height, PlanetTerrainSource source)
    {
        // (A grid coarser than the lattice is its own lattice, a step each way.)
        int L = Math.Max(PlanetTerrainSource.ReliefStep, (int)step), R = Math.Max(1, PlanetTerrainSource.ReliefStep * PlanetTerrainSource.ReliefReach / L);
        int la = spanX / L + 1, lb = spanZ / L + 1, wa = la + 2 * R, wb = lb + 2 * R;
        int per = Math.Max(1, (int)MathF.Round(L / step));
        var wide = new float[wa * wb];
        for (int b = 0; b < wb; b++)
        for (int a = 0; a < wa; a++)
        {
            int i = (a - R) * per, j = (b - R) * per;
            wide[b * wa + a] = i >= -1 && i <= grid.Nx && j >= -1 && j <= grid.Nz
                ? height[(j + 1) * grid.G + i + 1]
                : source.Ground(x0 + (a - R) * (double)L, z0 + (b - R) * (double)L, L).Height;
        }
        var hi = new float[la * lb];
        var lo = new float[la * lb];
        for (int b = 0; b < lb; b++)
        for (int a = 0; a < la; a++)
        {
            float top = float.MinValue, bottom = float.MaxValue;
            for (int db = 0; db <= 2 * R; db++)
            for (int da = 0; da <= 2 * R; da++)
            {
                float h = wide[(b + db) * wa + a + da];
                top = MathF.Max(top, h);
                bottom = MathF.Min(bottom, h);
            }
            hi[b * la + a] = top;
            lo[b * la + a] = bottom;
        }
        return new ReliefField(hi, lo, la, lb, L);
    }

    /// <summary>
    /// On a cliff, a grid point's normal from the ground's slope across it (the samples a step either
    /// side, where it stood before it slid) says little about the triangles drawn round it: points down
    /// a face lit as if they faced every way, light and dark triangles in a row of teeth. So inside the
    /// grid, a point beside a steep edge takes the normal of the triangles it is a corner of (each square
    /// split along its b-c diagonal), weighted by their area. On the grid's border the slope's normal stays.
    /// </summary>
    static void CliffNormals(Vector3[] vertices, Vector3[] normals, Grid grid, float step, float[] height)
    {
        int nx = grid.Nx, nz = grid.Nz, g = grid.G;
        var cliff = new bool[nx * nz];
        bool any = false;
        for (int j = 1; j < nz - 1; j++)
        for (int i = 1; i < nx - 1; i++)
        {
            int s = grid.Sample(i, j);
            float h = height[s];
            bool steep = Steep(h, height[s + 1], step) || Steep(h, height[s - 1], step) || Steep(h, height[s + g], step) || Steep(h, height[s - g], step);
            cliff[j * nx + i] = steep;
            any |= steep;
        }
        if (!any) return;
        var sum = new Vector3[nx * nz];
        void Face(int a, int b, int c)
        {
            if (!cliff[a] && !cliff[b] && !cliff[c]) return;
            // Wound so the ground's top faces up: (c - a) x (b - a) is outward.
            Vector3 n = (vertices[c] - vertices[a]).Cross(vertices[b] - vertices[a]);
            sum[a] += n;
            sum[b] += n;
            sum[c] += n;
        }
        for (int j = 0; j < nz - 1; j++)
        for (int i = 0; i < nx - 1; i++)
        {
            int a = j * nx + i, b = a + 1, c = a + nx, d = c + 1;
            Face(a, b, c);
            Face(b, d, c);
        }
        for (int k = 0; k < sum.Length; k++)
            if (cliff[k] && sum[k].LengthSquared() > 1e-12f) normals[k] = sum[k].Normalized();
    }
    /// <summary>The highest and lowest ground round each point of a chunk's 8 m relief lattice.</summary>
    internal sealed record ReliefField(float[] Hi, float[] Lo, int La, int Lb, int L)
    {
        /// <summary>How far the ground rises above a height and falls below it at a point of the chunk (m from its corner).</summary>
        public Vector2 At(float u, float v, float y)
        {
            float gx = Math.Clamp(u / L, 0f, La - 1.001f), gz = Math.Clamp(v / L, 0f, Lb - 1.001f);
            int i = (int)gx, j = (int)gz;
            float tx = gx - i, tz = gz - j;
            float Lerp(float[] f) => (f[j * La + i] * (1 - tx) + f[j * La + i + 1] * tx) * (1 - tz) + (f[(j + 1) * La + i] * (1 - tx) + f[(j + 1) * La + i + 1] * tx) * tz;
            return new Vector2(MathF.Max(0f, Lerp(Hi) - y), MathF.Max(0f, y - Lerp(Lo)));
        }
    }

    /// <summary>A coordinate slid along its axis by <paramref name="slide"/> grid steps.</summary>
    static double Slid(double at, float slide, float step) => at + slide * step;

    /// <summary>
    /// Where along the edge from a (0) to b (1) the ground passes the height <paramref name="share"/> of
    /// the way from a's to b's: four halvings, then a straight line between the last two samples.
    /// </summary>
    static float Crossing(PlanetTerrainSource source, double ax, double az, double bx, double bz, float ha, float hb, float step, float share)
    {
        float target = ha + (hb - ha) * share;
        float lo = 0f, hi = 1f, flo = ha - target, fhi = hb - target;
        for (int n = 0; n < 4; n++)
        {
            float mid = (lo + hi) * 0.5f;
            float f = source.Ground(ax + (bx - ax) * mid, az + (bz - az) * mid, step).Height - target;
            if (f > 0f == flo > 0f) { lo = mid; flo = f; }
            else { hi = mid; fhi = f; }
        }
        float t = flo == fhi ? 0.5f : flo / (flo - fhi);
        return lo + (hi - lo) * Math.Clamp(t, 0f, 1f);
    }

    /// <summary>
    /// The slides a steep edge gives the point at its start (+, toward its end) and at its end (-): each
    /// toward where the ground beside it starts to fall or rise to the other (<see cref="Lip"/> of the way),
    /// stopping just short. The top of a face stays on the top, its foot at the foot: sliding to the face's
    /// middle put points partway down it, and the rim was drawn notched with teeth.
    /// </summary>
    static (float start, float end) EdgeSlides(PlanetTerrainSource source, double ax, double az, double bx, double bz, float ha, float hb, float step)
    {
        float ca = Crossing(source, ax, az, bx, bz, ha, hb, step, Lip);
        float cb = Crossing(source, ax, az, bx, bz, ha, hb, step, 1f - Lip);
        return (Math.Clamp(ca - SlideShort, 0f, MaxSlide), -Math.Clamp(1f - cb - SlideShort, 0f, MaxSlide));
    }

    /// <summary>How far down (or up) a face a point beside it may stand, as a share of the fall between the two.</summary>
    const float Lip = 0.06f;

    /// <summary>
    /// How far each grid point slides toward the cliffs beside it, in grid steps along x and z, by every edge
    /// it is an end of (the ones out to the sampled ring too, so a grid's edge points slide as the grid beside
    /// it slides them). A point on a line every 16 m never leaves it; nor does a point in a river, whose water
    /// is drawn over the grid.
    /// </summary>
    static void Slides(Grid grid, float step, double x0, double z0, float[] height, bool[] river, PlanetTerrainSource source, float[] sx, float[] sz, float[] drop)
    {
        int nx = grid.Nx, nz = grid.Nz, g = grid.G;
        int per = Math.Max(1, (int)MathF.Round(Line / step));
        for (int j = 0; j < nz; j++)
        for (int i = -1; i < nx; i++)
        {
            bool fromA = i >= 0 && grid.OffLine(i, per), fromB = i + 1 < nx && grid.OffLine(i + 1, per);
            if (!fromA && !fromB) continue;
            int sa = grid.Sample(i, j), sb = sa + 1;
            if (!Steep(height[sa], height[sb], step)) continue;
            double ax = x0 + i * step, z = z0 + j * step;
            var (start, end) = EdgeSlides(source, ax, z, ax + step, z, height[sa], height[sb], step);
            int k = j * nx + i;
            float fall = MathF.Abs(height[sa] - height[sb]);
            if (fromA && !river[sa]) { sx[k] += start; drop[k] = MathF.Max(drop[k], fall); }
            if (fromB && !river[sb]) { sx[k + 1] += end; drop[k + 1] = MathF.Max(drop[k + 1], fall); }
        }
        for (int j = -1; j < nz; j++)
        for (int i = 0; i < nx; i++)
        {
            bool fromA = j >= 0 && grid.OffLine(j, per), fromB = j + 1 < nz && grid.OffLine(j + 1, per);
            if (!fromA && !fromB) continue;
            int sa = grid.Sample(i, j), sb = sa + g;
            if (!Steep(height[sa], height[sb], step)) continue;
            double x = x0 + i * step, az = z0 + j * step;
            var (start, end) = EdgeSlides(source, x, az, x, az + step, height[sa], height[sb], step);
            int k = j * nx + i;
            float fall = MathF.Abs(height[sa] - height[sb]);
            if (fromA && !river[sa]) { sz[k] += start; drop[k] = MathF.Max(drop[k], fall); }
            if (fromB && !river[sb]) { sz[k + nx] += end; drop[k + nx] = MathF.Max(drop[k + nx], fall); }
        }
        for (int k = 0; k < sx.Length; k++)
        {
            sx[k] = Math.Clamp(sx[k], -MaxSlide, MaxSlide);
            sz[k] = Math.Clamp(sz[k], -MaxSlide, MaxSlide);
        }
    }

    /// <summary>
    /// How far a slid point's ground may differ from where it stood: a point slides along its own top or
    /// foot, so more than this means it slid onto the face (round a corner of the cliff, where two slides
    /// add up) and would stand there as a spike or a notch. Then its slide is halved, and halved again.
    /// </summary>
    static float Drift(float drop) => MathF.Max(1f, drop * 0.15f);

    /// <summary>The steps of <see cref="Drift"/>'s easing: half, a quarter, an eighth, then none.</summary>
    const int Eases = 4;

    /// <summary>
    /// Keeps every square's two triangles (split along b-c, the GPU blocks' diagonal) the right way up in plan: where slides
    /// would turn one over (a back face, a hole), the square's inner points ease off their cliffs until it can be drawn. A
    /// grid's border points keep their slides (the tile beside it shapes them too): along a 16 m line they only slide along
    /// it, and with only those the split is always sound.
    /// </summary>
    static void Unfold(Vector3[] vertices, Grid grid, float step, float[] sx, float[] sz, Action<int> place)
    {
        int nx = grid.Nx, nz = grid.Nz;
        float least = 0.002f * step * step;
        float Area(int p, int q, int r)
        {
            Vector3 a = vertices[p], b = vertices[q], c = vertices[r];
            return (b.X - a.X) * (c.Z - a.Z) - (b.Z - a.Z) * (c.X - a.X);
        }
        var ease = new HashSet<int>();
        for (int pass = 0; pass <= 8; pass++)
        {
            ease.Clear();
            for (int j = 0; j < nz - 1; j++)
            for (int i = 0; i < nx - 1; i++)
            {
                int a = j * nx + i, b = a + 1, c = a + nx, d = c + 1;
                if (Area(a, b, c) > least && Area(b, d, c) > least) continue;
                foreach (int k in new[] { a, b, c, d })
                {
                    int ki = k % nx, kj = k / nx;
                    if (ki > 0 && kj > 0 && ki < nx - 1 && kj < nz - 1) ease.Add(k);
                }
            }
            if (ease.Count == 0) break;
            foreach (int k in ease)
            {
                float keep = pass < 2 ? 0.5f : 0f;
                sx[k] *= keep;
                sz[k] *= keep;
                place(k);
            }
        }
    }
    /// <summary>
    /// Flat water at the river's surface over every grid square near the channel. It runs a square past the
    /// channel, in under the banks, so wherever it meets the land the ground's own surface makes its edge, never
    /// the squares': off the channel a vertex keeps the nearest channel sample's level unless the ground there is
    /// lower, and then sinks under it (at the finest level under the voxels' own ground too, which this level's
    /// samples don't follow to the centimetre). Where a river has come down to the sea the shader leaves its
    /// water to the sea, at the same level.
    /// </summary>
    static void BuildRiverWater(FarJob job, Grid grid, float step, float[] level, bool[] river, float[] height, PlanetTerrainSource source, double x0, double z0)
    {
        int g = grid.G;
        var verts = new List<Vector3>();
        var indices = new List<int>();
        var vertexOf = new Dictionary<int, int>();
        int Vertex(int i, int j)
        {
            int s = grid.Sample(i, j);
            if (vertexOf.TryGetValue(s, out int v)) return v;
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
            float y = l - SurfaceSampler.RiverDrop;
            // (Not a river come down to the sea: at the sea's level, ground beside it lower still is the sea's own water.)
            if (!river[s] && y > source.WaterLevel)
            {
                float ground = job.Lod == 0 ? MathF.Min(height[s], source.SurfaceHeight(x0 + i * step, z0 + j * step)) : height[s];
                y = MathF.Min(y, ground - 0.3f);
            }
            verts.Add(new Vector3(i * step, y, j * step));
            vertexOf[s] = v;
            return v;
        }
        // Never over a place's opening dug down from the bank: the sheet would hang over it. (Only round a place
        // that digs: the voxels' ground under a far sample isn't open anywhere else, steep channel sides and all.)
        bool Opened(int i, int j)
        {
            double x = x0 + i * step, z = z0 + j * step;
            return job.Lod == 0 && NearDug(source, x, z, 0f) && FarBuilder.Opened(source, x, z, height[grid.Sample(i, j)]);
        }
        bool Wet(int i, int j) => i >= 0 && j >= 0 && i < grid.Nx && j < grid.Nz && river[grid.Sample(i, j)];
        for (int j = 0; j < grid.Nz - 1; j++)
        for (int i = 0; i < grid.Nx - 1; i++)
        {
            bool wet = false;
            for (int dj = -1; dj <= 2 && !wet; dj++)
            for (int di = -1; di <= 2 && !wet; di++)
                wet = Wet(i + di, j + dj);
            if (!wet || Opened(i, j) || Opened(i + 1, j) || Opened(i, j + 1) || Opened(i + 1, j + 1)) continue;
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
    static void Scatter(FarJob job, PlanetTerrainSource source, GroundPalette pal, IReadOnlySet<long> dug, IReadOnlySet<long> felled,
        Grid grid, float step, float[] height, bool[] river, float[] up, ReliefField relief)
    {
        // A whole chunk is 32 cells each way; one cut short at a seam has fewer. Cells keep their
        // ids (and their random numbers, drawn in the same order) whatever the chunk's size.
        int cellsX = (int)(job.SpanX / ScatterCell), cellsZ = (int)(job.SpanZ / ScatterCell);
        FloraSolidRules solids = job.Lod == 0 ? Solids : null;
        if (solids != null) job.Solids = new List<FloraSolid>();
        var rng = new Rng(Hash.Of(pal.Map.Spec.Seed, job.Fx, job.Fz, 0x5CA7));
        var clump = new Fractal(Hash.Of(pal.Map.Spec.Seed, 0x636C756D), 70, 3);
        var buffers = new Dictionary<(Flora Kind, int Plant, int Variant, bool Far, bool Distant), List<float>>();
        PlanetFlora book = pal.Flora;
        double x0 = job.Fx * (double)Size, z0 = job.Fz * (double)Size;
        var taken = new bool[cellsX * cellsZ];   // cells something was set down in
        int current = 0;
        for (int gj = 0; gj < cellsZ; gj++)
        for (int gi = 0; gi < cellsX; gi++)
        {
            current = gj * cellsX + gi;
            float lx = (gi + rng.NextFloat()) * ScatterCell, lz = (gj + rng.NextFloat()) * ScatterCell;
            float pick = rng.NextFloat(), chance = rng.NextFloat(), size = rng.NextFloat(), turn = rng.NextFloat(), shade = rng.NextFloat(), kin = rng.NextFloat();
            double x = x0 + lx, z = z0 + lz;
            long id = ScatterId(job.Fx, job.Fz, gi, gj);
            if (felled != null && felled.Contains(id)) continue; // felled: gone, at every level of detail

            // A tree through a ruin takes the cell's place in the scatter: it's the same flora from here on.
            if (pal.Planted != null && pal.Planted.TryGetValue(id, out PlantedFlora tree))
            {
                if (Dug(dug, (int)Math.Floor(tree.X), (int)Math.Floor(tree.Z))) continue; // the ground went from under it: gone, as any tree
                if (source.Reserved(tree.X, tree.Z, Reach(tree.Kind, tree.Species, tree.Scale))) continue; // its crown would reach the bunker's ground
                float tx = (float)(tree.X - x0), tz = (float)(tree.Z - z0), treeY = tree.Foot - FloraSolidRules.Sink;
                var treeBasis = new Basis(Vector3.Up, tree.Angle).Scaled(new Vector3(tree.Scale, tree.Scale, tree.Scale));
                if (job.Lod == 0) Add(tree.Kind, tree.Species, 0, tx, treeY, tz, treeBasis, tree.Tint, 0f, far: false);
                if (job.Lod <= 1) Add(tree.Kind, tree.Species, -1, tx, treeY, tz, treeBasis, tree.Tint, 0f, far: true);
                Add(tree.Kind, tree.Species, -1, tx, treeY, tz, treeBasis, tree.Tint, 0f, far: true, distant: true);
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
            // Large growth has a far drawing (levels 0 and 1 carry it), and a share of it a distant one
            // (every level); small growth only a near one (levels 0 and 1).
            bool hasFar = FloraMeshes.IsLarge(rule.Kind);
            if (job.Lod > 1 && !hasFar) continue;

            // What grows here: the world's own species of this kind, for this biome and region. Rocks stay rocks.
            Plant plant = book?.Pick(rule.Kind, biome, x, z, kin);
            if (book != null && plant == null && book.Rolled(rule.Kind)) continue;
            // Its near mesh, and so its far one: the distant drawing is the same far drawing, for the plants of the first.
            int variant = plant == null ? 0 : (int)(kin * 977f) % plant.Near.Length;
            bool distant = hasFar && (plant == null ? Distant(pal, id) : variant == 0);
            if (job.Lod > 1 && !distant) continue;

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
            // A tree takes its size from its kind's range on this world, by its age: the one scale draws it near and far and sizes its colliders.
            if (plant != null && book.TreeScale(plant, Hash.Unit(Hash.Of(pal.Map.Spec.Seed, id, 0xA6E), 0), size) is float grown) scale = grown;
            if (source.Reserved(x, z, Reach(rule.Kind, plant, scale))) continue; // its crown or body would reach the bunker's ground or the sky over it
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
            // A stem's foot goes down to the lowest ground round it, the far sides too: the slope above is
            // read uphill, and ground falling away behind (a cut, a rim, a bank) would leave it standing proud.
            if (!rock) rootY = MathF.Min(rootY, MathF.Min(source.SurfaceHeight(x - foot, z), source.SurfaceHeight(x, z - foot)) - FloraSolidRules.Sink);
            var basis = new Basis(Vector3.Up, angle).Scaled(new Vector3(scale, scale, scale));
            if (lean > 0.01f && slope > 0f)
                basis = new Basis(new Vector3(-gz, 0f, gx) / slope, -MathF.Atan(lean)) * basis;

            // A share of the large thins out sooner far off (it was the share the far levels used to leave out),
            // and of the small (marked the same way) at the large's near reach.
            if (job.Lod == 0 || !hasFar && job.Lod == 1)
                Add(rule.Kind, plant, variant, lx, rootY, lz, basis, tint, glow + (!hasFar && shade > 0.6f ? 2f : 0f), far: false);
            // Its far drawing is its own near mesh's far one, turned, sized and tinted as it is.
            if (hasFar && job.Lod <= 1) Add(rule.Kind, plant, plant == null ? 0 : -1 - variant, lx, rootY, lz, basis, tint, glow + (shade > 0.6f && !distant ? 2f : 0f), far: true);
            if (distant) Add(rule.Kind, plant, plant == null ? 0 : -1, lx, rootY, lz, basis, tint, glow, far: true, distant: true);

            // Trunks, stalks, pipes, shards and boulders are solid: their colliders, in planet coordinates, round
            // the parts of the near mesh this one draws (whatever the level of detail draws from here).
            solids?.MakeAll(rule.Kind, plant, plant == null ? 0 : (int)(kin * 977f) % plant.Near.Length, id, new Vector3((float)x, rootY, (float)z), scale, angle, job.Solids);
        }

        // Talus: rock fallen from a cliff lies along its foot, the taller the face the more and the bigger,
        // on whatever the scatter left free (a cell nothing grows in, off water, roads, pads and dug ground).
        // Its own random numbers, so the scatter's are as they were.
        if (job.Lod == 0)
        {
            Flora fallen = pal.Map.Def.Archetype == Archetype.Lava ? Flora.LavaRock : Flora.Rock;
            for (int gj = 0; gj < cellsZ; gj++)
            for (int gi = 0; gi < cellsX; gi++)
            {
                if (taken[gj * cellsX + gi]) continue;
                long id = ScatterId(job.Fx, job.Fz, gi, gj);
                if (felled != null && felled.Contains(id)) continue;
                ulong roll = Hash.Of(pal.Map.Spec.Seed, id, 0x7A105);
                float lx = (gi + Hash.Unit(roll, 0)) * ScatterCell, lz = (gj + Hash.Unit(roll, 1)) * ScatterCell;
                int i = Math.Clamp((int)MathF.Round(lx / step), 0, grid.Nx - 1), j = Math.Clamp((int)MathF.Round(lz / step), 0, grid.Nz - 1);
                int s = grid.Sample(i, j);
                if (river[s] || height[s] < 0.6f || up[j * grid.Nx + i] < FloraMeshes.MinUp(fallen)) continue;
                Vector2 under = relief.At(lx, lz, height[s]);
                // (A face above it: 14 m or more within the lattice's 16 m reach. A hillside's valley floor isn't one.)
                float lies = Smooth(14f, 34f, under.X) * (1f - Smooth(1.5f, 4f, under.Y));
                if (Hash.Unit(roll, 2) >= lies * 0.75f) continue;
                double x = x0 + lx, z = z0 + lz;
                if (source.Built(x, z, out bool onRoad) > 0f || onRoad) continue;
                int cx = (int)Math.Floor(x), cz = (int)Math.Floor(z);
                if (Dug(dug, cx, cz)) continue;
                float h = source.SurfaceHeight(x, z);
                if (source.CellDensity(cx, (int)Math.Floor(h - 0.6f), cz) <= 0f) continue;
                float big = Hash.Unit(roll, 3);
                float scale = Mathx.Lerp(0.45f, 1.5f, big * big) * Mathx.Lerp(0.7f, 1.25f, Smooth(10f, 40f, under.X)) * FloraMeshes.BaseScale(fallen);
                if (source.Reserved(x, z, Reach(fallen, null, scale))) continue;
                float angle = Hash.Unit(roll, 4) * Mathf.Tau;
                float foot = FootRadius(fallen) * scale;
                float gx = (source.SurfaceHeight(x + foot, z) - h) / foot, gz = (source.SurfaceHeight(x, z + foot) - h) / foot;
                float slope = MathF.Sqrt(gx * gx + gz * gz), lean = MathF.Min(slope, MaxRockLean);
                float rootY = h - FloraSolidRules.Sink - (slope - lean) * foot;
                var basis = new Basis(Vector3.Up, angle).Scaled(new Vector3(scale, scale, scale));
                if (lean > 0.01f && slope > 0f) basis = new Basis(new Vector3(-gz, 0f, gx) / slope, -MathF.Atan(lean)) * basis;
                Rgb land = pal.SoilAt(x, z, out _);
                Rgb tint = Rgb.Lerp(pal.Rock, land, 0.12f) * Mathx.Lerp(0.85f, 1.08f, Hash.Unit(roll, 5));
                Add(fallen, null, 0, lx, rootY, lz, basis, tint, 0f, far: false);
                solids?.MakeAll(fallen, null, 0, id, new Vector3((float)x, rootY, (float)z), scale, angle, job.Solids);
            }
        }
        foreach (var (key, list) in buffers) job.Flora.Add(new FloraBatch(key.Kind, key.Plant, key.Variant, list.ToArray(), list.Count / 16, key.Far, key.Distant));

        void Add(Flora kind, Plant plant, int variant, float lx, float y, float lz, Basis basis, Rgb tint, float glow, bool far, bool distant = false)
        {
            taken[current] = true;
            var key = (Kind: kind, Plant: plant?.Index ?? -1, Variant: plant == null ? 0 : variant, Far: far, Distant: distant);
            if (!buffers.TryGetValue(key, out List<float> list)) buffers[key] = list = new List<float>();
            // MultiMesh layout: a 3x4 row-major transform, then four custom floats (tint and glow).
            list.Add(basis.X.X); list.Add(basis.Y.X); list.Add(basis.Z.X); list.Add(lx);
            list.Add(basis.X.Y); list.Add(basis.Y.Y); list.Add(basis.Z.Y); list.Add(y);
            list.Add(basis.X.Z); list.Add(basis.Y.Z); list.Add(basis.Z.Z); list.Add(lz);
            list.Add(tint.R); list.Add(tint.G); list.Add(tint.B); list.Add(glow);
        }
    }

    /// <summary>
    /// Each kind's own low mesh, its height and crown radius (the streamer sets them): a distant drawing
    /// is its kind's mesh stretched to the plant's own size, so a chunk draws one batch a kind, not one a species.
    /// </summary>
    public static IReadOnlyDictionary<Flora, (float Height, float Radius)> KindSize;

    /// <summary>A distant drawing's turn and size: the kind's mesh made the plant's height and crown, a little bigger (<see cref="DetailReach.DistantGrow"/>).</summary>
    public static Basis DistantBasis(Flora kind, Plant plant, Basis basis)
    {
        Basis grown = basis.Scaled(Vector3.One * DetailReach.DistantGrow);
        if (plant == null || KindSize == null || !KindSize.TryGetValue(kind, out var k) || k.Height <= 0f || k.Radius <= 0f) return grown;
        float up = plant.Height / k.Height, out_ = MathF.Max(plant.CrownRadius, plant.Height * 0.2f) / k.Radius;
        return grown * Basis.FromScale(new Vector3(out_, up, out_));
    }

    /// <summary>Whether a scatter cell's large plant, if one grows there, is of the share with a distant drawing.</summary>
    static bool Distant(GroundPalette pal, long id) => Hash.Unit(Hash.Of(pal.Map.Spec.Seed, id, 0xD157A), 0) < DetailReach.DistantShare;

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

    /// <summary>How far a plant or rock reaches from its root at a scale: its crown, or its body (a kind's crown where the species is not known).</summary>
    static float Reach(Flora kind, Plant plant, float scale) =>
        scale * MathF.Max(FootRadius(kind), plant?.CrownRadius ?? (KindSize != null && KindSize.TryGetValue(kind, out var k) ? k.Radius : 0f));

    /// <summary>Whether anything built lies under a crown's reach of a point: four spots round its rim (a road, or a place's ground well in from its blend).</summary>
    static bool CrownOver(PlanetTerrainSource source, GroundPalette pal, double x, double z, float reach)
    {
        double w = pal.Map.WidthMeters, h = pal.Map.HeightMeters;
        bool Over(double px, double pz) => source.Built(px, pz, out bool road) >= 0.5f || road;
        return Over(Mathx.WrapX(x + reach, w), z) || Over(Mathx.WrapX(x - reach, w), z) || Over(x, Mathx.Wrap(z + reach, h)) || Over(x, Mathx.Wrap(z - reach, h));
    }

    /// <summary>A plant's own roll for how far it may come into a place's ground, unrelated to its other rolls.</summary>
    internal static float Feather(float roll) => roll * 7.31f % 1f;

    /// <summary>
    /// Whether the ground is open under a point's surface: a place's room or stairwell dug down from it
    /// (its stamp's carve), or a cave mouth. Nothing that lies on the ground, and no water, goes there.
    /// </summary>
    public static bool Opened(PlanetTerrainSource source, double x, double z, float ground) =>
        source.CellDensity((int)Math.Floor(x), (int)Math.Floor(ground - 0.6f), (int)Math.Floor(z)) <= 0f;

    /// <summary>
    /// Whether ground a plant's leaves would lie on is open anywhere round it, out to its reach (only
    /// round a place, where the ground is dug: elsewhere a cave mouth under the root is enough to know).
    /// </summary>
    public static bool Overhangs(PlanetTerrainSource source, double x, double z, float ground, float reach)
    {
        if (!NearDug(source, x, z, reach + 1f)) return false;
        foreach (var (dx, dz) in new[] { (reach, 0f), (-reach, 0f), (0f, reach), (0f, -reach) })
        {
            double px = x + dx, pz = z + dz;
            if (Opened(source, px, pz, source.SurfaceHeight(px, pz))) return true;
        }
        return false;
    }

    /// <summary>Whether a point lies within a margin of the footprint of a place that digs into the ground.</summary>
    static bool NearDug(PlanetTerrainSource source, double x, double z, float margin)
    {
        foreach (int i in source.StampsAt(x, z))
        {
            TerrainStamp st = source.Stamps[i];
            if (st.Shape == StampShape.Road || st.CarveBottom >= st.PadY && st.Shape != StampShape.Buried) continue;
            st.Offset(x, z, out double dx, out double dz);
            if (Math.Abs(dx) <= (st.MaxX - st.MinX) * 0.5 + margin + 1 && Math.Abs(dz) <= (st.MaxZ - st.MinZ) * 0.5 + margin + 1) return true;
        }
        return false;
    }

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
