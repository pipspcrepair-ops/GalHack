using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using GalHack.Items;
using GalHack.Structures;
using Godot;
using Planets.Gen;
using PlanetSurface.Skies;
using GalHack.Items.Bridge;
using Slice.Game;
using Slice.Orbit;
using Slice.Surface;
using N = System.Numerics;

namespace Slice.Tests;

/// <summary>
/// The star-system and planet rules, over fresh random systems every run (no seed is ever pinned):
/// gravity follows a world's size; the smaller a body the likelier it has no air; suns and gas giants
/// out-size every world you could land on, have bands, storms and spots and no ground; warp fuel lies
/// only on airless worlds (as a deposit, plus loot in space places and ships), hyperspace fuel on the
/// dangerous high-gravity ones (plus ships); every system holds a high-gravity world; every start has
/// a vacuum moon with enough warp fuel for a first jump; every landable world holds what the cheapest
/// flyer the recipe tree allows takes; and nothing marks a planet for a jump fuel.
///
/// Run: G:\GalHack\.tools\queue\test-q.ps1 -Project prototypes/slice -Scene res://tests/SystemsTest.tscn
/// </summary>
public partial class SystemsTest : Node
{
    const int Systems = 240;
    const double TimeLimitMinutes = 12;

    /// <summary>Warp cells for one jump of the smallest warp drive, its longest: 15 AU at one cell an AU (docs/grid-figures.md section 4.4).</summary>
    const int FirstJumpCells = 15;

    int _failures, _passes;

    public override void _Ready()
    {
        new System.Threading.Thread(() =>
        {
            System.Threading.Thread.Sleep(TimeSpan.FromMinutes(TimeLimitMinutes));
            Console.Error.WriteLine($"FAIL  the run took over {TimeLimitMinutes} minutes: watchdog");
            System.Environment.Exit(1);
        }) { IsBackground = true }.Start();
        var clock = Stopwatch.StartNew();
        try { Run(); }
        catch (Exception e) { Check(false, $"threw: {e}"); }
        GD.Print(_failures == 0 ? $"ALL PASSED ({_passes} checks in {clock.Elapsed.TotalSeconds:F1} s)" : $"{_failures} CHECK(S) FAILED ({_passes} passed)");
        GalHack.Testing.Backstop.Quit(this, _failures == 0 ? 0 : 1);
    }

    void Check(bool ok, string what)
    {
        if (ok) _passes++;
        else _failures++;
        GD.Print($"{(ok ? "PASS" : "FAIL")}  {what}");
    }

    /// <summary>The worlds you can land on: planets and moons with ground.</summary>
    static IEnumerable<Orbiter> Landable(StarSystem s) => s.Bodies.Where(b => b.Spec != null);

    void Run()
    {
        var systems = new List<StarSystem>();
        for (int i = 0; i < Systems; i++)
        {
            WorldChoice world = WorldChoice.Fresh(WorldChoice.Types[i % WorldChoice.Types.Count].Key);
            PlanetSpec spec = world.Spec();
            systems.Add(StarSystem.For(spec, SkySystem.For(spec), spec.WidthMeters));
        }
        GD.Print($"({systems.Count} fresh systems, {systems.Sum(s => Landable(s).Count())} worlds with ground)");
        Gravity(systems);
        Vacuum(systems);
        SunsAndGiants(systems);
        HeavyWorlds(systems);
        StartMoons(systems, out List<(StarSystem System, WorldMap Moon)> moons);
        NoWaterOnBareWorlds(Fuels(systems, moons), moons);
        Flyer(systems);
        Loot();
        NoMarkers(systems);
    }

    // ---- 1. Gravity comes from size ---------------------------------------------------------------

    void Gravity(List<StarSystem> systems)
    {
        int off = 0, backwards = 0, worlds = 0;
        var bySize = new SortedDictionary<int, HashSet<float>>();
        foreach (StarSystem s in systems)
            foreach (Orbiter b in Landable(s))
            {
                worlds++;
                float g = s.GravityOf(b, 9.8f);
                // The pull is in step with the world's size, from the one rule (GravityScale), and with its radius.
                if (Math.Abs(g - 9.8 * b.Spec.WidthKm / s.Home.Spec.WidthKm) > 1e-3 || Math.Abs(g / 9.8 - b.Radius / s.Home.Radius) > 1e-3) off++;
                if (!bySize.TryGetValue(b.Spec.WidthKm, out var set)) bySize[b.Spec.WidthKm] = set = new HashSet<float>();
                set.Add(MathF.Round(g, 3));
            }
        var sizes = bySize.Keys.ToList();
        for (int i = 1; i < sizes.Count; i++)
            if (bySize[sizes[i]].Min() <= bySize[sizes[i - 1]].Max()) backwards++;
        Check(off == 0, $"gravity: every one of {worlds} worlds pulls in step with its radius ({off} did not)");
        Check(backwards == 0 && sizes.Count >= 3, $"gravity: a bigger world always pulls harder ({string.Join(", ", sizes.Select(w => $"{w} km {bySize[w].Min():F1} m/s²"))})");
    }

