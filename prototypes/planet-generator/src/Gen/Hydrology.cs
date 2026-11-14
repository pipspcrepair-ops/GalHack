using System;
using System.Collections.Generic;

namespace Planets.Gen;

/// <summary>
/// Rivers from high ground to the sea.
///
/// Priority-flood (Barnes et al. 2014) grows inward from the coast in order of height, filling
/// every pit to its spill point plus a tiny gradient, so each land cell has a strictly lower
/// neighbour and water can always reach the sea. Flow then follows the steepest descent on that
/// filled surface; catchments accumulate downstream, and cells draining enough area become rivers.
/// River surfaces never climb downstream: where a river crosses a pit's rim it cuts a gorge.
/// Neighbours wrap on both axes (the map is a torus), so rivers flow across either seam.
/// </summary>
static class Hydrology
{
    const float FillGradient = 0.01f; // metres per cell across filled pits
    static readonly int[] DI = { 1, -1, 0, 0, 1, 1, -1, -1 };
    static readonly int[] DJ = { 0, 0, 1, -1, 1, -1, 1, -1 };
    static readonly float[] Step = { 1f, 1f, 1f, 1f, 1.41421356f, 1.41421356f, 1.41421356f, 1.41421356f };

    public static void Run(WorldMap map, float[] rain, float[] riverArea = null)
    {
        int w = map.Width, h = map.Height, n = map.Count;
        float[] elevation = map.Elevation;

        // Priority-flood from every sea cell that touches land.
        var filled = new float[n];
        var queued = new bool[n];
        var order = new int[n];   // land cells in the order they were reached: always downstream first
        int reached = 0;
        var queue = new PriorityQueue<int, float>();
        for (int k = 0; k < n; k++)
        {
            map.Downstream[k] = -1;
            if (!map.Has(k, CellFlags.Water)) continue;
            filled[k] = elevation[k];
            queued[k] = true;
            if (TouchesLand(map, k)) queue.Enqueue(k, elevation[k]);
        }
        while (queue.TryDequeue(out int c, out float level))
        {
            if (!map.Has(c, CellFlags.Water)) order[reached++] = c;
            int ci = c % w, cj = c / w;
            for (int d = 0; d < 8; d++)
            {
                int nb = map.Index(ci + DI[d], cj + DJ[d]);
                if (queued[nb]) continue;
                queued[nb] = true;
                filled[nb] = Math.Max(elevation[nb], level + FillGradient);
                queue.Enqueue(nb, filled[nb]);
            }
        }

        // Steepest descent on the filled surface, then catchment area summed downstream.
        float cellKm2 = map.CellSize * map.CellSize * 1e-6f;
        var catchment = new float[n];
        for (int r = 0; r < reached; r++)
        {
            int c = order[r];
            int ci = c % w, cj = c / w;
            float best = 0f;
            for (int d = 0; d < 8; d++)
            {
                int nb = map.Index(ci + DI[d], cj + DJ[d]);
                if (!queued[nb]) continue;
                float drop = (filled[c] - filled[nb]) / Step[d];
                if (drop > best)
                {
                    best = drop;
                    map.Downstream[c] = nb;
                }
            }
            catchment[c] = rain[c] * cellKm2;
        }
        for (int r = reached - 1; r >= 0; r--)
        {
            int c = order[r], down = map.Downstream[c];
            if (down >= 0) catchment[down] += catchment[c];
        }

        // River cells and their surface levels, which only ever fall downstream.
        for (int r = 0; r < reached; r++)
        {
            int c = order[r];
            if (catchment[c] < (riverArea != null ? riverArea[c] : map.Def.RiverArea)) continue;
            map.RiverFlow[c] = catchment[c];
            map.RiverLevel[c] = Math.Max(0.3f, elevation[c]);
            map.Flags[c] |= CellFlags.River;
        }
        for (int r = reached - 1; r >= 0; r--)
        {
            int c = order[r], down = map.Downstream[c];
            if (down >= 0 && map.Has(c, CellFlags.River) && map.Has(down, CellFlags.River))
                map.RiverLevel[down] = Math.Min(map.RiverLevel[down], map.RiverLevel[c]);
        }
        for (int k = 0; k < n; k++)
        {
            map.SeaDepth[k] = -elevation[k];
            if (map.Has(k, CellFlags.River)) elevation[k] = Math.Min(elevation[k], map.RiverLevel[k] - 1f);
        }

        MarkBanks(map);
        TraceRivers(map);
    }

