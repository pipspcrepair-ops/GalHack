using System;
using System.Collections.Generic;
using GalHack.Terrain;
using Godot;
using Planets.Gen;

namespace PlanetSurface.Terrain;

/// <summary>
/// The roads as the terrain shader draws them: every road stamp's ground stretches as line segments,
/// with a grid of 16 m cells listing the segments that can touch each. The shader finds the nearest
/// segment to each pixel and paints the road from its exact distance, so a road has the same clean
/// edge, markings and wear at any angle, on any level of detail, near or 400 m up. (Vertex colours
/// at the mesh's 1 to 8 m spacing drew roads as chains of blotches.) Bridges and tunnels are left out:
/// their decks are meshes, and the ground under them is not road.
///
/// Segments are four texels each in <see cref="Segments"/> (1,024 to a row):
///   0: a.x, a.z (canonical), b - a (x, z)
///   1: deck at a, deck at b, distance along the road at a, at b
///   2: half width, wear, surface, style + 8 x road id
///   3: how much a place's pad covers a and b (markings stop there), whether a and b lie on a place's footprint (the road stops there)
/// <see cref="Cells"/> holds each cell's first entry in <see cref="Index"/> and its count; the index lists segments.
/// </summary>
public sealed class RoadField
{
    public const float Cell = 16f;
    public const int SegmentsPerRow = 1024, IndexRow = 4096;

    /// <summary>How far past a road's deck the shader can paint (its shoulder and the edge's wander), m.</summary>
    public const float Reach = 4f;

    /// <summary>Road styles: how its markings run.</summary>
    public const int Street = 0, Highway = 1, Track = 2, Rail = 3, Drive = 4;

    public ImageTexture Segments, Cells, Index;
    /// <summary>Cell size, cells across, cells down, segment count.</summary>
    public Vector4 Grid;
    public int SegmentCount, Entries;

    float[] _segs, _cells, _index;
    double _width, _height;

    readonly record struct Point(double X, double Z, float Y, float S, float Pad, float Core);

    public static RoadField Build(PlanetTerrainSource source, double width, double height)
    {
        var segs = new List<float>();
        string lastName = null;
        double lastX = double.NaN, lastZ = double.NaN;
        float along = 0f;
        int id = 0, n = 0;
        var run = new List<Point>();
        foreach (TerrainStamp t in source.Stamps)
        {
            if (t.Shape != StampShape.Road || t.PathCount < 2) continue;
            int style = StyleOf(t.Name, t.HalfWidth);
            var (fx, fz, _) = t.PathPoint(0);
            double x0 = t.CenterX + fx, z0 = t.CenterZ + fz;
            // A route is laid in stretches of stamps, end to start: one road, measured along from its start.
            bool continues = t.Name == lastName && Math.Abs(Mathx.WrapDelta(x0 - lastX, width)) < 0.01 && Math.Abs(Mathx.WrapDelta(z0 - lastZ, height)) < 0.01;
            if (!continues) { id++; along = 0f; }
            run.Clear();
            void Flush()
            {
                if (run.Count >= 2) n += Emit(segs, run, t, style, id, width, height);
                run.Clear();
            }
            for (int i = 0; i < t.PathCount; i++)
            {
                var (px, pz, py) = t.PathPoint(i);
                double x = t.CenterX + px, z = t.CenterZ + pz;
                if (i > 0)
                {
                    var (qx, qz, _) = t.PathPoint(i - 1);
                    along += MathF.Sqrt((px - qx) * (px - qx) + (pz - qz) * (pz - qz));
                }
                // A run of ground stretches ends where the road takes to a bridge or a tunnel.
                bool before = i > 0 && t.PathSpan(i - 1) == RoadSpan.Ground, after = i < t.PathCount - 1 && t.PathSpan(i) == RoadSpan.Ground;
                if (!before) Flush();
                if (before || after) run.Add(Covered(source, x, z, py, along, width, height));
            }
            Flush();
            lastName = t.Name;
            lastX = t.CenterX + t.PathPoint(t.PathCount - 1).X;
            lastZ = t.CenterZ + t.PathPoint(t.PathCount - 1).Z;
        }
        return Pack(segs, n, width, height);
    }

    static int StyleOf(string name, float halfWidth)
    {
        if (name.StartsWith("Highway")) return Highway;
        if (name.StartsWith("Rail")) return Rail;
        if (name.StartsWith("Track")) return Track;
        if (name.StartsWith("Road")) return halfWidth >= 3.5f ? Street : Track;
        return Drive;
    }

