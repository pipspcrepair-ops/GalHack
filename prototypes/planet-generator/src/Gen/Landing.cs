using System;

namespace Planets.Gen;

/// <summary>A landing site: a point in metres and the heading (radians, 0 = north) that faces the water.</summary>
public readonly record struct LandingSite(double X, double Y, float Heading);

public static class Landing
{
    /// <summary>
    /// A gentle riverside or coastal spot, preferring rivers and the one nearest the given point
    /// (the part of the globe facing the camera), away from the polar walls.
    /// </summary>
    public static LandingSite Find(WorldMap map, double nearX, double nearY)
    {
        int best = -1;
        double bestScore = double.MaxValue;
        for (int k = 0; k < map.Count; k++)
        {
            bool river = map.Has(k, CellFlags.Riparian), coast = map.Has(k, CellFlags.Coast);
            if (!river && !coast) continue;
            float e = map.Elevation[k];
            if (e < 1.5f || e > 150f || map.Slope[k] > 0.18f) continue;
            int i = k % map.Width, j = k / map.Width;
            double y = map.CellY(j);
            if (y < map.HeightMeters * 0.2 || y > map.HeightMeters * 0.8) continue;
            double dx = Mathx.WrapDelta(map.CellX(i) - nearX, map.WidthMeters), dy = y - nearY;
            double score = (dx * dx + dy * dy) * (river ? 0.5 : 1.0);
            if (score < bestScore)
            {
                bestScore = score;
                best = k;
            }
        }
        if (best < 0) return new LandingSite(nearX, map.HeightMeters / 2, 0f);

        // Face the nearest water so the first view has the river or the sea in it.
        int bi = best % map.Width, bj = best / map.Width;
        double fx = 0, fy = 0;
        for (int dj = -4; dj <= 4; dj++)
        for (int di = -4; di <= 4; di++)
        {
            int nj = bj + dj;
            if (nj < 0 || nj >= map.Height || (di == 0 && dj == 0)) continue;
            int nb = map.Index(bi + di, nj);
            if (!map.Has(nb, CellFlags.Water) && !map.Has(nb, CellFlags.River)) continue;
            double w = 1.0 / (di * di + dj * dj);
            fx += di * w;
            fy += dj * w;
        }
        float heading = (float)Math.Atan2(fx, -fy);
        return new LandingSite(map.CellX(bi), map.CellY(bj), heading);
    }
}
