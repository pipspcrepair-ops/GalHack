using System;
using System.Threading.Tasks;

namespace Planets.Gen;

/// <summary>
/// Temperature from latitude and altitude; humidity from distance to water. Both get a little
/// weather noise so biome borders wander instead of following contour lines and latitude bands.
///
/// The equator is the middle row and both map edges are the pole: the map wraps north-south, so
/// the polar band runs continuously across the seam and the cold never stops at a wall.
/// </summary>
static class Climate
{
    const float LapseRate = 0.5f;            // temperature lost per km of altitude
    const float MoistureReach = 700f;        // metres over which water keeps the air damp

    /// <param name="dry">Per cell, how much drier (+) or wetter (-) than usual: dry belts, rain shadows and the landform's own air.</param>
    public static void Run(WorldMap map, float[] dry = null)
    {
        int w = map.Width, h = map.Height;
        float[] distance = DistanceToWater(map);
        ArchetypeDef def = map.Def;

        Parallel.For(0, h, j =>
        {
            double y = map.CellY(j);
            float lat = (float)(y / map.HeightMeters) * 2f - 1f;
            for (int i = 0; i < w; i++)
            {
                int k = j * w + i;
                float e = map.Elevation[k];
                float east = map.Elevation[map.Index(i + 1, j)], west = map.Elevation[map.Index(i - 1, j)];
                float south = map.Elevation[map.Index(i, j + 1)];
                float north = map.Elevation[map.Index(i, j - 1)];
                float gx = (east - west) / (2f * map.CellSize), gy = (south - north) / (2f * map.CellSize);
                map.Slope[k] = MathF.Sqrt(gx * gx + gy * gy);

                map.Model.ClimateNoise(map.CellX(i), y, out float tn, out float hn);
                map.Temperature[k] = Math.Clamp(
                    def.BaseTemperature - def.LatitudeRange * lat * lat - Math.Max(e, 0f) * 0.001f * LapseRate + tn * 0.08f,
                    0f, 1f);
                float damp = MathF.Exp(-distance[k] / MoistureReach);
                float shift = dry != null ? dry[k] * 0.45f : 0f;
                map.Humidity[k] = Math.Clamp(def.BaseHumidity * (0.75f + 0.5f * hn) + 0.45f * damp - shift, 0f, 1f);
            }
        });
    }

    /// <summary>
    /// Approximate Euclidean distance (metres) from every cell to the nearest sea or river cell:
    /// a chamfer transform, run three times so distances carry across both seams.
    /// </summary>
    static float[] DistanceToWater(WorldMap map)
    {
        int w = map.Width, h = map.Height;
        var d = new float[map.Count];
        for (int k = 0; k < map.Count; k++)
            d[k] = map.Has(k, CellFlags.Water) || map.Has(k, CellFlags.River) ? 0f : 1e9f;

        const float a = 1f, b = 1.4142f;
        for (int round = 0; round < 3; round++)
        {
            for (int j = 0; j < h; j++)
            for (int i = 0; i < w; i++)
            {
                int k = j * w + i;
                float v = Math.Min(d[k], d[map.Index(i - 1, j)] + a);
                v = Math.Min(v, d[map.Index(i, j - 1)] + a);
                v = Math.Min(v, d[map.Index(i - 1, j - 1)] + b);
                v = Math.Min(v, d[map.Index(i + 1, j - 1)] + b);
                d[k] = v;
            }
            for (int j = h - 1; j >= 0; j--)
            for (int i = w - 1; i >= 0; i--)
            {
                int k = j * w + i;
                float v = Math.Min(d[k], d[map.Index(i + 1, j)] + a);
                v = Math.Min(v, d[map.Index(i, j + 1)] + a);
                v = Math.Min(v, d[map.Index(i + 1, j + 1)] + b);
                v = Math.Min(v, d[map.Index(i - 1, j + 1)] + b);
                d[k] = v;
            }
        }
        for (int k = 0; k < map.Count; k++) d[k] *= map.CellSize;
        return d;
    }
}
