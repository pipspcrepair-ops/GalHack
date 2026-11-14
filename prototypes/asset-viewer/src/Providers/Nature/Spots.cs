using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using Planets.Gen;

namespace AssetViewer.Providers.Nature;

/// <summary>
/// Finds the place on a generated world where something is (a landform, a biome, a river mouth, a cliff, a
/// crater): the world map is searched, window by window, for the spot that holds most of it. Nothing is
/// forced onto a world; the viewer looks until it finds one that has it.
/// </summary>
public static class Spots
{
    public readonly record struct Found(PlanetSpec Spec, double X, double Z, float Score);

    /// <summary>Scores a window of the map centred on cell (i, j): higher holds more of what is looked for.</summary>
    public delegate float Scorer(WorldMap map, int i, int j);

    public const int WindowHalf = 8;   // cells: a window is 17 cells, about 540 m

    /// <summary>The share of a window's sample cells (every third one) a test holds for.</summary>
    public static float Share(WorldMap map, int i, int j, Func<WorldMap, int, bool> test, int half = WindowHalf)
    {
        int n = 0, hits = 0;
        for (int dj = -half; dj <= half; dj += 3)
        for (int di = -half; di <= half; di += 3)
        {
            n++;
            if (test(map, map.Index(i + di, j + dj))) hits++;
        }
        return hits / (float)n;
    }

    /// <summary>The best spot over the candidate worlds in order, stopping at the first whose score reaches <paramref name="enough"/>.</summary>
    public static Found? Find(IEnumerable<PlanetSpec> candidates, Scorer score, float enough, int maxWorlds = 6)
    {
        Found? best = null;
        int tried = 0;
        foreach (PlanetSpec spec in candidates)
        {
            if (tried++ >= maxWorlds) break;
            NatureWorlds.Prepare();
            WorldMap map = PlanetGenerator.Generate(spec);
            (int bi, int bj, float bs) = (0, 0, float.MinValue);
            for (int j = 6; j < map.Height - 6; j += 3)
            for (int i = 0; i < map.Width; i += 3)
            {
                float s = score(map, i, j);
                if (s > bs) (bi, bj, bs) = (i, j, s);
            }
            if (bs > (best?.Score ?? float.MinValue)) best = new Found(spec, map.CellX(bi), map.CellY(bj), bs);
            if (bs >= enough) break;
        }
        return best;
    }

    /// <summary>The world types in the order a search should try them, the preferred ones first, each rolled from its own seed.</summary>
    public static IEnumerable<PlanetSpec> Candidates(ulong seed, IEnumerable<(Archetype Archetype, int Variant)> preferred, string only = "Any")
    {
        var all = NatureWorlds.Types().ToList();
        if (only != "Any" && all.FindIndex(t => t.Label == only) is int one and >= 0)
        {
            for (int k = 0; k < 4; k++) yield return NatureWorlds.SpecOf(all[one].Archetype, all[one].Variant, seed + (ulong)k * 977);
            yield break;
        }
        var order = preferred.ToList();
        foreach (var t in all) if (!order.Contains((t.Archetype, t.Variant))) order.Add((t.Archetype, t.Variant));
        for (int round = 0; round < 3; round++)
            foreach (var (a, v) in order)
                yield return NatureWorlds.SpecOf(a, v, seed + (ulong)round * 977 + (ulong)((int)a * 2 + v) * 13);
    }

    /// <summary>The types whose landform table has a landform, likeliest first.</summary>
    public static IEnumerable<(Archetype, int)> WorldsWith(Landform landform)
    {
        var list = new List<(Archetype, int, float)>();
        foreach (var t in NatureWorlds.Types())
        {
            ArchetypeDef def = Archetypes.Get(t.Archetype, t.Variant);
            foreach (LandformOdds o in def.Landforms)
                if (o.Landform == landform) list.Add((t.Archetype, t.Variant, o.Weight));
        }
        return list.OrderByDescending(x => x.Item3).Select(x => (x.Item1, x.Item2));
    }
}
