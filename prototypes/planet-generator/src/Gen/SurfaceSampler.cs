using System;
using System.Collections.Generic;

namespace Planets.Gen;

/// <summary>
/// One fine terrain sample: ground height and, if a river runs here, its surface. Bank is the
/// height the ground must keep here for the river beside it to stay in its bed, stamps or no stamps.
/// </summary>
public readonly record struct GroundSample(float Height, float RiverLevel, bool InRiver, float Bank = float.NegativeInfinity);

/// <summary>
/// Fine-detail terrain for the area around the player, sampled on demand at 1-2 m. It evaluates
/// the same TerrainModel as the world map at a finer spacing (so the extra octaves appear), then
/// cuts river channels along the map's smoothed river polylines. Thread-safe: chunk builders
/// call it from worker threads.
/// </summary>
public sealed class SurfaceSampler
{
    public readonly WorldMap Map;
    readonly TerrainModel _model;
    readonly double _width;

    // River segments bucketed on a grid so each sample only checks the few nearby.
    const double Bucket = 128;      // bucket size; adjusted to divide the map exactly both ways
    const float Reach = 70f;  // metres beyond a channel edge that a river still shapes the land
    const float BankRise = 0.3f, BankFlat = 3f, BankFall = 0.25f;   // a bank's lip over the water, how far it runs flat, and its fall beyond (1 in 4)

    /// <summary>
    /// A river's water stands this far below its level, on the channel's sloping side under the bank's lip.
    /// The lowest a river's level goes, so where it reaches the sea its water is at the sea's level.
    /// </summary>
    public const float RiverDrop = 0.15f;

    /// <summary>How far past its channel's edge a river at this level holds the ground up to sea level (its levee).</summary>
    public static float BankReach(float level) => BankFlat + Math.Max(0f, level + BankRise) / BankFall;
    readonly int _bx, _by;
    readonly double _bucketW, _bucketH, _height;
    readonly List<int>[] _buckets;
    readonly List<(float ax, float ay, float bx, float by, float la, float lb, float wa, float wb)> _segments = new();

    public SurfaceSampler(WorldMap map)
    {
        Map = map;
        _model = map.Model;
        _width = map.WidthMeters;
        _height = map.HeightMeters;
        _bx = Math.Max(1, (int)Math.Round(_width / Bucket));
        _bucketW = _width / _bx;
        _by = Math.Max(1, (int)Math.Round(_height / Bucket));
        _bucketH = _height / _by;
        _buckets = new List<int>[_bx * _by];
        float[][] levels = RiverFit.Levels(map, _model);
        for (int n = 0; n < map.Rivers.Count; n++)
        {
            River r = map.Rivers[n];
            for (int k = 0; k + 1 < r.Count; k++)
                AddSegment(r.X[k], r.Y[k], r.X[k + 1], r.Y[k + 1], levels[n][k], levels[n][k + 1], r.Width[k], r.Width[k + 1]);
        }
    }

    public double Width => _width;
    public double Height => Map.HeightMeters;

    /// <summary>Ground and river at a point in metres. x and y may be outside the map: both wrap.</summary>
    public GroundSample Sample(double x, double y, double spacing)
    {
        x = Mathx.WrapX(x, _width);
        y = Mathx.Wrap(y, _height);
        float e = _model.Elevation(x, y, spacing, out float meander);
        _model.Meander(x, y, meander, out double mx, out double my);
        if (!NearestRiver(Mathx.WrapX(x + mx, _width), Mathx.Wrap(y + my, _height), out float d, out float level, out float width, out float hold))
            return new GroundSample(e, 0f, false);

        // A rounded channel below the river surface, then banks that rise away from it.
        float half = width * 0.5f;
        float depth = 0.8f + width * 0.09f;
        float target;
        if (d < half) target = level - depth * (1f - d * d / (half * half));
        else
        {
            // Where a river cuts through high ground the banks become gorge walls; a jagged
            // term keeps them from running as straight and even as the river's polyline.
            float out_ = d - half;
            out_ = Math.Max(0f, out_ + _model.Jag(x, y, spacing) * Math.Min(out_, 24f) * 0.7f);
            // Where rivers meander (plains, wetlands, deltas) their banks are low and gentle;
            // where they cut through high ground the banks become gorge walls.
            target = level + 0.35f + (0.18f * out_ + 0.03f * out_ * out_) / Math.Clamp(meander, 1f, 2.6f);
        }
        float keep = Mathx.Smooth(half + Reach, half + Reach * 0.75f, d);
        float h = Mathx.Lerp(e, Math.Min(e, target), keep);
        // Water stays in its bed: past the channel the ground never stands lower than the water beside
        // it (any river's: where two meet at different levels, the higher's), a low levee where the land
        // dips, running down to meet the land at a walkable slope. Not out in the sea (land under its
        // level where the sea is drawn): a river that comes near it is at its level by then (RiverFit),
        // and a levee there stood in the sea as a ring round the mouth.
        float bank = float.NegativeInfinity;
        if (d >= half)
        {
            float sea = Mathx.Smooth(WorldMap.SeaFrom, WorldMap.SeaFrom + 0.5f, Map.Sample(Map.SeaDepth, x, y)) * Mathx.Smooth(0.5f, 0f, e);
            bank = hold - sea * Reach;
            h = Math.Max(h, bank);
        }
        return new GroundSample(h, level, d < half + 1.5f, bank);
    }

