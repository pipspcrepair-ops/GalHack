using System;
using System.Threading.Tasks;

namespace Planets.Gen;

/// <summary>
/// Temperature from latitude and altitude; humidity from distance to water. Both get a little
/// weather noise so biome borders wander instead of following contour lines and latitude bands.
/// </summary>
static class Climate
{
    const float LapseRate = 0.5f;            // temperature lost per km of altitude
    const float MoistureReach = 700f;        // metres over which water keeps the air damp

    public static void Run(WorldMap map)
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
                float south = map.Elevation[map.Index(i, Math.Min(j + 1, h - 1))];
                float north = map.Elevation[map.Index(i, Math.Max(j - 1, 0))];
                float gx = (east - west) / (2f * map.CellSize), gy = (south - north) / (2f * map.CellSize);
                map.Slope[k] = MathF.Sqrt(gx * gx + gy * gy);

                map.Model.ClimateNoise(map.CellX(i), y, out float tn, out float hn);
                map.Temperature[k] = Math.Clamp(
                    def.BaseTemperature - def.LatitudeRange * lat * lat - Math.Max(e, 0f) * 0.001f * LapseRate + tn * 0.08f,
                    0f, 1f);
                float damp = MathF.Exp(-distance[k] / MoistureReach);
                map.Humidity[k] = Math.Clamp(def.BaseHumidity * (0.75f + 0.5f * hn) + 0.45f * damp, 0f, 1f);
            }
        });
    }

    /// <summary>
    /// Approximate Euclidean distance (metres) from every cell to the nearest sea or river cell:
    /// a 3-4 chamfer transform, run twice so distances carry across the east-west seam.
    /// </summary>
    static float[] DistanceToWater(WorldMap map)
    {
        int w = map.Width, h = map.Height;
        var d = new float[map.Count];
        for (int k = 0; k < map.Count; k++)
            d[k] = map.Has(k, CellFlags.Water) || map.Has(k, CellFlags.River) ? 0f : 1e9f;

        const float a = 1f, b = 1.4142f;
        for (int round = 0; round < 2; round++)
        {
            for (int j = 0; j < h; j++)
            for (int i = 0; i < w; i++)
            {
                int k = j * w + i;
                float v = Math.Min(d[k], d[map.Index(i - 1, j)] + a);
                if (j > 0)
                {
                    v = Math.Min(v, d[map.Index(i, j - 1)] + a);
                    v = Math.Min(v, d[map.Index(i - 1, j - 1)] + b);
                    v = Math.Min(v, d[map.Index(i + 1, j - 1)] + b);
                }
                d[k] = v;
            }
            for (int j = h - 1; j >= 0; j--)
            for (int i = w - 1; i >= 0; i--)
            {
                int k = j * w + i;
                float v = Math.Min(d[k], d[map.Index(i + 1, j)] + a);
                if (j < h - 1)
                {
                    v = Math.Min(v, d[map.Index(i, j + 1)] + a);
                    v = Math.Min(v, d[map.Index(i + 1, j + 1)] + b);
                    v = Math.Min(v, d[map.Index(i - 1, j + 1)] + b);
                }
                d[k] = v;
            }
        }
        for (int k = 0; k < map.Count; k++) d[k] *= map.CellSize;
        return d;
    }
}