    // ---- 2. Small bodies are very likely vacuum -----------------------------------------------------

    void Vacuum(List<StarSystem> systems)
    {
        var total = new SortedDictionary<int, int>();
        var airless = new SortedDictionary<int, int>();
        int wrongKind = 0, fuelMoons = 0, fuelMoonsBare = 0;
        foreach (StarSystem s in systems)
            foreach (Orbiter b in Landable(s))
            {
                if (b == s.Home) continue;
                int w = b.Spec.WidthKm;
                total[w] = total.GetValueOrDefault(w) + 1;
                if (b.Spec.Airless)
                {
                    airless[w] = airless.GetValueOrDefault(w) + 1;
                    if (!PlanetSpec.Craters(b.Spec.Archetype)) wrongKind++;
                }
                if (b == s.FuelMoon) { fuelMoons++; fuelMoonsBare += b.Spec.Airless ? 1 : 0; }
            }
        double Rate(int w) => airless.GetValueOrDefault(w) / (double)Math.Max(1, total.GetValueOrDefault(w));
        string rates = string.Join(", ", total.Keys.Select(w => $"{w} km {Rate(w) * 100:F0}% of {total[w]}"));
        Check(total.ContainsKey(8) && Rate(8) >= 0.85, $"vacuum: the smallest bodies are very likely airless ({rates})");
        var widths = total.Keys.ToList();
        bool falling = true;
        for (int i = 1; i < widths.Count; i++) falling &= Rate(widths[i]) < Rate(widths[i - 1]);
        Check(falling && Rate(widths[^1]) <= 0.2, $"vacuum: the bigger the body the likelier it keeps its air ({rates})");
        Check(wrongKind == 0, $"vacuum: every airless body is bare rock, ice or lava, never a living world ({wrongKind} were not)");
        Check(fuelMoons == systems.Count && fuelMoonsBare == fuelMoons, $"vacuum: every start's thruster moon is airless ({fuelMoonsBare} of {fuelMoons})");
    }

    // ---- 3. Suns and gas giants -----------------------------------------------------------------------

    void SunsAndGiants(List<StarSystem> systems)
    {
        int bigs = 0, small = 0, ground = 0, plain = 0, outSized = 0, geoOnGround = 0;
        foreach (StarSystem s in systems)
        {
            double largest = Landable(s).Max(b => b.Radius);
            foreach (Orbiter b in s.Bodies.Where(b => b.Kind is OrbiterKind.Star or OrbiterKind.GasGiant))
            {
                bigs++;
                if (b.Radius <= StarSystem.LargestLandableRadius || b.Radius <= largest) small++;
                if (b.Spec != null) ground++;
                Geography g = b.Geography;
                bool bands = b.Kind == OrbiterKind.GasGiant ? g is { BandCount: > 0 } : g != null;
                if (g == null || !bands || g.Marks.Count == 0 || g.Marks.Any(m => m.Size <= 0 || Math.Abs(m.Latitude) > Math.PI / 2)) plain++;
            }
            if (s.Bodies.Any(b => b.Kind == OrbiterKind.GasGiant)) outSized++;
            geoOnGround += s.Bodies.Count(b => b.Spec != null && b.Geography != null);
        }
        Check(small == 0, $"suns and giants: all {bigs} are bigger than the largest landable world ({StarSystem.LargestLandableRadius / 1000:F1} km radius) and than every world in their system ({small} were not)");
        Check(ground == 0 && geoOnGround == 0, $"suns and giants: none has a surface to land on, and no world with ground wears a giant's bands ({ground} did, {geoOnGround} worlds)");
        Check(plain == 0, $"suns and giants: each has its bands, storms or spots ({plain} had none)");
        Check(outSized == systems.Count, $"suns and giants: every system has a gas giant ({outSized} of {systems.Count})");
    }

    // ---- 6. Every system has a dangerous high-gravity world -----------------------------------------------

