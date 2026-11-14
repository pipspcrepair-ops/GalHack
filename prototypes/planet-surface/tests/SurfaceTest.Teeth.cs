using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using GalHack.Terrain;
using Godot;
using Planets.Gen;
using PlanetSurface.Terrain;
using PlanetSurface.World;
using Archetype = Planets.Gen.Archetype;

namespace PlanetSurface;

/// <summary>
/// Cliffs read as cleanly far off as the old far meshes drew them: on fresh worlds of mesas and buttes, round the steepest
/// cliffs, the drawn edge along each cliff's top, its middle and its foot (where the drawn ground crosses those heights) is
/// traced on a quarter-metre grid for the GPU rings (2, 4 and 8 m) and for the far meshes the GPU rings replaced (the same
/// shaping in 128 m chunks, as they were built). A tooth is a sharp turn of that edge (a fin or a notch a grid square
/// wide) where the land's own line makes none. The rings may not have more of them than the old meshes.
/// </summary>
public partial class SurfaceTest
{
    async Task TeethChecks()
    {
        var rand = new Random();
        const int Half = 32;
        const float Cell = 0.25f;
        int n = (int)(2 * Half / Cell) + 1;
        var teeth = new int[3, 2];      // by level: the old meshes', the rings'
        float length = 0f;
        int sites = 0;
        foreach (int variant in new[] { 1, 0 })
        {
            var spec = PlanetSpec.Roll((ulong)rand.NextInt64(), Archetype.Arid, variant);
            PlanetWorld w = await Task.Run(() => PlanetWorld.Build(spec, Main.Catalog));
            var rings = Enumerable.Range(0, 3).Select(l => new ClipGround.Reader(l, w.Source, w.Palette, (int)w.Width, (int)w.Height)).ToArray();
            var olds = Enumerable.Range(0, 3).Select(l => new OldFar(w, l)).ToArray();
            foreach (var (cx, cz) in Cliffs(w, rand, 3))
            {
                sites++;
                var origin = new Vector2((float)cx - Half, (float)cz - Half);
                var truth = new float[n * n];
                for (int j = 0; j < n; j++)
                for (int i = 0; i < n; i++)
                    truth[j * n + i] = w.Source.SurfaceHeight(Mathx.WrapX(origin.X + i * Cell, w.Width), Mathx.Wrap(origin.Y + j * Cell, w.Height));
                var sorted = truth.OrderBy(h => h).ToArray();
                float lo = sorted[sorted.Length / 20], hi = sorted[sorted.Length * 19 / 20];
                foreach (float share in new[] { 0.15f, 0.5f, 0.85f })
                {
                    float at = lo + (hi - lo) * share;
                    var land = Contours(truth.Select(h => h - at).ToArray(), n, Cell, origin);
                    var corners = land.SelectMany(line => SharpTurns(line, 1f)).ToList();
                    length += land.Sum(line => Resample(line, 1f).Count);
                    for (int l = 0; l < 3; l++)
                    {
                        float step = ClipGround.Spacing(l);
                        for (int which = 0; which < 2; which++)
                        {
                            var drawn = new float[n * n];
                            for (int j = 0; j < n; j++)
                            for (int i = 0; i < n; i++)
                            {
                                double x = Mathx.WrapX(origin.X + i * Cell, w.Width), z = Mathx.Wrap(origin.Y + j * Cell, w.Height);
                                drawn[j * n + i] = (which == 0 ? olds[l].Height(x, z) : rings[l].Height(x, z)) - at;
                            }
                            // A turn where the land's own line turns too is the land's.
                            foreach (var line in Contours(drawn, n, Cell, origin))
                                teeth[l, which] += SharpTurns(line, step).Count(t => t.X > origin.X + 2 && t.Y > origin.Y + 2 && t.X < origin.X + 2 * Half - 2 && t.Y < origin.Y + 2 * Half - 2
                                    && !corners.Any(c => c.DistanceTo(t) <= step * 1.5f));
                        }
                    }
                }
                await Frames(1);
            }
        }
        float per = 100f / Math.Max(length, 1f);
        for (int l = 0; l < 3; l++)
        {
            float old = teeth[l, 0] * per, ring = teeth[l, 1] * per;
            Check(sites > 0 && ring <= old * 1.15f + 0.5f,
                $"cliff teeth, ring {l} ({ClipGround.Spacing(l):F0} m): {ring:F2} a hundred metres of cliff edge against the old far meshes' {old:F2} ({teeth[l, 1]} and {teeth[l, 0]} over {length:F0} m of edge at the top, middle and foot of {sites} cliffs)");
        }
    }

    /// <summary>The points where a line turns more sharply than 60 degrees between samples a chord apart.</summary>
    static List<Vector2> SharpTurns(List<Vector2> line, float chord)
    {
        var turns = new List<Vector2>();
        List<Vector2> p = Resample(line, chord);
        for (int k = 0; k + 2 < p.Count; k++)
        {
            Vector2 a = (p[k + 1] - p[k]).Normalized(), b = (p[k + 2] - p[k + 1]).Normalized();
            if (Mathf.RadToDeg(MathF.Acos(Math.Clamp(a.Dot(b), -1f, 1f))) > 60f) turns.Add(p[k + 1]);
        }
        return turns;
    }

