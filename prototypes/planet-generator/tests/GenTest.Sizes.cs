using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Godot;
using Planets.Gen;
using Planets.Look;
using Planets.Orbit;

namespace Planets;

/// <summary>
/// Planets come in Empyrion's four size classes. The roll keeps to its odds, moons stay small and
/// start worlds are always 16x8 km; at every size landforms, regions and the globe still work,
/// and a bigger world has bigger or more regions, not stretched ones.
/// </summary>
public partial class GenTest
{
    static readonly int[] SizeWidths = { 8, 16, 32, 64 };

    void SizeClasses()
    {
        const int Rolls = 200000;
        var planets = new Dictionary<int, int>();
        var moons = new Dictionary<int, int>();
        bool startsAll16 = true, repeatable = true;
        for (ulong seed = 0; seed < Rolls; seed++)
        {
            int w = PlanetSpec.RollWidthKm(seed), m = PlanetSpec.RollWidthKm(seed, WorldKind.Moon);
            planets[w] = planets.GetValueOrDefault(w) + 1;
            moons[m] = moons.GetValueOrDefault(m) + 1;
            startsAll16 &= PlanetSpec.RollWidthKm(seed, WorldKind.Start) == 16;
            repeatable &= PlanetSpec.RollWidthKm(seed) == w && PlanetSpec.RollWidthKm(seed, WorldKind.Moon) == m;
        }
        float Share(Dictionary<int, int> counts, int w) => counts.GetValueOrDefault(w) / (float)Rolls;
        var odds = new[] { (8, 0.05f), (16, 0.4f), (32, 0.5f), (64, 0.05f) };
        Check(odds.All(o => MathF.Abs(Share(planets, o.Item1) - o.Item2) < 0.004f) && planets.Keys.All(SizeWidths.Contains),
            $"size classes: over {Rolls} seeds, " + string.Join(", ", odds.Select(o => $"{o.Item1}x{o.Item1 / 2} km {Share(planets, o.Item1):P1} (want {o.Item2:P0})")));
        Check(moons.Keys.All(w => w is 8 or 16) && MathF.Abs(Share(moons, 8) - 5f / 45f) < 0.004f,
            $"moons roll only the two small classes: 8x4 km {Share(moons, 8):P1}, 16x8 km {Share(moons, 16):P1}");
        Check(startsAll16 && repeatable, $"start worlds are always 16x8 km ({startsAll16}), and the same seed rolls the same size ({repeatable})");

        PlanetSpec named = PlanetSpec.Roll(4242, Archetype.Temperate, 0), sized = PlanetSpec.RollSized(4242, Archetype.Temperate, 0);
        Check(named.WidthKm == 16 && sized.WidthKm == PlanetSpec.RollWidthKm(4242) && sized.Modifiers == named.Modifiers
              && PlanetSpec.RollSized(4242, Archetype.Temperate, 0, WorldKind.Start).WidthKm == 16,
            $"the size roll stands apart: Roll without a width is still 16 km, RollSized on seed 4242 gives {sized.WidthKm} km with the same modifiers");
    }

