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
        bool coastal = LandformInfo.IsCoastal(at);
        double bestScore = double.MinValue, bx = 0, bz = 0;
        float bestHeading = 0f;
        for (double z = 100; z < map.HeightMeters; z += 200)
        for (double x = 100; x < map.WidthMeters; x += 200)
        {
            if (map.LandformsAt(x, z).Weight(at) < 0.92f) continue;
            if (!coastal && map.Sample(map.Elevation, x, z) < 3f) continue;
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
                    while (r < 6000 && map.LandformsAt(x + dx * r, z + dz * r).Weight(at) >= 0.7f) r += 100;
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