    /// <summary>The steepest cliffs within 3 km of the landing (a rise of 20 m or more within 16 m), 400 m apart.</summary>
    static List<(double X, double Z)> Cliffs(PlanetWorld w, Random rand, int count)
    {
        var found = new List<(double X, double Z, float Rise)>();
        for (int tries = 0; tries < 6000; tries++)
        {
            double x = w.Landing.X + (rand.NextDouble() - 0.5) * 6000, z = w.Landing.Y + (rand.NextDouble() - 0.5) * 6000;
            float h = w.Source.SurfaceHeight(Mathx.WrapX(x, w.Width), Mathx.Wrap(z, w.Height)), rise = 0f;
            foreach (var (dx, dz) in new[] { (16, 0), (-16, 0), (0, 16), (0, -16) })
                rise = MathF.Max(rise, w.Source.SurfaceHeight(Mathx.WrapX(x + dx, w.Width), Mathx.Wrap(z + dz, w.Height)) - h);
            if (rise > 20f && h > 1f) found.Add((x, z, rise));
        }
        var cliffs = new List<(double, double)>();
        foreach (var f in found.OrderByDescending(f => f.Rise))
        {
            if (cliffs.Count >= count) break;
            if (cliffs.All(c => Math.Abs(c.Item1 - f.X) + Math.Abs(c.Item2 - f.Z) > 400)) cliffs.Add((f.X, f.Z));
        }
        return cliffs;
    }

    /// <summary>The far meshes the GPU rings replaced: a level's 128 m chunks shaped as they were built, each square split by the ground.</summary>
    sealed class OldFar(PlanetWorld w, int lod)
    {
        readonly Dictionary<(int, int), FarBuilder.Surface> _chunks = new();
        readonly float _step = FarBuilder.Step(lod);

        FarBuilder.Surface Chunk(int fx, int fz)
        {
            if (_chunks.TryGetValue((fx, fz), out var surface)) return surface;
            int span = FarBuilder.Size, nx = (int)(span / _step) + 1;
            var grid = new FarBuilder.Grid(nx, nx);
            int g = grid.G;
            double x0 = fx * (double)FarBuilder.Size, z0 = fz * (double)FarBuilder.Size;
            var height = new float[g * g];
            var river = new bool[g * g];
            for (int j = 0; j < g; j++)
            for (int i = 0; i < g; i++)
            {
                GroundSample s = w.Source.Ground(x0 + (i - 1) * _step, z0 + (j - 1) * _step, _step);
                (height[j * g + i], river[j * g + i]) = (s.Height, s.InRiver);
            }
            return _chunks[(fx, fz)] = FarBuilder.Shape(grid, span, span, _step, x0, z0, height, river, w.Source, w.Palette, 0);
        }

        /// <summary>The chunk's triangles over a point (canonical): the square under it and those round it, as a slide may move one.</summary>
        public float Height(double x, double z)
        {
            int fx = (int)Math.Floor(x / FarBuilder.Size), fz = (int)Math.Floor(z / FarBuilder.Size);
            FarBuilder.Surface s = Chunk(fx, fz);
            int nx = (int)(FarBuilder.Size / _step) + 1;
            float u = (float)(x - fx * (double)FarBuilder.Size), v = (float)(z - fz * (double)FarBuilder.Size);
            int ci = (int)(u / _step), cj = (int)(v / _step);
            for (int dj = -1; dj <= 1; dj++)
            for (int di = -1; di <= 1; di++)
            {
                int i = Math.Clamp(ci + di, 0, nx - 2), j = Math.Clamp(cj + dj, 0, nx - 2);
                int a = j * nx + i, b = a + 1, c = a + nx, d = c + 1;
                Vector3 pa = s.Vertices[a], pb = s.Vertices[b], pc = s.Vertices[c], pd = s.Vertices[d];
                float? y = s.Split[j * (nx - 1) + i] == 0 ? Tri(pa, pb, pc, u, v) ?? Tri(pb, pd, pc, u, v) : Tri(pa, pb, pd, u, v) ?? Tri(pa, pd, pc, u, v);
                if (y is float h) return h;
            }
            return s.Vertices[Math.Clamp(cj, 0, nx - 1) * nx + Math.Clamp(ci, 0, nx - 1)].Y;
        }

        static float? Tri(Vector3 a, Vector3 b, Vector3 c, float x, float z)
        {
            float det = (b.X - a.X) * (c.Z - a.Z) - (c.X - a.X) * (b.Z - a.Z);
            if (MathF.Abs(det) < 1e-6f) return null;
            float l1 = ((b.X - x) * (c.Z - z) - (c.X - x) * (b.Z - z)) / det, l2 = ((c.X - x) * (a.Z - z) - (a.X - x) * (c.Z - z)) / det, l3 = 1f - l1 - l2;
            if (l1 < -1e-4f || l2 < -1e-4f || l3 < -1e-4f) return null;
            return l1 * a.Y + l2 * b.Y + l3 * c.Y;
        }
    }
}