    void SizedWorlds()
    {
        // Regions, from the landform layer alone: more of them or bigger ones as a world grows, square-ish at every size.
        var worst = new List<string>();
        foreach (int width in SizeWidths)
        {
            float widest = 0f, smallest = float.MaxValue, largest = 0f;
            var counts = new List<int>();
            int singles = 0, worlds = 0;
            for (ulong s = 0; s < 200; s++)
            {
                LandformLayer layer = ModelOf(RandomSpec(s * 31 + 6100, width)).Landforms;
                double rw = layer.Width / layer.RegionsX, rh = layer.Height / layer.RegionsY;
                widest = Math.Max(widest, (float)(Math.Max(rw, rh) / Math.Min(rw, rh)));
                smallest = Math.Min(smallest, (float)Math.Min(rw, rh));
                largest = Math.Max(largest, (float)Math.Max(rw, rh));
                counts.Add(layer.RegionsX * layer.RegionsY);
                worlds++;
                if (layer.Mix.Count == 1) singles++;
            }
            bool ok = widest <= 1.4f && smallest >= 1300f && largest <= 4700f && singles >= worlds * 0.13f && singles <= worlds * 0.28f;
            Check(ok, $"regions at {width}x{width / 2} km: {counts.Max()} regions, each {smallest:F0}-{largest:F0} m across, at most {widest:F2} times as long as wide; {singles} of {worlds} worlds one landform");
            worst.Add($"{width} km {counts.Max()}");
        }
        int RegionsAt(int width) => ModelOf(RandomSpec(6100, width)).Landforms is var l ? l.RegionsX * l.RegionsY : 0;
        Check(RegionsAt(8) < RegionsAt(16) || RegionsAt(16) <= RegionsAt(32), $"bigger worlds have more regions, or bigger ones: {string.Join(", ", worst)}");

        // Whole worlds at the two ends: each landform keeps its character, regions don't smear along one axis.
        foreach (int width in new[] { 8, 64 })
        {
            int worlds = width == 8 ? 10 : 4;
            for (int w = 0; w < worlds; w++)
            {
                var (a, v) = Variants().ElementAt(w * (10 / worlds));
                var watch = Stopwatch.StartNew();
                WorldMap map = PlanetGenerator.Generate(PlanetSpec.Roll((ulong)(6500 + w * 13), a, v, width));
                double ms = watch.Elapsed.TotalMilliseconds;
                float across = RunLength(map, 1, 0), down = RunLength(map, 0, 1);
                float ratio = Math.Max(across, down) / Math.Max(1f, Math.Min(across, down));
                int land = 0;
                for (int k = 0; k < map.Count; k++) if (!map.Has(k, CellFlags.Water)) land++;
                float share = land / (float)map.Count;
                bool bare = land == 0 || map.Landforms.Present(0.004f).Count() == 0;
                Check(!bare && ratio <= 1.7f,
                    $"{width}x{width / 2} km {a}/{map.Def.VariantName}: {map.Landforms.Present(0.004f).Count()} landforms, {share:P0} land, landform patches {across * map.CellSize:F0} m across and {down * map.CellSize:F0} m down (ratio {ratio:F2}, 1.7); {ms:F0} ms");
            }
        }
        foreach (int width in new[] { 8, 64 })
        foreach (var (landform, a, v, min) in new[] { (Landform.Plains, 0, 0, 0.2f), (Landform.Mountains, 0, 0, 0.08f), (Landform.DuneSea, 1, 0, 0.15f) })
        {
            PlanetSpec spec = null;
            for (ulong s = 1; s < 400 && spec == null; s++)
            {
                PlanetSpec candidate = PlanetSpec.Roll(s, (Archetype)a, v, width);
                if (ModelOf(candidate).Landforms.Present(0f).Any(p => p.landform == landform && p.share >= min)) spec = candidate;
            }
            if (spec == null) { Check(false, $"no {landform} world at {width}x{width / 2} km in 400 seeds"); continue; }
            WorldMap map = PlanetGenerator.Generate(spec);
            Character c = Measure(map, new SurfaceSampler(map), landform);
            bool ok = landform switch
            {
                Landform.Plains => c.Gentle >= 0.75f && c.Relief1Km <= 45f,
                Landform.Mountains => c.Relief1Km >= 120f && c.Steep30 >= 0.1f,
                _ => c.Steep30 >= 0.01f && c.Steep45 <= 0.02f && c.Relief1Km >= 15f,
            };
            Check(ok, $"{LandformInfo.Name(landform)} keeps its character at {width}x{width / 2} km (seed {spec.Seed}): {c.Gentle:P0} gentle, {c.Steep30:P0} over 30°, {c.Steep45:P0} over 45°, relief in 1 km {c.Relief1Km:F0} m");
        }

        // The globe and the colour map at every size, and what the biggest costs.
        foreach (int width in SizeWidths)
        {
            var watch = Stopwatch.StartNew();
            WorldMap map = PlanetGenerator.Generate(PlanetSpec.Roll(9001, Archetype.Temperate, 0, width));
            double mapMs = watch.Elapsed.TotalMilliseconds;
            watch.Restart();
            ColorMap colors = ColorMap.Build(map, ArchetypeLook.For(map.Spec), Math.Clamp(map.Width * 4, 2048, 4096));
            double colorMs = watch.Elapsed.TotalMilliseconds;
            watch.Restart();
            GlobeRelief.Data globe = GlobeRelief.Build(map);
            double globeMs = watch.Elapsed.TotalMilliseconds;
            int stride = globe.Columns + 1;
            float seam = 0f;
            bool finite = globe.Vertices.All(p => float.IsFinite(p.X + p.Y + p.Z)) && globe.Normals.All(n => float.IsFinite(n.X + n.Y + n.Z));
            for (int j = 0; j <= globe.Rows; j++) seam = Math.Max(seam, (globe.Vertices[j * stride] - globe.Vertices[j * stride + globe.Columns]).Length());
            float total = (float)(mapMs + colorMs + globeMs);
            // 16x8 km has 2 s for its map: the biggest, sixteen times the ground, has 16 s for all three.
            Check(finite && seam < 1e-6f && colors.Albedo.Length > 0 && (width < 64 || total < 16000f),
                $"{width}x{width / 2} km ({map.Width}x{map.Height} cells): map {mapMs:F0} ms, colour map {colorMs:F0} ms, globe {globeMs:F0} ms; globe seam {seam:E1}");
        }
    }

    /// <summary>How far the dominant landform runs along a direction over land, in cells on average.</summary>
    static float RunLength(WorldMap map, int di, int dj)
    {
        long cells = 0, runs = 0;
        int lines = di != 0 ? map.Height : map.Width, along = di != 0 ? map.Width : map.Height;
        for (int line = 0; line < lines; line++)
        for (int t = 0; t < along; t++)
        {
            int k = di != 0 ? map.Index(t, line) : map.Index(line, t);
            int next = di != 0 ? map.Index(t + 1, line) : map.Index(line, t + 1);
            if (map.Has(k, CellFlags.Water)) continue;
            cells++;
            if (map.Has(next, CellFlags.Water) || map.Landform[next] != map.Landform[k]) runs++;
        }
        return runs == 0 ? along : cells / (float)runs;
    }
}
