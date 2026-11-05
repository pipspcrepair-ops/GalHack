using System;
using System.Collections.Generic;

namespace Planets.Gen;

/// <summary>One fine terrain sample: ground height and, if a river runs here, its surface.</summary>
public readonly record struct GroundSample(float Height, float RiverLevel, bool InRiver);

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
        foreach (River r in map.Rivers)
            for (int k = 0; k + 1 < r.Count; k++)
                AddSegment(r.X[k], r.Y[k], r.X[k + 1], r.Y[k + 1], r.Level[k], r.Level[k + 1], r.Width[k], r.Width[k + 1]);
    }

    public double Width => _width;
    public double Height => Map.HeightMeters;

    /// <summary>Ground and river at a point in metres. x and y may be outside the map: both wrap.</summary>
    public GroundSample Sample(double x, double y, double spacing)
    {
        x = Mathx.WrapX(x, _width);
        y = Mathx.Wrap(y, _height);
        float e = _model.Elevation(x, y, spacing);
        _model.Meander(x, y, out double mx, out double my);
        if (!NearestRiver(Mathx.WrapX(x + mx, _width), Mathx.Wrap(y + my, _height), out float d, out float level, out float width))
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
            target = level + 0.35f + 0.18f * out_ + 0.03f * out_ * out_;
        }
        float keep = Mathx.Smooth(half + Reach, half + Reach * 0.75f, d);
        float h = Mathx.Lerp(e, Math.Min(e, target), keep);
        return new GroundSample(h, level, d < half + 1.5f);
    }

    public float HeightAt(double x, double y, double spacing = 2) => Sample(x, y, spacing).Height;

    bool NearestRiver(double x, double y, out float distance, out float level, out float width)
    {
        distance = float.MaxValue;
        level = width = 0f;
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
            if (edge < best)
            {
                best = edge;
                distance = d;
                width = w;
                level = Mathx.Lerp(g.la, g.lb, t);
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
