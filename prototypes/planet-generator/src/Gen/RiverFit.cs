using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace Planets.Gen;

/// <summary>
/// River surfaces fitted to the land they actually run over. Hydrology traces rivers on the coarse
/// map after filling its pits, so a river keeps the fill's level straight across a basin the fine
/// terrain never filled, and its smoothed line runs along hillsides: left alone, its water stands
/// in the air, up to a hundred metres over the ground. Here each river's level comes down to the
/// lowest land beside its line, only ever falling toward the mouth (it can't climb out again; the
/// sampler cuts its way through as a gorge), and a tributary meets its main river at the main
/// river's level. A pure function of the map, worked out once per map, the same on every machine.
/// </summary>
public static class RiverFit
{
    const int Every = 4;            // river points between land samples (about 32 m)
    const double Spacing = 8;       // how fine a look at the land: its detail is the banks' job
    const float Below = 0.8f;       // the surface stays this far under the lowest land beside it
    const float JoinReach = 48f;    // a river ending this near another's line runs into it
    const float JoinGrade = 0.08f;  // how fast a tributary falls to meet it, a metre in twelve or so
    const float TouchGap = 12f;     // rivers whose channels come this near have met
    const float TouchStep = 1f;     // and a difference in level worth bringing down there: less is a rapid's
    const float MaxFall = 0.14f;    // the steepest a river runs: rapids, a metre in seven, never a sheet of water on end
    const float EstuaryReach = 16f; // how near the sea a river's levee may come before the river comes down to it, and how far above that it is at the sea's level

    static readonly ConditionalWeakTable<WorldMap, float[][]> Cache = new();

    /// <summary>Each river's fitted surface level, point by point (the same order as <see cref="WorldMap.Rivers"/>).</summary>
    public static float[][] Levels(WorldMap map, TerrainModel model) => Cache.GetValue(map, m => Fit(m, model));

    static float[][] Fit(WorldMap map, TerrainModel model)
    {
        var rivers = map.Rivers;
        var levels = new float[rivers.Count][];
        double w = map.WidthMeters, h = map.HeightMeters;
        for (int r = 0; r < rivers.Count; r++)
        {
            River river = rivers[r];
            var fit = new float[river.Count];
            float run = float.MaxValue;
            float low = float.MaxValue;
            int mouth = -1;
            for (int k = 0; k < river.Count; k++)
            {
                if (k % Every == 0) low = LowestBeside(river, k, model, w, h);
                run = Math.Min(run, Math.Min(river.Level[k], low - Below));
                fit[k] = Math.Max(run, SurfaceSampler.RiverDrop);
                if (mouth < 0 && InSea(map, river, k, model, w, h, river.Width[k] * 0.5f + Math.Min(SurfaceSampler.BankReach(fit[k]), EstuaryReach))) mouth = k;
            }
            // Where it reaches the sea a river comes down to the sea's level: from a little above the first of its
            // channel the sea is drawn over, its water is the sea's (an estuary), and above that it falls to it as a
            // tributary falls to its main river. Left at the land's level it stood metres over the sea at its mouth.
            if (mouth >= 0)
            {
                int from = mouth;
                for (float back = 0f; from > 0 && back < river.Width[mouth] * 0.5f + 2f * EstuaryReach; from--)
                    back += MathF.Sqrt(MathF.Pow(river.X[from] - river.X[from - 1], 2) + MathF.Pow(river.Y[from] - river.Y[from - 1], 2));
                for (int k = from; k < river.Count; k++) fit[k] = SurfaceSampler.RiverDrop;
                FallTo(river, fit, from, SurfaceSampler.RiverDrop);
            }
            levels[r] = fit;
        }
        // Tributaries: where one ends on another's line, what's downstream there stands no higher than
        // it arrives, and it falls to meet that. Each pass can lower a river others run into.
        var points = new Dictionary<(int, int), List<(int River, int K)>>();
        const double Cell = 64;
        (int, int) Key(double x, double y) => ((int)Math.Floor(Mathx.WrapX(x, w) / Cell), (int)Math.Floor(Mathx.Wrap(y, h) / Cell));
        for (int r = 0; r < rivers.Count; r++)
            for (int k = 0; k < rivers[r].Count; k += 2)
            {
                var key = Key(rivers[r].X[k], rivers[r].Y[k]);
                if (!points.TryGetValue(key, out var list)) points[key] = list = new List<(int, int)>();
                list.Add((r, k));
            }
        for (int pass = 0; pass < 6; pass++)
        {
            bool moved = false;
            for (int r = 0; r < rivers.Count; r++) moved |= Ease(rivers[r], levels[r]);
            for (int r = 0; r < rivers.Count; r++)
            {
                River river = rivers[r];
                int end = river.Count - 1;
                if (end < 1 || levels[r][end] <= SurfaceSampler.RiverDrop + 0.01f) continue;
                var (cx, cy) = Key(river.X[end], river.Y[end]);
                int main = -1, at = -1;
                float best = JoinReach * JoinReach;
                for (int j = cy - 1; j <= cy + 1; j++)
                    for (int i = cx - 1; i <= cx + 1; i++)
                    {
                        if (!points.TryGetValue((i, j), out var list)) continue;
                        foreach (var (o, k) in list)
                        {
                            if (o == r) continue;
                            float dx = (float)Mathx.WrapDelta(rivers[o].X[k] - river.X[end], w), dy = (float)Mathx.WrapDelta(rivers[o].Y[k] - river.Y[end], h);
                            float d = dx * dx + dy * dy;
                            if (d < best) { best = d; main = o; at = k; }
                        }
                    }
                if (main < 0) continue;
                float join = Math.Min(levels[r][end], levels[main][at]);
                // The main river from the join on stands no higher than the tributary arrives.
                for (int k = at; k < rivers[main].Count; k++)
                    if (levels[main][k] > join) { levels[main][k] = join; moved = true; }
                // The tributary falls to it over its last stretch.
                moved |= FallTo(river, levels[r], end, join);
            }
            // Rivers that touch before any end (a tributary running in alongside its main river, say):
            // the higher comes down to the lower where their channels meet, so no wall of water stands between.
            for (int r = 0; r < rivers.Count; r++)
            {
                River river = rivers[r];
                for (int k = 0; k < river.Count; k += 2)
                {
                    var (cx, cy) = Key(river.X[k], river.Y[k]);
                    float nearest = float.MaxValue, touch = float.MaxValue;
                    for (int j = cy - 1; j <= cy + 1; j++)
                        for (int i = cx - 1; i <= cx + 1; i++)
                        {
                            if (!points.TryGetValue((i, j), out var list)) continue;
                            foreach (var (o, at) in list)
                            {
                                if (o == r) continue;
                                float dx = (float)Mathx.WrapDelta(rivers[o].X[at] - river.X[k], w), dy = (float)Mathx.WrapDelta(rivers[o].Y[at] - river.Y[k], h);
                                float reach = (river.Width[k] + rivers[o].Width[at]) * 0.5f + TouchGap, d = dx * dx + dy * dy;
                                if (d < reach * reach && d < nearest) { nearest = d; touch = levels[o][at]; }
                            }
                        }
                    if (touch >= levels[r][k] - TouchStep) continue;
                    for (int j = k; j < river.Count; j++)
                        if (levels[r][j] > touch) levels[r][j] = touch;
                    FallTo(river, levels[r], k, touch);
                    moved = true;
                    break;
                }
            }
            if (!moved) break;
        }
        return levels;
    }