    void HeavyWorlds(List<StarSystem> systems)
    {
        int without = 0, heavyHome = 0, total = 0;
        var sizes = new SortedDictionary<int, int>();
        foreach (StarSystem s in systems)
        {
            var heavy = Landable(s).Where(b => b.Spec.HighGravity).ToList();
            if (heavy.Count == 0) without++;
            total += heavy.Count;
            foreach (Orbiter b in heavy) sizes[b.Spec.WidthKm] = sizes.GetValueOrDefault(b.Spec.WidthKm) + 1;
            if (s.Home.Spec.HighGravity) heavyHome++;
        }
        Check(without == 0, $"every system has a dangerous high-gravity world ({without} of {systems.Count} had none; {total} found, by size: {string.Join(", ", sizes.Select(p => $"{p.Key} km x{p.Value}"))})");
        Check(heavyHome == 0, $"the start world itself is never the dangerous one ({heavyHome} were)");
    }

    // ---- 7. A start has a vacuum moon with warp fuel for a first jump ----------------------------------------

    void StartMoons(List<StarSystem> systems, out List<(StarSystem, WorldMap)> maps)
    {
        var made = new (StarSystem, WorldMap)[systems.Count];
        System.Threading.Tasks.Parallel.For(0, systems.Count, i => made[i] = (systems[i], BodySurfaces.Coarse(systems[i].FuelMoon.Spec)));
        maps = made.ToList();
        int noMoon = 0, notVacuum = 0, thin = 0, faults = 0, unreachable = 0;
        long least = long.MaxValue;
        foreach (var (s, map) in maps)
        {
            Orbiter moon = s.FuelMoon;
            if (moon == null || moon.Parent != s.Home && moon.Parent != s.Home.Parent) noMoon++;
            if (moon?.Spec is not { Airless: true }) notVacuum++;
            double cells = map.Deposits.All().Where(d => d.Kind == DepositKind.WarpFuel).Sum(Units);
            long biggest = (long)map.Deposits.All().Where(d => d.Kind == DepositKind.WarpFuel).Select(Units).DefaultIfEmpty(0).Max();
            least = Math.Min(least, (long)cells);
            if (cells < FirstJumpCells || biggest < FirstJumpCells) thin++;
            if (s.StartFault() is { } fault) { faults++; GD.Print($"   start fault: {fault}"); }
            if ((s.Position(moon, 0) - s.Position(s.Home, 0)).Length > s.Home.Radius * (StarSystem.ThrusterReachRadii + 1.0) && moon.Parent == s.Home) unreachable++;
        }
        Check(noMoon == 0 && notVacuum == 0, $"start: every system has a vacuum moon to start from ({noMoon} none, {notVacuum} with air)");
        Check(thin == 0, $"start: each of the {maps.Count} moons holds warp fuel for a first jump ({FirstJumpCells} cells) in one deposit and the whole ({thin} too thin; the least {least} cells)");
        Check(faults == 0 && unreachable == 0, $"start: the moon is within thrusters' reach and nothing else the design promises is missing ({faults} faults, {unreachable} out of reach)");
    }

    /// <summary>What a deposit yields dug out, in ore: its sphere of ore at the terrain's radius, a dug cell giving 12 (the poorest grade, 0.6).</summary>
    static double Units(Planets.Gen.Deposit d)
    {
        float r = GalHack.Terrain.PlanetTerrainSource.OreRadius(d);
        return 4.0 / 3.0 * Math.PI * r * r * r * 12 * 0.6;
    }

    // ---- 4 and 5. The jump fuels ------------------------------------------------------------------------------

    List<(StarSystem System, Orbiter Body, WorldMap Map)> Fuels(List<StarSystem> systems, List<(StarSystem System, WorldMap Moon)> moons)
    {
        // Every landable world, its coarse map made here (the first map of a start moon is reused).
        var worlds = systems.SelectMany(s => Landable(s).Select(b => (System: s, Body: b))).ToList();
        var maps = new WorldMap[worlds.Count];
        System.Threading.Tasks.Parallel.For(0, worlds.Count, i => maps[i] = worlds[i].Body == worlds[i].System.FuelMoon ? moons.First(m => m.System == worlds[i].System).Moon : BodySurfaces.Coarse(worlds[i].Body.Spec));
        int warpOn = 0, warpWrong = 0, hyperOn = 0, hyperMissing = 0, hyperElsewhere = 0, bare = 0, heavy = 0;
        for (int i = 0; i < worlds.Count; i++)
        {
            Orbiter b = worlds[i].Body;
            bool warp = maps[i].Deposits.All().Any(d => d.Kind == DepositKind.WarpFuel), crystal = maps[i].Deposits.All().Any(d => d.Kind == DepositKind.Crystal);
            if (warp) warpOn++;
            if (warp != b.Spec.Airless) warpWrong++;
            if (b.Spec.Airless) bare++;
            if (b.Spec.HighGravity)
            {
                heavy++;
                if (crystal) hyperOn++;
                else hyperMissing++;
            }
            else if (crystal && b.Spec.Archetype != Archetype.Alien) hyperElsewhere++; // only the alien crystal world's own signature, else the heavy worlds' alone
        }
        Check(warpWrong == 0 && bare > 0, $"warp fuel: it lies on {warpOn} worlds, every airless one of {bare} and no world with air ({warpWrong} wrong)");
        Check(hyperMissing == 0 && heavy > 0, $"hyperspace fuel: every one of the {heavy} dangerous high-gravity worlds holds some ({hyperMissing} did not)");
        Check(hyperElsewhere == 0, $"hyperspace fuel: it lies nowhere else, but on the alien crystal worlds that always had it ({hyperElsewhere} other worlds had it)");
        return worlds.Select((w, i) => (w.System, w.Body, maps[i])).ToList();
    }

