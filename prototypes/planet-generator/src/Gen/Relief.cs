using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Planets.Gen;

/// <summary>
/// How dramatic a planet's land is, in numbers a player would recognise:
/// - <b>Range</b>: highest minus lowest ground (sea counts as its surface) inside 2 x 2 km windows
///   centred on dry land. Mountain ranges push it into the hundreds of metres.
/// - <b>Slopes</b>: the share of dry ground steeper than 30 and 45 degrees across 8 m: the scale of a
///   cliff face or a hillside, not of a bump underfoot. 45 degrees is roughly where a player stops
///   climbing and starts using the jetpack. The same shares across 2 m are kept for reference; they
///   also count small bumps, so they run higher.
/// - <b>Incision</b>: how deep the ground at a point sits below walls on both sides within 150 m,
///   taking the best of eight directions. Valley floors, gorges and canyons score high; a plain or
///   a ridge scores near zero. The 95th percentile is the "canyon depth".
/// Pure C#, deterministic from the seed salt, and independent of who supplies the heights, so the
/// planet generator and the streamed surface measure the same way.
/// </summary>
public static class Relief
{
    public readonly record struct Stats(
        float RangeMedian, float RangeMax,
        float Slope30, float Slope45,
        float Incision95, float IncisionMax,
        float Fine30 = 0, float Fine45 = 0)
    {
        public override string ToString() =>
            $"range in 2 km {RangeMedian:F0} m median / {RangeMax:F0} m max; slopes over 8 m >30° {Slope30:P1}, >45° {Slope45:P1} " +
            $"(over 2 m {Fine30:P0}, {Fine45:P0}); canyon depth {Incision95:F0} m (95th pct) / {IncisionMax:F0} m max";
    }

    const double Window = 2000, HalfWidth = 150;

    /// <summary>
    /// Measures relief on dry land.
    /// <paramref name="height"/> is (x, y, spacing) to metres above sea level.
    /// </summary>
    public static Stats Measure(WorldMap map, Func<double, double, double, float> height, ulong salt,
        int windows = 8, int slopePoints = 3000, int incisionPoints = 700)
    {
        var land = LandPoints(map, salt, Math.Max(windows, Math.Max(slopePoints, incisionPoints)));
        if (land.Count == 0) return default;

        // Range: a 16 m grid over each window, the sea counted at its surface.
        var ranges = new float[Math.Min(windows, land.Count)];
        Parallel.For(0, ranges.Length, w =>
        {
            var (cx, cy) = land[w * land.Count / ranges.Length];
            float lo = float.MaxValue, hi = float.MinValue;
            for (double dy = -Window / 2; dy <= Window / 2; dy += 16)
            for (double dx = -Window / 2; dx <= Window / 2; dx += 16)
            {
                float h = Math.Max(0f, height(cx + dx, cy + dy, 16));
                lo = Math.Min(lo, h);
                hi = Math.Max(hi, h);
            }
            ranges[w] = hi > lo ? hi - lo : 0f;
        });
        Array.Sort(ranges);

        // Slopes across 8 m (landforms) and 2 m (underfoot), on dry ground.
        int n = Math.Min(slopePoints, land.Count);
        int over30 = 0, over45 = 0, fine30 = 0, fine45 = 0, counted = 0;
        object gate = new();
        Parallel.For(0, n, () => new int[5], (k, _, acc) =>
        {
            var (x, y) = land[k];
            float h = height(x, y, 1);
            if (h < 1f) return acc;
            float wide = Slope(height, x, y, 4), fine = Slope(height, x, y, 1);
            if (wide > 0.57735f) acc[0]++;
            if (wide > 1f) acc[1]++;
            if (fine > 0.57735f) acc[2]++;
            if (fine > 1f) acc[3]++;
            acc[4]++;
            return acc;
        }, acc => { lock (gate) { over30 += acc[0]; over45 += acc[1]; fine30 += acc[2]; fine45 += acc[3]; counted += acc[4]; } });

        // Incision: walls on both sides within 150 m, best of four direction pairs.
        int m = Math.Min(incisionPoints, land.Count);
        var depth = new float[m];
        Parallel.For(0, m, k =>
        {
            var (x, y) = land[(k * 7919) % land.Count];
            float h = Math.Max(0f, height(x, y, 4));
            float best = 0f;
            for (int d = 0; d < 4; d++)
            {
                double a = d * Math.PI / 4;
                double ux = Math.Cos(a), uy = Math.Sin(a);
                float left = float.MinValue, right = float.MinValue;
                for (double r = 8; r <= HalfWidth; r += 8)
                {
                    left = Math.Max(left, height(x + ux * r, y + uy * r, 4));
                    right = Math.Max(right, height(x - ux * r, y - uy * r, 4));
                }
                best = Math.Max(best, Math.Min(left, right) - h);
            }
            depth[k] = best;
        });
        Array.Sort(depth);

        float Share(int c) => counted > 0 ? c / (float)counted : 0f;
        return new Stats(
            ranges[ranges.Length / 2], ranges[^1], Share(over30), Share(over45),
            depth[(int)(depth.Length * 0.95f)], depth[^1], Share(fine30), Share(fine45));
    }

    /// <summary>Rise over run between points <paramref name="half"/> metres either side, along x and y.</summary>
    static float Slope(Func<double, double, double, float> height, double x, double y, double half)
    {
        double spacing = Math.Min(half, 2);
        float gx = (height(x + half, y, spacing) - height(x - half, y, spacing)) / (float)(2 * half);
        float gy = (height(x, y + half, spacing) - height(x, y - half, spacing)) / (float)(2 * half);
        return MathF.Sqrt(gx * gx + gy * gy);
    }

    /// <summary>Seeded dry-land points anywhere on the planet, in a fixed order.</summary>
    static List<(double x, double y)> LandPoints(WorldMap map, ulong salt, int count)
    {
        var rng = new Rng(Hash.Of(map.Spec.Seed, 0x52454C, (long)salt));
        var list = new List<(double, double)>(count);
        for (int tries = 0; list.Count < count && tries < count * 40; tries++)
        {
            double x = rng.NextFloat() * map.WidthMeters, y = rng.NextFloat() * map.HeightMeters;
            int cell = map.CellAt(x, y);
            if (map.Has(cell, CellFlags.Water)) continue;
            if (map.Elevation[cell] < 2f) continue;
            list.Add((x, y));
        }
        return list;
    }
}
