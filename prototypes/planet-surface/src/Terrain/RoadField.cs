using System;
using System.Collections.Generic;
using System.Linq;
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
/// Segments are eight texels each in <see cref="Segments"/> (512 to a row):
///   0: a.x, a.z (canonical), b - a (x, z)
///   1: deck at a, deck at b, distance along the road at a, at b
///   2: half width, wear, surface, style + 16 x road id
///   3: how much a place's pad covers a and b (markings stop there), whether a and b lie on a place's footprint (the road stops there)
///   4: how much a pad covers any of the deck's width at a and b (the ground there may be the pad's, not the deck's),
///      and whether a and b lie near an end that runs into a place's lot (where it leaves a street, that is a crossover)
///   5: the junction form a and b lie near (the network's JunctionForm, at the nearer end within 80 m; 0 none),
///      and how far along the road that junction is from a and from b (positive ahead the way it was laid, negative behind)
///   6: the median dividing a highway's carriageways (0 none, 1 grass, 2 barrier, 3 grown over), then unused
///   7: whether the stretch's start or end is a way's end lying on another way's deck (a junction's branch, a drive):
///      it ends there flat, square across, inside that deck (a round end poked out past its far kerb as a bump); 0, 0
/// A junction's rounded corner (<see cref="PlanetSurface.Infra.RoadCorner"/>) is an entry of its own, style <see cref="Corner"/>:
///   0: its centre (canonical), the tilt of its plane (rise per metre in x and z: a turning bulb on sloping land)
///   1: deck height, deck height, radius, cut (how far from the centre its deck reaches)
///   2: cut (its reach, for the cells), 0, 0, style + 16 x 0
///   3: unit vectors from the centre to where it meets each kerb line (a, b)
///   4: how far along each of those it may reach, kerbed, 0
/// <see cref="Cells"/> holds each cell's first entry in <see cref="Index"/> and its count; the index lists segments.
/// </summary>
public sealed class RoadField
{
    public const float Cell = 16f;
    public const int SegmentsPerRow = 512, IndexRow = 4096, Texels = 8, Stride = Texels * 4;

    /// <summary>How far past a road's deck the shader can paint (its shoulder and the edge's wander), m.</summary>
    public const float Reach = 8f;

    /// <summary>Road styles: how its markings run (plus <see cref="Town"/> on a town's street).</summary>
    public const int Street = 0, Highway = 1, Track = 2, Rail = 3, Drive = 4, Graded = 5, Ring = 6, Corner = 7;

    public ImageTexture Segments, Cells, Index;
    /// <summary>Cell size, cells across, cells down, segment count.</summary>
    public Vector4 Grid;
    public int SegmentCount, Entries;
    /// <summary>The junctions' rounded corners drawn with the roads.</summary>
    public IReadOnlyList<PlanetSurface.Infra.RoadCorner> Corners = Array.Empty<PlanetSurface.Infra.RoadCorner>();

    float[] _segs, _cells, _index;
    double _width, _height;

    readonly record struct Point(double X, double Z, float Y, float S, float Pad, float Core, float Wide, float Lot = 0f, float JForm = 0f, float JDist = 0f, float PaintHw = 0f, float Seam = 0f, float Flat = 0f);

    /// <summary>How far from an end that runs into a place's lot a road counts as that lot's way in (a crossover where it leaves the street), m.</summary>
    const float LotReach = 30f;

