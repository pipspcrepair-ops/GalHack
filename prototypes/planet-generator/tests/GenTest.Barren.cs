using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Planets.Gen;

namespace Planets;

/// <summary>
/// Barren worlds (owner, 22 Nov: an airless moon is a grey cratered moonscape, never a dune world), fresh seeds every run:
/// each variant is all dry land (no sea, lake, river or ground under the waterline), only barren ground (regolith, mare,
/// highland, iron-rich, frost) on landforms without weather (no dunes, mesas, canyons, deltas or wetlands), and craters
/// at every size from metres to kilometres stand in its ground, each a bowl under its rim. Its fine terrain costs about
/// what a living world's does.
/// </summary>
public partial class GenTest
{
    /// <summary>Crater sizes, radius m: shell holes underfoot, the ones a walk crosses, the ones a flight crosses, the basins.</summary>
    static readonly (float From, float To)[] CraterBands = { (3f, 10f), (10f, 40f), (40f, 150f), (150f, 2000f) };

    void BarrenWorlds()
    {
        var rng = new Random();
        var weather = new[] { Landform.DuneSea, Landform.Mesas, Landform.Canyonlands, Landform.Delta, Landform.Wetlands, Landform.LakeDistrict,
            Landform.RiverValleys, Landform.Fjords, Landform.Badlands, Landform.Karst, Landform.CoastalCliffs, Landform.Archipelago };
        long barrenMs = 0, livingMs = 0;
        int barrenSamples = 0, livingSamples = 0;
        for (int v = 0; v < Archetypes.Variants(Archetype.Barren); v++)
            for (int s = 0; s < 3; s++)
            {
                int width = v == Archetypes.Metal ? 32 : s == 0 ? 8 : 16;
                ulong seed = (ulong)rng.NextInt64(1, long.MaxValue);
                PlanetSpec spec = PlanetSpec.Roll(seed, Archetype.Barren, v, width);
                WorldMap map = PlanetGenerator.Generate(spec);
                string name = $"barren {map.Def.VariantName} {seed} ({width} km)";
                int wet = 0, low = 0, alive = 0, eroded = 0;
                for (int k = 0; k < map.Count; k++)
                {
                    if (map.Has(k, CellFlags.Water) || map.Has(k, CellFlags.River) || map.RiverFlow[k] > 0f) wet++;
                    if (map.Elevation[k] < PlanetGenerator.BareFloor - 0.01f) low++;
                    if (Minerals.Of((Biome)map.Biome[k]) == null) alive++;
                    if (weather.Contains((Landform)map.Landform[k])) eroded++;
                }
                Check(spec.Airless && wet == 0 && low == 0, $"{name}: no air and all dry land, nothing under its floor ({wet} wet cells, {low} low)");
                Check(alive == 0 && eroded == 0, $"{name}: only barren ground ({alive} cells of other biomes), on landforms no weather made ({eroded} cells of them): {map.Model.Mix}");

                // Craters at every size, each a bowl in the ground under its rim (the freshest of each size, at the
                // game's own spacing for the small ones: a crater a few metres across shows only at a metre).
                var sampler = new SurfaceSampler(map);
                var found = new List<string>();
                int sizes = 0;
                foreach (var (from, to) in CraterBands)
                {
                    var some = map.Model.CratersOver(from).Where(c => c.Radius < to && c.Fresh > 0.8f).Take(30).ToList();
                    int bowls = 0;
                    foreach (var c in some)
                    {
                        double spacing = Math.Clamp(c.Radius / 6.0, 1.0, 16.0);
                        // Its floor half way out (a big one has a peak in its middle) against its rim.
                        float floor = 0f, rim = 0f;
                        for (int k = 0; k < 12; k++)
                        {
                            double a = k * Math.Tau / 12;
                            rim += sampler.HeightAt(c.X + Math.Cos(a) * c.Radius, c.Y + Math.Sin(a) * c.Radius, spacing) / 12f;
                            floor += sampler.HeightAt(c.X + Math.Cos(a) * c.Radius * 0.5, c.Y + Math.Sin(a) * c.Radius * 0.5, spacing) / 12f;
                        }
                        // Its floor well under its rim: a tenth of its radius on rock (less on ice and on a heavy world, its own relief).
                        if (rim - floor > c.Radius * 0.1f * map.Def.CraterRelief) bowls++;
                    }
                    if (some.Count > 0 && bowls >= some.Count * 0.7f) sizes++;
                    found.Add($"{from:F0}-{to:F0} m: {bowls} of {some.Count}");
                }
                Check(sizes >= 3, $"{name}: craters of {sizes} sizes stand in the ground as bowls under their rims ({string.Join("; ", found)})");

                // What its fine terrain costs against a living world's, sample for sample at a metre.
                var watch = Stopwatch.StartNew();
                for (int k = 0; k < 4000; k++) sampler.HeightAt(k * 37.3 % map.WidthMeters, k * 11.9 % map.HeightMeters, 1);
                barrenMs += watch.ElapsedMilliseconds;
                barrenSamples += 4000;
                var living = new SurfaceSampler(PlanetGenerator.Generate(PlanetSpec.Roll(seed, Archetype.Temperate, 0, width)));
                watch.Restart();
                for (int k = 0; k < 4000; k++) living.HeightAt(k * 37.3 % map.WidthMeters, k * 11.9 % map.HeightMeters, 1);
                livingMs += watch.ElapsedMilliseconds;
                livingSamples += 4000;
            }
        float barrenUs = barrenMs * 1000f / barrenSamples, livingUs = livingMs * 1000f / livingSamples;
        Check(barrenUs < livingUs * 1.6f + 1f, $"barren: a fine sample costs {barrenUs:F1} µs against a temperate world's {livingUs:F1} µs");
    }
}