    public float HeightAt(double x, double y, double spacing = 2) => Sample(x, y, spacing).Height;

    /// <summary>The landforms at a point (both axes wrap), strongest first: cheap, and the same on every machine.</summary>
    public LandformBlend LandformsAt(double x, double y) => Map.LandformsAt(x, y);

    /// <param name="hold">the highest any river's levee stands over the point, of those it lies outside the channel of</param>
    bool NearestRiver(double x, double y, out float distance, out float level, out float width, out float hold)
    {
        distance = float.MaxValue;
        level = width = 0f;
        hold = float.NegativeInfinity;
        int bi = Math.Clamp((int)(x / _bucketW), 0, _bx - 1), bj = Math.Clamp((int)(y / _bucketH), 0, _by - 1);
        List<int> list = _buckets[bj * _bx + bi];
        if (list == null) return false;

        float best = float.MaxValue;
        foreach (int s in list)
        {
            var g = _segments[s];
            // Bring the query point next to the segment, whichever side of either seam it lies on.
            float px = (float)(g.ax + Mathx.WrapDelta(x - g.ax, _width)), py = (float)(g.ay + Mathx.WrapDelta(y - g.ay, _height));
            float vx = g.bx - g.ax, vy = g.by - g.ay;
            float len2 = vx * vx + vy * vy;
            float t = len2 > 0f ? Math.Clamp(((px - g.ax) * vx + (py - g.ay) * vy) / len2, 0f, 1f) : 0f;
            float dx = px - (g.ax + vx * t), dy = py - (g.ay + vy * t);
            float w = Mathx.Lerp(g.wa, g.wb, t);
            // Rank by distance to the channel edge so a big river wins over a nearby trickle.
            float d = MathF.Sqrt(dx * dx + dy * dy);
            float edge = d - w * 0.5f;
            float at = Mathx.Lerp(g.la, g.lb, t);
            if (edge >= 0f) hold = Math.Max(hold, at + BankRise - Math.Max(0f, edge - BankFlat) * BankFall);
            if (edge < best)
            {
                best = edge;
                distance = d;
                width = w;
                level = at;
            }
        }
        return best < Reach;
    }

    void AddSegment(float ax, float ay, float bx, float by, float la, float lb, float wa, float wb)
    {
        int id = _segments.Count;
        _segments.Add((ax, ay, bx, by, la, lb, wa, wb));
        float pad = Reach + Math.Max(wa, wb);
        int i0 = (int)Math.Floor((Math.Min(ax, bx) - pad) / _bucketW), i1 = (int)Math.Floor((Math.Max(ax, bx) + pad) / _bucketW);
        int j0 = (int)Math.Floor((Math.Min(ay, by) - pad) / _bucketH), j1 = (int)Math.Floor((Math.Max(ay, by) + pad) / _bucketH);
        for (int j = j0; j <= j1; j++)
        for (int i = i0; i <= i1; i++)
        {
            int wi = (i % _bx + _bx) % _bx, wj = (j % _by + _by) % _by;
            ref List<int> list = ref _buckets[wj * _bx + wi];
            list ??= new List<int>();
            if (list.Count == 0 || list[^1] != id) list.Add(id);
        }
    }
}