    public static float RiverWidth(WorldMap map, float flow) =>
        Math.Clamp(3f + 4.5f * MathF.Sqrt(flow / map.Def.RiverArea), 4f, 40f);

    static bool TouchesLand(WorldMap map, int k)
    {
        int i = k % map.Width, j = k / map.Width;
        for (int d = 0; d < 8; d++)
            if (!map.Has(map.Index(i + DI[d], j + DJ[d]), CellFlags.Water)) return true;
        return false;
    }

    /// <summary>Riparian land along every river (wider rivers get wider banks), and the coastline.</summary>
    static void MarkBanks(WorldMap map)
    {
        int w = map.Width, h = map.Height;
        for (int k = 0; k < map.Count; k++)
        {
            int i = k % w, j = k / w;
            if (map.Has(k, CellFlags.River))
            {
                int reach = RiverWidth(map, map.RiverFlow[k]) > 16f ? 2 : 1;
                for (int dj = -reach; dj <= reach; dj++)
                for (int di = -reach; di <= reach; di++)
                {
                    int nb = map.Index(i + di, j + dj);
                    if (map.Flags[nb] == 0) map.Flags[nb] = CellFlags.Riparian;
                }
            }
            else if (map.Flags[k] == 0)
            {
                for (int d = 0; d < 4; d++)
                {
                    if (map.Has(map.Index(i + DI[d], j + DJ[d]), CellFlags.Water))
                    {
                        map.Flags[k] |= CellFlags.Coast;
                        break;
                    }
                }
            }
        }
    }

    /// <summary>
    /// Turns river cells into polylines: one per source, running until it reaches the sea or
    /// joins a river already traced. Two rounds of Chaikin smoothing take the grid's stair steps out.
    /// </summary>
    static void TraceRivers(WorldMap map)
    {
        int w = map.Width, n = map.Count;
        var fed = new bool[n];
        for (int k = 0; k < n; k++)
        {
            int down = map.Downstream[k];
            if (map.Has(k, CellFlags.River) && down >= 0) fed[down] = true;
        }

        var visited = new bool[n];
        for (int source = 0; source < n; source++)
        {
            if (!map.Has(source, CellFlags.River) || fed[source]) continue;
            var river = new River();
            int c = source;
            double x = map.CellX(c % w), y = map.CellY(c / w);
            visited[c] = true;
            Add(map, river, c, x, y);
            while (true)
            {
                int down = map.Downstream[c];
                if (down < 0) break;
                // Step in unwrapped coordinates, so a river crossing a seam stays one line.
                x += Wrapped(down % w - c % w, w) * map.CellSize;
                y += Wrapped(down / w - c / w, map.Height) * map.CellSize;
                if (map.Has(down, CellFlags.Water))
                {
                    river.X.Add((float)x);
                    river.Y.Add((float)y);
                    river.Level.Add(0f);
                    river.Width.Add(RiverWidth(map, map.RiverFlow[c]) * 1.3f);
                    break;
                }
                Add(map, river, down, x, y);
                if (visited[down]) break;
                visited[down] = true;
                c = down;
            }
            if (river.Count >= 2) map.Rivers.Add(Smooth(Smooth(river)));
        }
    }

    /// <summary>A step of one cell either way across a seam, as -1, 0 or 1.</summary>
    static int Wrapped(int d, int span) => d > span / 2 ? d - span : d < -span / 2 ? d + span : d;

    static void Add(WorldMap map, River river, int cell, double x, double y)
    {
        river.X.Add((float)x);
        river.Y.Add((float)y);
        river.Level.Add(map.RiverLevel[cell]);
        river.Width.Add(RiverWidth(map, map.RiverFlow[cell]));
    }

    static River Smooth(River r)
    {
        var s = new River();
        void Put(int a, int b, float t)
        {
            s.X.Add(Mathx.Lerp(r.X[a], r.X[b], t));
            s.Y.Add(Mathx.Lerp(r.Y[a], r.Y[b], t));
            s.Level.Add(Mathx.Lerp(r.Level[a], r.Level[b], t));
            s.Width.Add(Mathx.Lerp(r.Width[a], r.Width[b], t));
        }
        Put(0, 0, 0f);
        for (int k = 0; k + 1 < r.Count; k++)
        {
            Put(k, k + 1, 0.25f);
            Put(k, k + 1, 0.75f);
        }
        Put(r.Count - 1, r.Count - 1, 0f);
        return s;
    }
}