    /// <summary>A road point and how the places' pads lie over it.</summary>
    static Point Covered(PlanetTerrainSource source, double x, double z, float y, float s, double width, double height)
    {
        double cx = Mathx.WrapX(x, width), cz = Mathx.Wrap(z, height);
        float pad = 0f, core = 0f;
        foreach (int i in source.StampsAt(cx, cz))
        {
            TerrainStamp p = source.Stamps[i];
            if (p.Shape != StampShape.Pad) continue;
            pad = MathF.Max(pad, p.Weight(cx, cz));
            if (p.OnFootprint(cx - 0.5, cz - 0.5)) core = 1f;
        }
        return new Point(x, z, y, s, pad, core);
    }

    /// <summary>A run of ground points as few segments as keep the line within 3 cm (and the deck within 20 cm): straights become one segment.</summary>
    static int Emit(List<float> segs, List<Point> run, TerrainStamp t, int style, int id, double width, double height)
    {
        int count = 0;
        var keep = new List<int> { 0 };
        Simplify(run, 0, run.Count - 1, keep);
        keep.Add(run.Count - 1);
        keep.Sort();
        for (int k = 0; k + 1 < keep.Count; k++)
        {
            Point a = run[keep[k]], b = run[keep[k + 1]];
            double ax = Mathx.WrapX(a.X, width), az = Mathx.Wrap(a.Z, height);
            segs.Add((float)ax); segs.Add((float)az); segs.Add((float)(b.X - a.X)); segs.Add((float)(b.Z - a.Z));
            segs.Add(a.Y); segs.Add(b.Y); segs.Add(a.S); segs.Add(b.S);
            segs.Add(t.HalfWidth); segs.Add(t.Wear); segs.Add((float)t.Surface); segs.Add(style + 8f * id);
            segs.Add(a.Pad); segs.Add(b.Pad); segs.Add(a.Core); segs.Add(b.Core);
            count++;
        }
        return count;
    }

    static void Simplify(List<Point> run, int a, int b, List<int> keep)
    {
        if (b - a < 2) return;
        Point p = run[a], q = run[b];
        double ex = q.X - p.X, ez = q.Z - p.Z, len = Math.Sqrt(ex * ex + ez * ez);
        float worst = 1f;
        int at = -1;
        for (int i = a + 1; i < b; i++)
        {
            Point r = run[i];
            float u = (float)((r.S - p.S) / Math.Max(1e-6f, q.S - p.S));
            double side = len > 1e-6 ? Math.Abs((r.X - p.X) * ez - (r.Z - p.Z) * ex) / len : 0;
            float err = MathF.Max((float)side / 0.03f, MathF.Abs(r.Y - (p.Y + (q.Y - p.Y) * u)) / 0.2f);
            err = MathF.Max(err, MathF.Max(MathF.Abs(r.Pad - (p.Pad + (q.Pad - p.Pad) * u)), MathF.Abs(r.Core - (p.Core + (q.Core - p.Core) * u))) / 0.1f);
            if (err > worst) { worst = err; at = i; }
        }
        // Long segments are split anyway, so a cell's list stays short and the deck follows the grade.
        if (at < 0 && q.S - p.S > 96f) at = (a + b) / 2;
        if (at < 0) return;
        keep.Add(at);
        Simplify(run, a, at, keep);
        Simplify(run, at, b, keep);
    }