    public static RoadField Build(PlanetTerrainSource source, double width, double height, IReadOnlyList<PlanetSurface.Infra.RoadCorner> corners = null)
    {
        var segs = new List<float>();
        string lastName = null;
        double lastX = double.NaN, lastZ = double.NaN;
        float along = 0f;
        int id = 0, n = 0;
        var run = new List<Point>();
        var runs = new List<(List<Point> Points, TerrainStamp Stamp, int Style, int Id)>();
        foreach (TerrainStamp t in source.Stamps)
        {
            if (t.Shape != StampShape.Road || t.PathCount < 2 || t.Name.StartsWith("Corner")) continue;
            int style = StyleOf(t.Name, t.HalfWidth);
            var (fx, fz, _) = t.PathPoint(0);
            double x0 = t.CenterX + fx, z0 = t.CenterZ + fz;
            // A route is laid in stretches of stamps, end to start: one road, measured along from its start.
            bool continues = t.Name == lastName && Math.Abs(Mathx.WrapDelta(x0 - lastX, width)) < 0.01 && Math.Abs(Mathx.WrapDelta(z0 - lastZ, height)) < 0.01;
            if (!continues) { id++; along = 0f; }
            run.Clear();
            void Flush()
            {
                if (run.Count >= 2) runs.Add((new List<Point>(run), t, style, id));
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
                if (before || after)
                {
                    // Across the deck: square to the way the road runs here, from its neighbours.
                    var (ax, az, _) = t.PathPoint(Math.Max(0, i - 1));
                    var (bx, bz, _) = t.PathPoint(Math.Min(t.PathCount - 1, i + 1));
                    float dx = bx - ax, dz = bz - az, dl = MathF.Max(1e-4f, MathF.Sqrt(dx * dx + dz * dz));
                    run.Add(Covered(source, x, z, py, along, -dz / dl, dx / dl, t.HalfWidth, width, height));
                }
            }
            Flush();
            lastName = t.Name;
            lastX = t.CenterX + t.PathPoint(t.PathCount - 1).X;
            lastZ = t.CenterZ + t.PathPoint(t.PathCount - 1).Z;
        }
        // A roundabout's island, from its ring's own path (its middle and its radius less half its width): the ways
        // through it stop at the ring, their decks not drawn over the island.
        var islands = new List<(double X, double Z, float R)>();
        foreach (TerrainStamp t in source.Stamps)
        {
            if (t.Shape != StampShape.Road || !t.Name.StartsWith("Ring") || t.PathCount < 8) continue;
            double sx = 0, sz = 0;
            for (int i = 0; i < t.PathCount; i++) { var (px, pz, _) = t.PathPoint(i); sx += px; sz += pz; }
            sx /= t.PathCount; sz /= t.PathCount;
            double rr = 0;
            for (int i = 0; i < t.PathCount; i++) { var (px, pz, _) = t.PathPoint(i); rr += Math.Sqrt((px - sx) * (px - sx) + (pz - sz) * (pz - sz)); }
            islands.Add((t.CenterX + sx, t.CenterZ + sz, (float)(rr / t.PathCount) - t.HalfWidth - 0.5f));
        }
        // Each road's ends: one that runs onto a place's footprint is a lane into its lot, and near it the road is that lot's way in.
        var ends = new Dictionary<int, (bool Start, bool End, float Length)>();
        foreach (var (pts, _, _, rid) in runs)
        {
            var e = ends.TryGetValue(rid, out var have) ? have : (Start: pts[0].Core > 0.5f && pts[0].S < 0.01f, End: false, Length: 0f);
            ends[rid] = (e.Start, pts[^1].Core > 0.5f, Math.Max(e.Length, pts[^1].S));
        }
        // Each way's own two ends: one lying on another way's deck, away from that way's ends (not a way running on into
        // another), ends flat.
        var firsts = new Dictionary<int, (double X, double Z)>();
        var lasts = new Dictionary<int, (double X, double Z)>();
        foreach (var (pts, _, _, rid) in runs)
        {
            if (!firsts.ContainsKey(rid) && pts[0].S < 0.01f) firsts[rid] = (pts[0].X, pts[0].Z);
            lasts[rid] = (pts[^1].X, pts[^1].Z);
        }
        // How many ways end at a point (three or more ending at one point make a junction, whose ends all end flat).
        int EndsAt(double x, double z)
        {
            int n = 0;
            foreach (var (id, e) in firsts.Concat(lasts))
                if (Math.Abs(Mathx.WrapDelta(e.X - x, width)) < 1.5 && Math.Abs(Mathx.WrapDelta(e.Z - z, height)) < 1.5) n++;
            return n;
        }
        bool OnAnother(int rid, double x, double z)
        {
            bool meeting = EndsAt(x, z) >= 3;
            foreach (var (pts, t, _, other) in runs)
            {
                if (other == rid) continue;
                for (int i = 0; i + 1 < pts.Count; i++)
                {
                    double ax = Mathx.WrapDelta(pts[i].X - x, width), az = Mathx.WrapDelta(pts[i].Z - z, height);
                    double ex = pts[i + 1].X - pts[i].X, ez = pts[i + 1].Z - pts[i].Z, l2 = ex * ex + ez * ez;
                    if (Math.Abs(ax) > 40 || Math.Abs(az) > 40) continue;
                    double u = l2 < 1e-9 ? 0 : Math.Clamp(-(ax * ex + az * ez) / l2, 0, 1);
                    double px = ax + ex * u, pz = az + ez * u;
                    if (px * px + pz * pz > t.HalfWidth * t.HalfWidth) continue;
                    // Not where the other way ends too (two ways running on into each other keep their round ends).
                    foreach (var e in new[] { firsts.GetValueOrDefault(other, (double.NaN, double.NaN)), lasts.GetValueOrDefault(other, (double.NaN, double.NaN)) })
                        if (!meeting && !double.IsNaN(e.Item1) && Math.Abs(Mathx.WrapDelta(e.Item1 - x, width)) < 1.5 && Math.Abs(Mathx.WrapDelta(e.Item2 - z, height)) < 1.5) return false;
                    return true;
                }
            }
            return false;
        }
        var flatStart = new HashSet<int>(firsts.Where(f => OnAnother(f.Key, f.Value.X, f.Value.Z)).Select(f => f.Key));
        var flatEnd = new HashSet<int>(lasts.Where(f => OnAnother(f.Key, f.Value.X, f.Value.Z)).Select(f => f.Key));
        foreach (var (pts, t, st, rid) in runs)
        {
            var (start, end, length) = ends[rid];
            if ((flatStart.Contains(rid) || t.FlatStart) && pts[0].S < 0.01f) pts[0] = pts[0] with { Flat = 1f };
            if ((flatEnd.Contains(rid) || t.FlatEnd) && Math.Abs(pts[^1].S - length) < 0.01f) pts[^1] = pts[^1] with { Flat = 1f };
            for (int i = 0; i < pts.Count; i++)
            {
                if (start && pts[i].S < LotReach || end && length - pts[i].S < LotReach) pts[i] = pts[i] with { Lot = 1f };
                // The junction it comes to: the nearer end's, within 80 m.
                float toStart = pts[i].S, toEnd = length - pts[i].S;
                if (t.JoinStart != 0 && toStart < JunctionReach && (t.JoinEnd == 0 || toStart <= toEnd)) pts[i] = pts[i] with { JForm = t.JoinStart, JDist = -toStart };
                else if (t.JoinEnd != 0 && toEnd < JunctionReach) pts[i] = pts[i] with { JForm = t.JoinEnd, JDist = toEnd };
                // Where it runs on into a way of another class, its painted width eases to meet that way's over a taper.
                var (paint, seam) = Seam(t, toStart, toEnd);
                pts[i] = pts[i] with { PaintHw = paint, Seam = seam };
                if (st % Town != Ring)
                    foreach (var (ix, iz, ir) in islands)
                        if (Math.Sqrt(Math.Pow(Mathx.WrapDelta(pts[i].X - ix, width), 2) + Math.Pow(Mathx.WrapDelta(pts[i].Z - iz, height), 2)) < ir) pts[i] = pts[i] with { Core = 1f };
            }
            n += Emit(segs, pts, t, st, rid, width, height);
        }
        // The junctions' rounded corners, each an entry of its own.
        foreach (PlanetSurface.Infra.RoadCorner c in corners ?? Array.Empty<PlanetSurface.Infra.RoadCorner>())
        {
            segs.Add((float)Mathx.WrapX(c.X, width)); segs.Add((float)Mathx.Wrap(c.Z, height)); segs.Add(c.TiltX); segs.Add(c.TiltZ);
            segs.Add(c.Y); segs.Add(c.Y); segs.Add(c.R); segs.Add(c.Cut);
            segs.Add(c.Cut); segs.Add(0f); segs.Add(0f); segs.Add(Corner);
            segs.Add(c.AX); segs.Add(c.AZ); segs.Add(c.BX); segs.Add(c.BZ);
            segs.Add(c.LimA); segs.Add(c.LimB); segs.Add(c.Kerbed ? 1f : 0f); segs.Add(c.Disc ? 1f : 0f);
            for (int k = 5 * 4; k < Stride; k++) segs.Add(0f);
            n++;
        }
        RoadField field = Pack(segs, n, width, height);
        field.Corners = corners ?? Array.Empty<PlanetSurface.Infra.RoadCorner>();
        return field;
    }