    /// <summary>Brings a river down to a level at a point, falling to it over the stretch above. True if anything moved.</summary>
    static bool FallTo(River river, float[] level, int at, float to)
    {
        bool moved = false;
        float back = 0f;
        for (int k = at; k >= 0; k--)
        {
            if (k < at)
            {
                float sx = river.X[k + 1] - river.X[k], sy = river.Y[k + 1] - river.Y[k];
                back += MathF.Sqrt(sx * sx + sy * sy);
            }
            float cap = to + back * JoinGrade;
            if (level[k] <= cap) break;
            level[k] = cap;
            moved = true;
        }
        return moved;
    }

    /// <summary>Spreads every drop back up the river, so it falls as rapids down a cut it has made rather than over a cliff of its own.</summary>
    static bool Ease(River river, float[] level)
    {
        bool moved = false;
        for (int k = river.Count - 2; k >= 0; k--)
        {
            float sx = river.X[k + 1] - river.X[k], sy = river.Y[k + 1] - river.Y[k];
            float cap = level[k + 1] + MathF.Sqrt(sx * sx + sy * sy) * MaxFall;
            if (level[k] <= cap) continue;
            level[k] = cap;
            moved = true;
        }
        return moved;
    }

    /// <summary>
    /// Where a river point's channel lies: the sampler looks the river up from a displaced point, so the channel
    /// sits off its line by the displacement there. Step back by it (two rounds settle it near enough).
    /// </summary>
    public static void Channel(River river, int k, TerrainModel model, double w, double h, out double qx, out double qy)
    {
        double x = river.X[k], y = river.Y[k];
        qx = x;
        qy = y;
        for (int i = 0; i < 2; i++)
        {
            model.Meander(Mathx.WrapX(qx, w), Mathx.Wrap(qy, h), out double mx, out double my);
            qx = x - mx;
            qy = y - my;
        }
    }

    /// <summary>
    /// Whether the sea is drawn over a river point's channel or within <paramref name="side"/> of its middle either
    /// way (its levee's reach: a river beside the sea, held up over it, stood a bank out in the water).
    /// </summary>
    static bool InSea(WorldMap map, River river, int k, TerrainModel model, double w, double h, float side)
    {
        Channel(river, k, model, w, h, out double cx, out double cy);
        int a = Math.Max(0, k - 1), b = Math.Min(river.Count - 1, k + 1);
        float tx = river.X[b] - river.X[a], ty = river.Y[b] - river.Y[a], len = MathF.Max(MathF.Sqrt(tx * tx + ty * ty), 1e-3f);
        for (float s = -side; s <= side; s += Math.Max(side * 0.5f, 1f))
            if (map.IsSea(cx - ty / len * s, cy + tx / len * s)) return true;
        return false;
    }

    /// <summary>The lowest land at a river point: on its line and either side of its channel, where the meander puts it.</summary>
    static float LowestBeside(River river, int k, TerrainModel model, double w, double h)
    {
        int a = Math.Max(0, k - 1), b = Math.Min(river.Count - 1, k + 1);
        float tx = river.X[b] - river.X[a], ty = river.Y[b] - river.Y[a], len = MathF.Sqrt(tx * tx + ty * ty);
        if (len < 1e-3f) return model.Elevation(Mathx.WrapX(river.X[k], w), Mathx.Wrap(river.Y[k], h), Spacing);
        float nx = -ty / len, ny = tx / len, side = river.Width[k] * 0.5f + 6f;
        Channel(river, k, model, w, h, out double qx, out double qy);
        float low = float.MaxValue;
        foreach (float s in new[] { 0f, -side, side })
            low = Math.Min(low, model.Elevation(Mathx.WrapX(qx + nx * s, w), Mathx.Wrap(qy + ny * s, h), Spacing));
        return low;
    }
}
