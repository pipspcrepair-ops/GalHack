using System;
using Planets.Gen;

namespace Planets;

/// <summary>
/// Camera spots for landform screenshots: somewhere deep inside a landform (it holds all round,
/// as far out as possible), and a heading across it or towards another landform.
/// </summary>
public static class Spots
{
    /// <param name="face">Look towards the nearest country of this landform (if given).</param>
    /// <param name="from">Stand about this far from it, metres (if given).</param>
    public static (double x, double z, float heading) Find(WorldMap map, Landform at, Landform? face, float from)
    {
        // Ringlands are seen from the air, from just south of the tallest ring on dry land, looking north across it.
        if (at == Landform.Ringlands)
        {
            double ringX = 0, ringZ = 0;
            float tallest = -1f;
            foreach (var (x, y, height) in map.Model.RingStructures())
                if (height > tallest && map.Sample(map.Elevation, x, y) > 5f && map.LandformsAt(x, y).Weight(at) > 0.8f)
                    (ringX, ringZ, tallest) = (x, y, height);
            if (tallest > 0f) return (ringX, Mathx.Wrap(ringZ + 620, map.HeightMeters), 0f);
        }
        bool coastal = LandformInfo.IsCoastal(at);
        double bestScore = double.MinValue, bx = 0, bz = 0;
        float bestHeading = 0f;
        for (double z = 100; z < map.HeightMeters; z += 200)
        for (double x = 100; x < map.WidthMeters; x += 200)
        {
            if (map.LandformsAt(x, z).Weight(at) < 0.92f) continue;
            if (!coastal && map.Sample(map.Elevation, x, z) < 3f) continue;
            // Fjords are seen from the water in a trough, looking up it.
            if (at == Landform.Fjords && map.Sample(map.Elevation, x, z) > -2f) continue;
            // How far it holds: the first ring (of 8 directions) where it falls below 0.8.
            double depth = 0;
            for (double r = 250; r <= 2000; r += 250)
            {
                bool all = true;
                for (int d = 0; d < 8 && all; d++)
                    all = map.LandformsAt(x + Math.Cos(d * Math.PI / 4) * r, z + Math.Sin(d * Math.PI / 4) * r).Weight(at) >= 0.8f;
                if (!all) break;
                depth = r;
            }
            double score = depth;
            float heading;
            if (face is Landform f)
            {
                // The nearest country of the other landform, along 24 headings.
                double nearest = double.MaxValue;
                heading = 0f;
                for (int d = 0; d < 24; d++)
                {
                    double a = d * Math.PI * 2 / 24, dx = Math.Sin(a), dz = -Math.Cos(a);
                    for (double r = 100; r <= 6000; r += 100)
                    {
                        if (map.LandformsAt(x + dx * r, z + dz * r).Weight(f) < 0.8f) continue;
                        if (r < nearest) { nearest = r; heading = (float)a; }
                        break;
                    }
                }
                if (nearest == double.MaxValue) continue;
                score = from > 0 ? -Math.Abs(nearest - from) + depth * 0.2 : depth - nearest * 0.3;
            }
            else
            {
                // Look the way it goes on furthest.
                heading = 0f;
                double longest = -1;
                for (int d = 0; d < 16; d++)
                {
                    double a = d * Math.PI * 2 / 16, dx = Math.Sin(a), dz = -Math.Cos(a), r = 100;
                    while (r < 6000 && map.LandformsAt(x + dx * r, z + dz * r).Weight(at) >= 0.7f
                        && (at != Landform.Fjords || map.Sample(map.Elevation, x + dx * r, z + dz * r) < 0f)) r += 50;
                    if (r > longest) { longest = r; heading = (float)a; }
                }
                score += longest * 0.3;
            }
            if (score > bestScore)
            {
                bestScore = score;
                bx = x;
                bz = z;
                bestHeading = heading;
            }
        }
        return (bx, bz, bestHeading);
    }
}
