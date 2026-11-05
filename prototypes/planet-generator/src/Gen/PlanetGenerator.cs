using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading.Tasks;

namespace Planets.Gen;

/// <summary>
/// Builds the coarse world map for a planet spec. Pure C#, no engine types, deterministic from the
/// seed: the noise passes run in parallel but every cell depends only on its own coordinates.
///
/// Order matters and follows the design document: continents and oceans first (sea level by
/// quantile), then rivers (they need the final terrain), then climate (it needs to know where
/// water is), then biomes, then resource deposits on the finished ground.
/// </summary>
public static class PlanetGenerator
{
    public const float DefaultCellSize = 32f;

    public static WorldMap Generate(PlanetSpec spec, float cellSize = DefaultCellSize)
    {
        var watch = Stopwatch.StartNew();
        ArchetypeDef def = Archetypes.For(spec);
        var model = new TerrainModel(spec, def);
        var map = new WorldMap(spec, def, model, cellSize);
        int w = map.Width, h = map.Height, n = map.Count;

        // Continents, mountains, volcanoes and craters, before sea level exists.
        var raw = new float[n];
        Parallel.For(0, h, j =>
        {
            double y = map.CellY(j);
            for (int i = 0; i < w; i++)
            {
                int k = j * w + i;
                raw[k] = model.Raw(map.CellX(i), y, cellSize, out map.Special[k]);
            }
        });

        // Sea level: the quantile that leaves the target fraction of the planet above water.
        map.LandTarget = RollLandTarget(spec, def);
        var sorted = (float[])raw.Clone();
        Array.Sort(sorted);
        model.SeaOffset = Quantile(sorted, 1f - map.LandTarget);
        // Landforms cut lakes, pools, delta channels and fjords below the water after sea level is
        // chosen, and deltas build out over the shallows. A sparse first pass measures what that
        // does to the land, and sea level is chosen again to make up for it.
        int sampled = 0, wasLand = 0, isLand = 0;
        for (int j = 2; j < h; j += 4)
        for (int i = 2; i < w; i += 4)
        {
            int k = j * w + i;
            sampled++;
            if (raw[k] > model.SeaOffset) wasLand++;
            if (model.Shape(raw[k], map.CellX(i), map.CellY(j), cellSize) >= 0f) isLand++;
        }
        float lost = sampled > 0 ? (wasLand - isLand) / (float)sampled : 0f;
        if (Math.Abs(lost) > 0.002f) model.SeaOffset = Quantile(sorted, 1f - Math.Clamp(map.LandTarget + lost, 0.02f, 0.98f));

        // Landform by landform, the final height; and what each cell's landform means for its
        // rivers (plains and wetlands gather many, deserts few), its rain and how dry its air is.
        var rain = new float[n];
        var riverArea = new float[n];
        var dry = new float[n];
        Parallel.For(0, h, j =>
        {
            double y = map.CellY(j);
            for (int i = 0; i < w; i++)
            {
                int k = j * w + i;
                double x = map.CellX(i);
                float e = model.ShapeCell(raw[k], x, y, cellSize, out Landform landform, out float area, out float wet, out dry[k]);
                map.Elevation[k] = e;
                map.Landform[k] = (byte)landform;
                riverArea[k] = def.RiverArea * Math.Clamp(area, 0.6f, 4f);
                if (e < 0f) map.Flags[k] = CellFlags.Water;
                else rain[k] = model.Rain(x, y) * def.Rain * wet;
            }
        });

        Hydrology.Run(map, rain, riverArea);
        Climate.Run(map, dry);
        BiomeRules.Run(map);
        map.Deposits = new DepositField(map);

        int land = 0;
        for (int k = 0; k < n; k++)
            if (!map.Has(k, CellFlags.Water)) land++;
        map.LandFraction = land / (float)n;
        map.GenerationMs = watch.Elapsed.TotalMilliseconds;
        return map;
    }

    /// <summary>A land target inside the archetype's band. Archipelagos sit low in it: many small islands.</summary>
    internal static float RollLandTarget(PlanetSpec spec, ArchetypeDef def)
    {
        var rng = new Rng(Hash.Of(spec.Seed, 0x4C414E44));
        float t = spec.Has(Modifier.Archipelago) ? rng.Range(0.1f, 0.4f) : rng.Range(0.15f, 0.85f);
        return Mathx.Lerp(def.LandMin, def.LandMax, t);
    }

    /// <summary>The q-quantile of values already sorted ascending.</summary>
    static float Quantile(float[] sorted, float q)
    {
        int index = Math.Clamp((int)(q * sorted.Length), 0, sorted.Length - 1);
        return sorted[index];
    }
}