    /// <summary>
    /// A road's painted half width at a point, and how near a class-change seam it is (1 at the seam, 0 past its taper):
    /// <see cref="PlanetSurface.Infra.InfraNetwork.SeamWidth"/> from the nearer end that runs on into another class.
    /// </summary>
    public static (float PaintHw, float Seam) Seam(TerrainStamp t, double toStart, double toEnd)
    {
        float hw = t.HalfWidth, seam = 0f;
        bool atStart = t.MeetStartHw > 0f && toStart < t.MeetStartTaper && (t.MeetEndHw <= 0f || toStart <= toEnd);
        bool atEnd = !atStart && t.MeetEndHw > 0f && toEnd < t.MeetEndTaper;
        if (atStart || atEnd)
        {
            double d = atStart ? toStart : toEnd, taper = atStart ? t.MeetStartTaper : t.MeetEndTaper;
            hw = PlanetSurface.Infra.InfraNetwork.SeamWidth(t.HalfWidth, atStart ? t.MeetStartHw : t.MeetEndHw, d, taper);
            double k = Math.Clamp(d / taper, 0, 1);
            seam = (float)(1 - k * k * (3 - 2 * k));
        }
        return (hw, seam);
    }

    /// <summary>How far into a place's made ground a road that leads there runs before it gives way to it, m.</summary>
    public const float MadeOverlap = 2.5f;