    static RoadField Pack(List<float> segs, int n, double width, double height)
    {
        int cellsX = (int)Math.Ceiling(width / Cell), cellsZ = (int)Math.Ceiling(height / Cell);
        var lists = new Dictionary<int, List<int>>();
        var seen = new HashSet<int>();
        for (int s = 0; s < n; s++)
        {
            int o = s * 16;
            float ax = segs[o], az = segs[o + 1], ex = segs[o + 2], ez = segs[o + 3], reach = segs[o + 8] + Reach;
            float x0 = MathF.Min(ax, ax + ex) - reach, x1 = MathF.Max(ax, ax + ex) + reach;
            float z0 = MathF.Min(az, az + ez) - reach, z1 = MathF.Max(az, az + ez) + reach;
            float len2 = MathF.Max(ex * ex + ez * ez, 1e-6f);
            seen.Clear();
            // Every cell the reach touches: sampled every half cell, so even the seam's narrow last cell is found.
            for (float z = z0; ; z = MathF.Min(z + Cell * 0.5f, z1))
            {
                for (float x = x0; ; x = MathF.Min(x + Cell * 0.5f, x1))
                {
                    double wx = Mathx.WrapX(x, width), wz = Mathx.Wrap(z, height);
                    int cx = Math.Min(cellsX - 1, (int)(wx / Cell)), cz = Math.Min(cellsZ - 1, (int)(wz / Cell));
                    int key = cz * cellsX + cx;
                    // Only cells the segment's reach really crosses (from the cell's middle).
                    float mx = x - (float)(wx - (cx + 0.5) * Cell), mz = z - (float)(wz - (cz + 0.5) * Cell);
                    float u = Math.Clamp(((mx - ax) * ex + (mz - az) * ez) / len2, 0f, 1f);
                    float dx = ax + ex * u - mx, dz = az + ez * u - mz;
                    if (MathF.Sqrt(dx * dx + dz * dz) <= reach + Cell * 0.7072f && seen.Add(key))
                    {
                        if (!lists.TryGetValue(key, out List<int> list)) lists[key] = list = new List<int>(4);
                        list.Add(s);
                    }
                    if (x >= x1) break;
                }
                if (z >= z1) break;
            }
        }

        int entries = 0;
        foreach (List<int> l in lists.Values) entries += l.Count;
        var cells = new float[cellsX * cellsZ * 2];
        var index = new float[Math.Max(1, (entries + IndexRow - 1) / IndexRow) * IndexRow];
        int next = 0;
        foreach (var (key, list) in lists)
        {
            cells[key * 2] = next;
            cells[key * 2 + 1] = list.Count;
            foreach (int s in list) index[next++] = s;
        }
        int rows = Math.Max(1, (n + SegmentsPerRow - 1) / SegmentsPerRow);
        var data = new float[rows * SegmentsPerRow * 16];
        segs.CopyTo(data);
        return new RoadField
        {
            _segs = data, _cells = cells, _index = index, _width = width, _height = height,
            Segments = Texture(data, SegmentsPerRow * 4, rows, Image.Format.Rgbaf, 16),
            Cells = Texture(cells, cellsX, cellsZ, Image.Format.Rgf, 8),
            Index = Texture(index, IndexRow, index.Length / IndexRow, Image.Format.Rf, 4),
            Grid = new Vector4(Cell, cellsX, cellsZ, n),
            SegmentCount = n,
            Entries = entries,
        };
    }

    /// <summary>
    /// What the terrain shader finds at a point, worked the same way (tests): how far past the nearest
    /// road's deck edge it is (negative on the deck; infinity: no road within reach), and how far from that road's middle.
    /// </summary>
    public float EdgeAt(double x, double z, float y, float tolerance, out float across)
    {
        across = float.PositiveInfinity;
        float best = float.PositiveInfinity;
        if (SegmentCount == 0) return best;
        double cx = Mathx.WrapX(x, _width), cz = Mathx.Wrap(z, _height);
        int cellX = Math.Min((int)Grid.Y - 1, (int)(cx / Cell)), cellZ = Math.Min((int)Grid.Z - 1, (int)(cz / Cell));
        int cell = cellZ * (int)Grid.Y + cellX, first = (int)_cells[cell * 2], count = Math.Min((int)_cells[cell * 2 + 1], 64);
        for (int k = 0; k < count; k++)
        {
            int o = (int)_index[first + k] * 16;
            float rx = (float)Mathx.WrapDelta(cx - _segs[o], _width), rz = (float)Mathx.WrapDelta(cz - _segs[o + 1], _height);
            float ex = _segs[o + 2], ez = _segs[o + 3];
            float u = Math.Clamp((rx * ex + rz * ez) / MathF.Max(ex * ex + ez * ez, 1e-6f), 0f, 1f);
            float d = MathF.Sqrt((rx - ex * u) * (rx - ex * u) + (rz - ez * u) * (rz - ez * u));
            float edge = d - _segs[o + 8];
            if (edge > Reach || MathF.Abs(y - (_segs[o + 4] + (_segs[o + 5] - _segs[o + 4]) * u)) > tolerance + MathF.Max(edge, 0f) * 1.2f) continue;
            if (edge < best) { best = edge; across = d; }
        }
        return best;
    }

    static ImageTexture Texture(float[] values, int w, int h, Image.Format format, int bytesPerTexel)
    {
        var bytes = new byte[w * h * bytesPerTexel];
        Buffer.BlockCopy(values, 0, bytes, 0, bytes.Length);
        return ImageTexture.CreateFromImage(Image.CreateFromData(w, h, false, format, bytes));
    }
}
