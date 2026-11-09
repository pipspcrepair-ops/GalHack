using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Godot;
using Planets.Gen;
using PoiGenerator.Gen;
using PlanetSurface.Terrain;
using PlanetSurface.World;

namespace Slice.Tests;

/// <summary>
/// What a war's craters cost: world load time on fresh worlds of each kind of war, against the same worlds
/// without their strikes, and the per-frame work they add (a ground cover cell built round a crater, a height query in one).
/// </summary>
public partial class LayersTest
{
    /// <summary>The most a war may add to a world's load, as a share of a world without one, and the most a cover cell may take round its craters, ms.</summary>
    const double LoadShare = 0.35, CoverCellMs = 20;

    void WarLoads()
    {
        GD.Print("-- what the craters cost --");
        WorldForce before = WorldForce.Current;
        bool start = PlanetSurface.Poi.PoiSiting.StartWorld;
        PlanetSurface.Poi.PoiSiting.StartWorld = false;
        var ms = new Dictionary<string, List<double>>();
        var bareMs = new Dictionary<string, List<double>>();
        var cover = new List<double>();
        var height = new List<double>();
        var plain = new List<double>();
        var plan = new List<double>();
        try
        {
            // Each world is built twice, the same seed and the same fall, with its war's strikes and without (in turn
            // which first): what the strikes add. Two different worlds' loads differ by seconds with their places and
            // their city, whatever their war did.
            int turn = 0;
            foreach (string fall in new[] { "nuclear_war", "orbital_bombardment", "kinetic_strikes", "invasion" })
                for (int i = 0; i < 3; i++, turn++)
                {
                    WorldForce.Current = new WorldForce { Fall = fall, MidCollapse = false, Before = TechLevel.Mid };
                    PlanetSpec spec = PlanetSpec.Roll(Seed(), Archetype.Temperate, 0);
                    PlanetWorld w = null, bare = null;
                    double took = 0, without = 0;
                    foreach (bool off in turn % 2 == 0 ? new[] { false, true } : new[] { true, false })
                    {
                        PlanetSurface.Infra.StrikeSites.Off = off;
                        var watch = Stopwatch.StartNew();
                        PlanetWorld built;
                        try { built = PlanetWorld.Build(spec, Slice.Surface.SurfaceZone.PoiCatalog); }
                        finally { PlanetSurface.Infra.StrikeSites.Off = false; }
                        if (off) (bare, without) = (built, watch.Elapsed.TotalMilliseconds);
                        else (w, took) = (built, watch.Elapsed.TotalMilliseconds);
                    }
                    (ms.TryGetValue(fall, out var list) ? list : ms[fall] = new List<double>()).Add(took);
                    (bareMs.TryGetValue(fall, out var none) ? none : bareMs[fall] = new List<double>()).Add(without);
                    var bowls = w.Blasts.Where(b => b.Bowl).ToList();
                    plan.Add(w.Infra.StrikeMs);
                    GD.Print($"(load {fall}: {took:F0} ms, {without:F0} ms without its strikes; {w.Blasts.Count} blasts, {bowls.Count} craters, strikes planned in {w.Infra.StrikeMs:F0} ms)");
                    if (i == 0)
                    {
                        // Ordinary ground, for the cost of a cover cell anywhere: the same world without its war.
                        var at = new Stopwatch();
                        int n = 0;
                        for (int k = 0; k < 400 && n < 25; k++)
                        {
                            // Dry land, where cover grows.
                            int cx = _rng.Next((int)(bare.Map.WidthMeters / CoverBuilder.CellSize)), cz = _rng.Next((int)(bare.Map.HeightMeters / CoverBuilder.CellSize));
                            if (bare.Map.Has(bare.Map.CellAt((cx + 0.5) * CoverBuilder.CellSize, (cz + 0.5) * CoverBuilder.CellSize), CellFlags.Water)) continue;
                            at.Start();
                            CoverBuilder.Build(cx, cz, bare.Source, bare.Palette, new HashSet<long>());
                            at.Stop();
                            n++;
                        }
                        if (n > 0) plain.Add(at.Elapsed.TotalMilliseconds / n);
                    }
                    if (bowls.Count == 0) continue;
                    // The cover cells round a crater, and height queries in it: what the streamer does every frame it moves.
                    Strike s = bowls[_rng.Next(bowls.Count)];
                    var t = Stopwatch.StartNew();
                    int cells = 0;
                    for (int dz = -2; dz <= 2; dz++)
                        for (int dx = -2; dx <= 2; dx++, cells++)
                            CoverBuilder.Build((int)Math.Floor(s.X / CoverBuilder.CellSize) + dx, (int)Math.Floor(s.Z / CoverBuilder.CellSize) + dz, w.Source, w.Palette, new HashSet<long>());
                    cover.Add(t.Elapsed.TotalMilliseconds / cells);
                    t.Restart();
                    for (int k = 0; k < 4000; k++) w.Source.SurfaceHeight(s.X + _rng.NextDouble() * 200 - 100, s.Z + _rng.NextDouble() * 200 - 100);
                    height.Add(t.Elapsed.TotalMilliseconds * 1000 / 4000);
                }
        }
        finally
        {
            WorldForce.Current = before;
            PlanetSurface.Poi.PoiSiting.StartWorld = start;
        }
        // The worst war against the same worlds without their strikes.
        var falls = ms.Select(k => (Fall: k.Key, With: k.Value.Average(), Without: bareMs[k.Key].Average())).ToList();
        foreach (var f in falls)
            GD.Print($"(load {f.Fall}: mean {f.With:F0} ms with its strikes, {f.Without:F0} ms without)");
        var worst = falls.OrderByDescending(f => f.With - f.Without * (1 + LoadShare)).First();
        Check(worst.With <= worst.Without * (1 + LoadShare) + 1500, $"a war's world loads in {worst.With:F0} ms at worst against {worst.Without:F0} ms for the same worlds without its strikes ({worst.Fall})");
        Check(cover.Count > 0 && cover.Average() <= CoverCellMs, $"a ground cover cell round a crater takes {(cover.Count > 0 ? cover.Average() : 0):F2} ms (on dry land without a war {(plain.Count > 0 ? plain.Average() : 0):F2} ms; built on workers); a height query in one {(height.Count > 0 ? height.Average() : 0):F1} us; a war's strikes planned in {(plan.Count > 0 ? plan.Average() : 0):F0} ms on average, {(plan.Count > 0 ? plan.Max() : 0):F0} ms at most");
    }
}