    // ---- 8. Every world has what the cheapest flyer takes ------------------------------------------------------------

    void Flyer(List<StarSystem> systems)
    {
        GameData data = GameData.Shared;
        ItemCatalogue items = data.Items;
        string folder = GameData.Own("data/blueprints");
        Expansion cheapest = null;
        string name = null;
        foreach (string file in Directory.GetFiles(folder, "*.gbp"))
        {
            Structure s;
            try { s = data.Blueprint(Path.GetFileName(file)); }
            catch { continue; }
            if (!StructureStats.Compute(s).Flies) continue;
            Expansion e = items.Graph.Expand(items.Blocks.Total(BuildingBridge.BlocksOf(s)));
            if (cheapest == null || e.Leaves.Total < cheapest.Leaves.Total) (cheapest, name) = (e, Path.GetFileName(file));
        }
        Check(cheapest != null, "flyer: the stock blueprints include a flyer");
        if (cheapest == null) return;

        // What it takes, as ore: each raw leaf of its recipe tree with its count (dug stone is everywhere).
        var need = new Dictionary<DepositKind, int>();
        foreach (var (id, n) in cheapest.Leaves)
        {
            ItemDef leaf = items.Item(id);
            if (leaf.Source == ItemSource.Dug) continue;
            if (!Enum.TryParse(leaf.DepositKind, out DepositKind kind)) { Check(false, $"flyer: {id} is a leaf of the cheapest flyer and no deposit gives it"); return; }
            need[kind] = need.GetValueOrDefault(kind) + n;
        }
        GD.Print($"(the cheapest flyer is {name}: {cheapest.Leaves.Total} ore, tier {cheapest.MaxTier}: {items.Describe(cheapest.Leaves)})");

        var worlds = systems.SelectMany(s => Landable(s)).ToList();
        var short1 = new List<string>();
        var maps = new WorldMap[worlds.Count];
        System.Threading.Tasks.Parallel.For(0, worlds.Count, i => maps[i] = BodySurfaces.Coarse(worlds[i].Spec));
        for (int i = 0; i < worlds.Count; i++)
        {
            var have = maps[i].Deposits.All().GroupBy(d => d.Kind).ToDictionary(g => g.Key, g => g.Sum(Units));
            foreach (var (kind, n) in need)
                if (have.GetValueOrDefault(kind) < n) short1.Add($"{worlds[i].Spec} lacks {kind} ({have.GetValueOrDefault(kind):F0} of {n})");
        }
        Check(short1.Count == 0, $"flyer: all {worlds.Count} worlds hold the {string.Join(", ", need.Select(p => $"{p.Value} {p.Key}"))} of the cheapest flyer ({short1.Count} fell short{(short1.Count > 0 ? ": " + short1[0] : "")})");
    }

    // ---- 4 and 5, loot: in space places and in ships -----------------------------------------------------------------