    /// <summary>How far from a junction a road carries its form (for its approach paint), m.</summary>
    const float JunctionReach = 80f;

    /// <summary>A town's street: its stamp's name says so (InfraNetwork names them), and its style is raised by <see cref="Town"/>.</summary>
    public const int Town = 8;

    static int StyleOf(string name, float halfWidth)
    {
        if (name.EndsWith(" street")) return StyleOf(name[..^7], halfWidth) + Town;
        if (name.StartsWith("Highway")) return Highway;
        if (name.StartsWith("Rail")) return Rail;
        if (name.StartsWith("Track")) return Track;
        if (name.StartsWith("Graded")) return Graded;
        if (name.StartsWith("Ring")) return Ring;
        if (name.StartsWith("Road")) return halfWidth >= 3.5f ? Street : Track;
        return Drive;
    }

    /// <summary>
    /// A road point and how the places' pads lie over it: at its middle (markings stop there, and on a
    /// footprint the road itself), and anywhere across its deck. A pad rules the ground it covers, so where
    /// a yard's apron reaches over a road's edge the ground there is the yard's level, not the deck's: the
    /// shader allows for that, or the road's edge showed notches the shape of the yard's cells.
    /// </summary>
    static Point Covered(PlanetTerrainSource source, double x, double z, float y, float s, float nx, float nz, float halfWidth, double width, double height)
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
        // A road that leads to a place runs on into its made ground: it stops only once it is well inside the yard,
        // apron or car park laid at its end (which takes over there), never short of it on the pad's bare margin.
        // A place with no made ground near its way in still stops the road at its footprint.
        if (core > 0f && source.Grounds is { Count: > 0 } grounds)
        {
            float inside = float.MaxValue;
            bool near = false;
            foreach (GroundPatch g in grounds)
            {
                if (g.Cover == MadeCover.Lawn) continue;
                float d = g.Distance(cx, cz, width, height);
                if (d < 40f) near = true;
                inside = MathF.Min(inside, d);
            }
            if (near && inside > -MadeOverlap) core = 0f;
        }
        float wide = pad;
        for (int k = -2; k <= 2; k++)
        {
            if (k == 0) continue;
            float o = halfWidth * k * 0.5f + 0.75f * Math.Sign(k);
            double qx = Mathx.WrapX(x + nx * o, width), qz = Mathx.Wrap(z + nz * o, height);
            foreach (int i in source.StampsAt(qx, qz))
                if (source.Stamps[i].Shape == StampShape.Pad) wide = MathF.Max(wide, source.Stamps[i].Weight(qx, qz));
        }
        return new Point(x, z, y, s, pad, core, wide);
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
            segs.Add(t.HalfWidth); segs.Add(t.Wear); segs.Add((float)t.Surface); segs.Add(style + 16f * id);
            segs.Add(a.Pad); segs.Add(b.Pad); segs.Add(a.Core); segs.Add(b.Core);
            segs.Add(a.Wide); segs.Add(b.Wide); segs.Add(a.Lot); segs.Add(b.Lot);
            segs.Add(a.JForm); segs.Add(b.JForm); segs.Add(a.JDist); segs.Add(b.JDist);
            segs.Add(t.Median); segs.Add((a.PaintHw + b.PaintHw) * 0.5f); segs.Add(a.Seam); segs.Add(b.Seam);
            segs.Add(k == 0 ? a.Flat : 0f); segs.Add(k + 2 == keep.Count ? b.Flat : 0f);
            for (int k2 = 7 * 4 + 2; k2 < Stride; k2++) segs.Add(0f);
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
            err = MathF.Max(err, MathF.Max(MathF.Abs(r.Wide - (p.Wide + (q.Wide - p.Wide) * u)), MathF.Abs(r.Lot - (p.Lot + (q.Lot - p.Lot) * u))) / 0.1f);
            if (r.JForm != p.JForm || r.JForm != q.JForm) err = MathF.Max(err, 100f);
            err = MathF.Max(err, MathF.Abs(r.JDist - (p.JDist + (q.JDist - p.JDist) * u)) / 0.2f);
            err = MathF.Max(err, MathF.Max(MathF.Abs(r.PaintHw - (p.PaintHw + (q.PaintHw - p.PaintHw) * u)) / 0.05f, MathF.Abs(r.Seam - (p.Seam + (q.Seam - p.Seam) * u)) / 0.1f));
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
            int o = s * Stride;
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
        var data = new float[rows * SegmentsPerRow * Stride];
        segs.CopyTo(data);
        return new RoadField
        {
            _segs = data, _cells = cells, _index = index, _width = width, _height = height,
            Segments = Texture(data, SegmentsPerRow * Texels, rows, Image.Format.Rgbaf, 16),
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
            int o = (int)_index[first + k] * Stride;
            if (Math.Abs(_segs[o + 11] - Corner) < 0.5f) continue;
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

    /// <summary>
    /// The deck's edge as the terrain shader draws it where paved roads meet (tests): the nearest road's edge, or a
    /// junction's rounded corner where that comes nearer (negative on the deck, positive past the kerb).
    /// </summary>
    public float DeckEdgeAt(double x, double z, float y, float tolerance)
    {
        float edge = float.PositiveInfinity;
        if (SegmentCount == 0) return edge;
        double cx = Mathx.WrapX(x, _width), cz = Mathx.Wrap(z, _height);
        int cellX = Math.Min((int)Grid.Y - 1, (int)(cx / Cell)), cellZ = Math.Min((int)Grid.Z - 1, (int)(cz / Cell));
        int cell = cellZ * (int)Grid.Y + cellX, first = (int)_cells[cell * 2], count = Math.Min((int)_cells[cell * 2 + 1], 64);
        // The roads: the nearest's edge (and a street's turning bulb round its end, as the shader swells it).
        for (int k = 0; k < count; k++)
        {
            int o = (int)_index[first + k] * Stride;
            if (Math.Abs(_segs[o + 11] - Corner) < 0.5f) continue;
            float rx = (float)Mathx.WrapDelta(cx - _segs[o], _width), rz = (float)Mathx.WrapDelta(cz - _segs[o + 1], _height);
            float ex = _segs[o + 2], ez = _segs[o + 3];
            float u = Math.Clamp((rx * ex + rz * ez) / MathF.Max(ex * ex + ez * ez, 1e-6f), 0f, 1f);
            float d = MathF.Sqrt((rx - ex * u) * (rx - ex * u) + (rz - ez * u) * (rz - ez * u));
            float hw = _segs[o + 25] > 0.1f ? _segs[o + 25] : _segs[o + 8];
            float e = d - hw;
            float ur = (rx * ex + rz * ez) / MathF.Max(ex * ex + ez * ez, 1e-6f), len = MathF.Sqrt(ex * ex + ez * ez);
            if (ur < 0f && _segs[o + 28] > 0.5f || ur > 1f && _segs[o + 29] > 0.5f)
                e = MathF.Max(MathF.Abs(rx * ez - rz * ex) / MathF.Max(len, 1e-4f) - hw, MathF.Max(-ur, ur - 1f) * len);
            if (e > Reach || MathF.Abs(y - (_segs[o + 4] + (_segs[o + 5] - _segs[o + 4]) * u)) > tolerance + MathF.Max(e, 0f) * 1.2f) continue;
            edge = MathF.Min(edge, e);
        }
        for (int k = 0; k < count; k++)
        {
            int o = (int)_index[first + k] * Stride;
            if (Math.Abs(_segs[o + 11] - Corner) > 0.5f) continue;
            var c = new PlanetSurface.Infra.RoadCorner
            {
                X = _segs[o], Z = _segs[o + 1], R = _segs[o + 6], Cut = _segs[o + 7], AX = _segs[o + 12], AZ = _segs[o + 13], BX = _segs[o + 14], BZ = _segs[o + 15],
                LimA = _segs[o + 16], LimB = _segs[o + 17], Disc = _segs[o + 19] > 0.5f, Y = _segs[o + 4], TiltX = _segs[o + 2], TiltZ = _segs[o + 3],
            };
            float f = c.Edge(cx, cz, _width, _height);
            if (float.IsInfinity(f) || MathF.Abs(y - c.DeckAt(cx, cz, _width, _height)) > tolerance + MathF.Max(f, 0f) * 1.2f) continue;
            edge = float.IsInfinity(edge) ? f : CornerJoin(edge, f);
        }
        return edge;
    }

    /// <summary>
    /// A corner's kerb joined to the ways' own (the shader's): their least, eased over the last few centimetres where they meet, so
    /// the few centimetres a corner's fit to a curved centre line leaves at its tangent points never show as a jog in the kerb.
    /// </summary>
    public static float CornerJoin(float road, float corner)
    {
        const float K = 0.4f;
        float h = MathF.Max(K - MathF.Abs(road - corner), 0f) / K;
        return MathF.Min(road, corner) - h * h * K * 0.25f;
    }

    static ImageTexture Texture(float[] values, int w, int h, Image.Format format, int bytesPerTexel)
    {
        var bytes = new byte[w * h * bytesPerTexel];
        Buffer.BlockCopy(values, 0, bytes, 0, bytes.Length);
        return ImageTexture.CreateFromImage(Image.CreateFromData(w, h, false, format, bytes));
    }
}
