using System;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using Planets.Gen;
using PlanetSurface.Poi;
using PlanetSurface.World;
using Archetype = Planets.Gen.Archetype;

namespace PlanetSurface;

/// <summary>Fresh worlds of every size: a once-lived-in world has places all over it, in step with its area and its old people, and none goes missing quietly.</summary>
public partial class SurfaceTest
{
    async Task DensityChecks()
    {
        var rand = new Random();
        // A new game's population picks reach the profile the world is built from, as a rolled value would.
        int took = 0;
        for (int i = 0; i < 40; i++)
        {
            var spec = PlanetSpec.Roll((ulong)rand.NextInt64(), Archetype.Lava, 0, 8);
            int pop = rand.Next(0, 11);
            var now = (PoiGenerator.Gen.PopulationNow)rand.Next(0, 4);
            var p = PlanetWorld.ProfileFor(spec, rand.Next(2) == 0, new PoiGenerator.Gen.WorldForce { Population = pop, PopulationNow = now });
            if (p.Population == pop && p.Inhabited == pop > 0 && p.Current == now) took++;
        }
        Check(took == 40, $"forced populations reach the profile: {took}/40");
        PoiGenerator.Gen.WorldForce.Current = new PoiGenerator.Gen.WorldForce { Population = 9, PopulationNow = PoiGenerator.Gen.PopulationNow.Settled };
        PlanetWorld crowded = await Task.Run(() => PlanetWorld.Build(PlanetSpec.Roll((ulong)rand.NextInt64(), Archetype.Temperate, 0, 16), Main.Catalog));
        PoiGenerator.Gen.WorldForce.Current = new PoiGenerator.Gen.WorldForce();
        Check(crowded.Profile is { Population: 9, Current: PoiGenerator.Gen.PopulationNow.Settled, Inhabited: true }, $"a forced crowded world builds crowded: {crowded.Profile}, {crowded.Pois.Count} places, {crowded.Pois.Count(q => q.Siting == "city")} city buildings");
        var kinds = new[] { Archetype.Temperate, Archetype.Arid, Archetype.Ice };
        // -- --sizes=64 (or 32,64) builds just those sizes, three worlds each (for timing).
        var sizes = PlanetSpec.Widths.ToList();
        int each = 0;
        foreach (string arg in OS.GetCmdlineUserArgs())
            if (arg.StartsWith("--sizes=")) { sizes = arg["--sizes=".Length..].Split(',').Select(int.Parse).ToList(); each = 3; }
        foreach (int km in sizes)
            for (int n = 0; n < (each > 0 ? each : km >= 64 ? 1 : 2); n++)
            {
                var watch = Stopwatch.StartNew();
                var spec = PlanetSpec.Roll((ulong)rand.NextInt64(), kinds[rand.Next(kinds.Length)], 0, km);
                PlanetWorld w = await Task.Run(() => PlanetWorld.Build(spec, Main.Catalog));
                double area = w.Width / 1000.0 * (w.Height / 1000.0);
                // A city's lots are one place to fly to: counted once, so a city can't stand in for a full map.
                int lots = w.Pois.Count(p => p.Siting == "city");
                int places = w.Pois.Count - lots + (lots > 0 ? 1 : 0), ruins = w.Infra?.Ruins.Count ?? 0;
                double per = places / area;
                // The farthest dry land lies from a place, sampled over the world (km): no river, above the shore (the plan's own rule), clear of the seam bands.
                var ground = new GalHack.Terrain.PlanetTerrainSource(w.Map);
                double gap = 0;
                if (places > 0)
                    for (int i = 0; i < 600; i++)
                    {
                        double x = 420 + rand.NextDouble() * (w.Width - 840), z = 420 + rand.NextDouble() * (w.Height - 840);
                        var g = ground.Sampler.Sample(x, z, 2);
                        if (g.InRiver || g.Height < 2.5f) continue;
                        double best = w.Pois.Min(p =>
                        {
                            double dx = Math.Abs(p.Center.X - x), dz = Math.Abs(p.Center.Z - z);
                            dx = Math.Min(dx, w.Width - dx); dz = Math.Min(dz, w.Height - dz);
                            return Math.Sqrt(dx * dx + dz * dz);
                        });
                        gap = Math.Max(gap, best / 1000.0);
                    }
                GD.Print($"      {km}x{km / 2} km {spec.Archetype}, {w.Profile}: {places} places ({per * 100:F1} per 100 km2, {lots} city lots), {ruins} roadside ruins, " +
                         $"planned {PoiSiting.LastPlanned}, skipped {PoiSiting.LastSkipped}, unseated {PoiSiting.LastUnseated}, farthest dry land from a place {gap:F1} km, {watch.Elapsed.TotalSeconds:F0} s ({PlanetWorld.LastTimes})");
                // Never lived on: sparse, but never a 20 km hole.
                if (!w.Profile.Inhabited) { Check(places == 0 || gap <= 18, $"{km} km world nobody lived on: the farthest spot is {gap:F1} km from any place"); continue; }
                // A peopled world of millions has a place every 17 km2 at least; fewer people, fewer places, but never an empty map.
                double floor = w.Profile.Population >= 6 ? 0.06 : w.Profile.Population >= 3 ? 0.04 : 0.02;
                Check(per >= floor, $"{km} km world (population {w.Profile.Population}) has {per * 100:F1} places per 100 km2 (floor {floor * 100:F1})");
                // Anywhere on a world that had people, a place within a few minutes' flight.
                Check(gap <= 5.5, $"{km} km world (population {w.Profile.Population}): the farthest dry land is {gap:F1} km from any place (limit 5.5)");
                Check(PoiSiting.LastSkipped == 0, $"{km} km world: {PoiSiting.LastSkipped} planned places had no layout");
                Check(PoiSiting.LastUnseated <= Math.Max(2, PoiSiting.LastPlanned / 10), $"{km} km world: {PoiSiting.LastUnseated} of {PoiSiting.LastPlanned} planned places found no ground");
            }
    }
}