    void Loot()
    {
        GameData data = GameData.Shared;
        PoiGenerator.Gen.PoiLibrary library = WorldChoice.Library;
        foreach (string fuel in new[] { "warp-cell", "fold-crystal" })
            Check(data.Items.Has(fuel) && library.Loot.Any(l => l.Id == fuel && l.Tags.Contains("ship")), $"loot: {fuel} is in the places' loot pool, leaning to ships");

        // Ships on the ground: every family tagged ship, many seeds.
        int ships = 0, withWarp = 0, withHyper = 0;
        var rng = new Random();
        foreach (var family in library.Families.Where(f => f.Tags.Contains("ship")))
            for (int i = 0; i < 40; i++)
            {
                var inst = PoiGenerator.Gen.PoiFactory.Generate(library, family, i % Math.Max(1, family.Variants.Count), family.Rarity, PoiGenerator.Gen.PoiState.Abandoned, (ulong)rng.NextInt64());
                ships++;
                var ids = inst.Caches.SelectMany(c => c.Items).Select(l => l.Id).ToHashSet();
                if (ids.Contains("warp-cell")) withWarp++;
                if (ids.Contains("fold-crystal") || ids.Contains("fold-crystal-raw")) withHyper++;
            }
        Check(withWarp > 0 && withWarp < ships, $"loot: {withWarp} of {ships} grounded or crashed ships hold warp cells, some and not all");
        Check(withHyper > 0 && withHyper < ships, $"loot: {withHyper} of {ships} grounded or crashed ships hold fold crystal (hyperspace fuel), some and not all");

        // Wrecks in space: the start system's planned derelicts, fresh seeds.
        int sites = 0, spaceWarp = 0;
        OrbitRules rules = OrbitRules.Shared;
        for (int i = 0; i < 30; i++)
        {
            var map = new GlobeMap(16000, 8000, 16000 / Math.Tau);
            foreach (DerelictPlan p in Derelicts.Plan(library, (ulong)rng.NextInt64(), map, 16000 / Math.Tau * 1.2, rules, data.Catalogue, data.Items))
            {
                sites++;
                if (p.Loot["warp-cell"] > 0) spaceWarp++;
            }
        }
        Check(spaceWarp > 0 && spaceWarp < sites, $"loot: {spaceWarp} of {sites} wrecks in space hold warp cells, some and not all");
    }

    // ---- 10. Vacuum worlds have no liquid water -------------------------------------------------------------------

    /// <summary>No sea, lake or river on an airless world: no water or river cell, no ground under the waterline. Ice deposits stay.</summary>
    static bool Dry(WorldMap map)
    {
        for (int k = 0; k < map.Count; k++)
            if (map.Has(k, CellFlags.Water) || map.Has(k, CellFlags.River) || map.Elevation[k] < 0f || map.RiverFlow[k] > 0f) return false;
        return true;
    }

    void NoWaterOnBareWorlds(List<(StarSystem System, Orbiter Body, WorldMap Map)> coarse, List<(StarSystem System, WorldMap Moon)> moons)
    {
        int bare = 0, wet = 0, wetOnes = 0, withAir = 0, watery = 0, ice = 0;
        foreach (var (_, body, map) in coarse)
        {
            if (!body.Spec.Airless)
            {
                withAir++;
                if (!Dry(map)) watery++;
                continue;
            }
            bare++;
            if (!Dry(map)) wet++;
            if (body.Spec.Archetype == Archetype.Ice && map.Deposits.All().Any(d => d.Kind == DepositKind.Water)) ice++;
        }
        // The same at the game's own resolution, on every start's moon.
        var full = new bool[moons.Count];
        System.Threading.Tasks.Parallel.For(0, moons.Count, i => full[i] = Dry(PlanetGenerator.Generate(moons[i].System.FuelMoon.Spec)));
        wetOnes = full.Count(d => !d);
        Check(bare > 0 && wet == 0, $"no water: none of {bare} airless worlds has a sea, lake or river, or ground under the waterline ({wet} had) ; {withAir} worlds with air keep theirs ({watery} have some)");
        Check(watery > 0, "no water: the worlds with air still have their seas and rivers (the rule is the vacuum's alone)");
        Check(wetOnes == 0, $"no water: and at the game's own resolution none of the {full.Length} start moons has any ({wetOnes} had)");
        Check(ice > 0, $"no water: ice deposits stay on airless ice worlds ({ice} such worlds had them)");
    }

    // ---- 9. No map marker for warp fuel -----------------------------------------------------------------------

    void NoMarkers(List<StarSystem> systems)
    {
        var fuels = new[] { "warp-fuel", "hyperdrive-fuel", "hyperspace-fuel", "warp-cell", "fold-crystal" };
        int marked = systems.Sum(s => s.Bodies.Sum(b => b.Deposits.Count(d => fuels.Any(f => d.Contains(f, StringComparison.OrdinalIgnoreCase)))));
        Check(marked == 0, $"no markers: no planet or moon in {systems.Count} systems is marked as holding a jump fuel ({marked} were)");
        Check(DepositFinder.Kinds.All(k => k is not (DepositKind.WarpFuel or DepositKind.Crystal)), "no markers: the scanner and the compass never look for a jump fuel");
    }
}
