using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Godot;
using Planets.Gen;
using PlanetSurface.Poi;
using PoiGenerator.Gen;
using Slice.Game;
using Slice.Places;
using Slice.Surface;
using Wildlife;

namespace Slice.Tests;

/// <summary>
/// Places follow their occupant. Over fresh worlds every run (nothing pinned): the occupants a
/// world's places roll match its theme's and fall's weights (and never bring in anyone the world
/// hasn't); the places sited on real planets roll them; and what each place holds is what its
/// occupant says: nesters' nests and bugs, the twisted beasts' den, machines on their rounds,
/// cyborgs, the Husks, people (friendly or raiders holding it), the empty with vermin at most, and
/// a place the growth took full of its Hollows, inside and on the ground round it.
///
/// Run: .\tools\test.ps1 -Project prototypes/slice -Scene res://tests/OccupantTest.tscn
/// </summary>
public partial class OccupantTest : Node
{
    const int WatchdogMinutes = 10;
    int _failures, _passes;
    readonly Stopwatch _clock = Stopwatch.StartNew();
    readonly System.Random _rng = new();
    PoiLibrary _lib;
    WildlifeData _data;

    public override void _Ready()
    {
        var thread = new System.Threading.Thread(() =>
        {
            System.Threading.Thread.Sleep(TimeSpan.FromMinutes(WatchdogMinutes));
            Console.Error.WriteLine($"FAIL  the run took over {WatchdogMinutes} minutes: watchdog");
            System.Environment.Exit(1);
        }) { IsBackground = true };
        thread.Start();
        try
        {
            _lib = WorldChoice.Library;
            _data = DataFiles.LoadWildlife();
            Mix();
            Falls();
            Sited();
            Spawns();
        }
        catch (Exception e) { Check(false, $"the test threw: {e}"); }
        GD.Print(_failures == 0 ? $"ALL PASSED ({_passes} checks in {_clock.Elapsed.TotalSeconds:F1} s)" : $"{_failures} CHECK(S) FAILED ({_passes} passed)");
        Shutdown.Quit(this, _failures == 0 ? 0 : 1);
    }

    void Check(bool ok, string what)
    {
        if (ok) _passes++;
        else _failures++;
        GD.Print($"  {(ok ? "ok   " : "FAIL ")} {what}");
    }

    static readonly string[] Archetypes = { "Temperate", "Arid", "Ice", "Lava", "Alien" };

    ulong Seed() => (ulong)_rng.NextInt64();

    WorldRoll Roll(ulong seed, string arch, WorldRoll.Given given = null)
    {
        WorldProfile profile = WorldProfile.Roll(seed, arch, false);
        if (!profile.Inhabited) profile = WorldProfile.Roll(seed ^ 0x5EED, arch, true);
        return WorldRoll.Roll(_lib, seed, arch, profile, false, given, (float)_rng.NextDouble());
    }

    List<FamilyDef> Applicable() => _lib.Families.Where(f => OccupancyLayer.Applies(f, PoiState.Abandoned)).ToList();

    /// <summary>The share of each code as the main occupant, expected from the world's weights with the family's own leanings.</summary>
    static Dictionary<string, float> Expected(IReadOnlyDictionary<string, float> mix, FamilyDef f)
    {
        var w = new Dictionary<string, float>();
        foreach (var (k, v) in mix)
        {
            if (!Known(k)) continue;
            float x = v;
            if (f.Tags.Contains("farm") && k == "SQT") x *= 2f;
            if (f.Tags.Contains("industry") && k == "MCH") x *= 1.5f;
            w[k] = x;
        }
        float sum = w.Values.Sum();
        return sum <= 0f ? new Dictionary<string, float> { ["EMP"] = 1f } : w.ToDictionary(kv => kv.Key, kv => kv.Value / sum);
    }

    static bool Known(string code) => code is "EMP" or "NST" or "HSK" or "SCV" or "SQT" or "GNG" or "MCH" or "WRP" or "HOL" or "MUT" or "FAC" or "CYB" or "BOT" or "CLT" or "KPR" or "SLP" or "GRD" or "MIM";

    static string CodeOf(Occupant o) => o switch
    {
        Occupant.Empty => "EMP", Occupant.Nesters => "NST", Occupant.Turned => "HSK", Occupant.Scavengers => "SCV", Occupant.Squatters => "SQT", Occupant.Gang => "GNG",
        Occupant.Machines => "MCH", Occupant.Warped => "WRP", Occupant.Prowlers => "MUT", Occupant.Faction => "FAC", Occupant.Cyborgs => "CYB", Occupant.Robots => "BOT",
        Occupant.Cult => "CLT", Occupant.Keepers => "KPR", Occupant.Sleepers => "SLP", Occupant.Guardians => "GRD", _ => "MIM",
    };

    /// <summary>Over fresh worlds, the occupants rolled come out in the proportions the world's weights give.</summary>
    void Mix()
    {
        GD.Print("-- the world's mix --");
        var families = Applicable();
        var observed = new Dictionary<string, float>();
        var expected = new Dictionary<string, float>();
        int rolls = 0, outside = 0, worlds = 0;
        for (int i = 0; i < 160; i++)
        {
            ulong seed = Seed();
            WorldRoll roll = Roll(seed, Archetypes[_rng.Next(Archetypes.Length)]);
            if (roll == null || roll.Occupancy.Count == 0) continue;
            worlds++;
            for (int j = 0; j < 50; j++)
            {
                FamilyDef f = families[_rng.Next(families.Count)];
                OccupancyRoll occ = OccupancyLayer.Roll(roll.Occupancy, f, Seed());
                string code = CodeOf(occ.Main);
                observed[code] = observed.GetValueOrDefault(code) + 1f;
                foreach (var (k, p) in Expected(roll.Occupancy, f)) expected[k] = expected.GetValueOrDefault(k) + p;
                if (!roll.Occupancy.ContainsKey(code) && !(code == "WRP" && roll.Occupancy.ContainsKey("HOL"))) outside++;
                if (occ.Second is { } second && !roll.Occupancy.ContainsKey(CodeOf(second))) outside++;
                rolls++;
            }
        }
        float tvd = 0.5f * observed.Keys.Union(expected.Keys).Sum(k => MathF.Abs(observed.GetValueOrDefault(k) - expected.GetValueOrDefault(k)) / rolls);
        Check(worlds > 100 && tvd < 0.03f, $"{rolls} places on {worlds} fresh worlds: who holds them matches the worlds' weights (distance {tvd:F3}; "
            + string.Join(" ", observed.OrderByDescending(kv => kv.Value).Take(8).Select(kv => $"{kv.Key} {kv.Value / rolls:P0}/{expected.GetValueOrDefault(kv.Key) / rolls:P0}")) + ")");
        Check(outside == 0, $"nobody holds a place the world's theme and fall don't have ({outside} did)");
    }

    /// <summary>Each fall's own occupants take its worlds' places, far more than on worlds that fell otherwise.</summary>
    void Falls()
    {
        GD.Print("-- the fall's occupants --");
        var families = Applicable();
        (string Fall, string[] Codes)[] cases =
        {
            ("biohazard", new[] { "MUT", "NST" }), ("machine_uprising", new[] { "MCH", "BOT" }), ("nuclear_war", new[] { "MUT" }),
            ("sleep_sickness", new[] { "SLP", "KPR" }), ("parasite_swarm", new[] { "NST" }), ("faith_takeover", new[] { "CLT" }), ("husks_fall", new[] { "HSK" }),
        };
        float Share(string fall, string[] codes)
        {
            int hit = 0, n = 0;
            for (int i = 0; i < 24; i++)
            {
                WorldRoll roll = Roll(Seed(), "Temperate", new WorldRoll.Given().Set(LayerKind.Fall, fall));
                if (roll == null || roll.Occupancy.Count == 0) continue;
                for (int j = 0; j < 30; j++, n++)
                    if (codes.Contains(CodeOf(OccupancyLayer.Roll(roll.Occupancy, families[_rng.Next(families.Count)], Seed()).Main))) hit++;
            }
            return n == 0 ? 0f : hit / (float)n;
        }
        foreach (var (fall, codes) in cases)
        {
            float own = Share(fall, codes), other = Share("slow_fade", codes);
            Check(own >= 0.06f && own > other * 2f + 0.04f, $"{fall}: {string.Join("/", codes)} hold {own:P0} of its places, against {other:P0} after a slow fade");
        }
    }

    /// <summary>On real planets, every place that can be held has an occupant from the world's own mix.</summary>
    void Sited()
    {
        GD.Print("-- places sited on real planets --");
        string[] falls = { "biohazard", "machine_uprising", "nuclear_war", "plague" };
        PoiCatalog catalog = SurfaceZone.PoiCatalog;
        foreach (string fall in falls)
        {
            WorldForce.Current = new WorldForce { Fall = fall };
            var spec = new PlanetSpec(Seed(), Archetype.Temperate, 0, Modifier.None);
            WorldMap map = PlanetGenerator.Generate(spec);
            WorldProfile profile = WorldProfile.Roll(spec.Seed, "Temperate", false);
            if (!profile.Inhabited) profile = profile with { Inhabited = true };
            profile = WorldForce.Current.Apply(profile);
            WorldRoll roll = PoiSiting.RollOf(_lib, map, profile);
            LandingSite landing = Landing.Find(map, map.WidthMeters * 0.5, map.HeightMeters * 0.5);
            List<PoiSite> sites = PoiSiting.Place(catalog, map, new GalHack.Terrain.PlanetTerrainSource(map), landing, profile: profile);
            var held = sites.Where(s => s.Instance != null && OccupancyLayer.Applies(s.Family, s.Instance.State)).ToList();
            int rolled = held.Count(s => s.Instance.Occupancy != null);
            int inMix = held.Count(s => s.Instance.Occupancy is { } o && roll.Occupancy.ContainsKey(CodeOf(o.Main)));
            Check(roll != null && held.Count > 0 && rolled == held.Count && inMix == rolled,
                $"a {fall} world: {held.Count} of {sites.Count} places can be held, {rolled} rolled who holds them, {inMix} from the world's own mix ({string.Join(", ", held.Select(s => s.Instance.Occupancy?.Main.ToString() ?? "none").GroupBy(x => x).Select(g => $"{g.Key} {g.Count()}"))})");
        }
        WorldForce.Current = new WorldForce();
    }

    WorldConditions Conditions(WorldRoll roll, ulong seed)
    {
        WorldReading r = WorldReading.Of(roll);
        var w = new WorldConditions { Seed = seed, Alienness = 0.25f, Coreward = Math.Max(0f, roll.Depth), Bloom = r.HollowWorld, Stock = r.Stock, Working = r.Working, Ferals = r.Ferals, Predators = r.Predators, Density = r.Density, Mutagen = r.Mutagen };
        return w;
    }

    /// <summary>What each place holds is what its occupant says, on fresh worlds and fresh places.</summary>
    void Spawns()
    {
        GD.Print("-- what places hold --");
        var families = _lib.Families.Where(f => f.Id is "hospital" or "research_lab" or "sewage_works" or "meridian_apartments" or "office_block" or "warehouse" or "school").ToList();
        if (families.Count < 3) families = Applicable().OrderBy(_ => _rng.Next()).Take(5).ToList();
        var fails = new Dictionary<Occupant, List<string>>();
        var counts = new Dictionary<Occupant, int>();
        var notes = new Dictionary<Occupant, string>();
        int hollowPlaces = 0, hollowOk = 0, haunts = 0, hauntsOk = 0;
        for (int w = 0; w < 6; w++)
        {
            ulong seed = Seed();
            WorldRoll roll = Roll(seed, "Temperate");
            WorldLife life = Bestiary.Roll(_data, Conditions(roll, seed));
            var deep = new PlaceFauna.Deep(life, null, 1f);
            foreach (FamilyDef f in families)
            {
                Layout layout = PoiFactory.BuildLayout(_lib, f, 0, f.Rarity, Seed());
                foreach (Occupant o in Enum.GetValues<Occupant>())
                {
                    PoiInstance inst = PoiFactory.Spawn(_lib, layout, PoiState.Abandoned, Seed(), null, new OccupancyRoll(o));
                    string why = Judge(o, inst, deep, life, out string note);
                    counts[o] = counts.GetValueOrDefault(o) + 1;
                    notes[o] = note;
                    if (why != null) (fails.TryGetValue(o, out var l) ? l : fails[o] = new List<string>()).Add($"{f.Id}: {why}");
                }
                // A place the growth took, with no occupant rolled: its Hollows inside, and round it outside.
                PoiInstance taken = PoiFactory.Spawn(_lib, layout, PoiState.Infested, Seed(), null, null);
                PoiPopulation pop = PlaceFauna.Plan(new PlaceMemory(new PoiSite { Instance = taken, Family = f, Name = f.Id }), deep).Population;
                hollowPlaces++;
                int hollows = pop.Spawns.Count(s => s.Species.Corrupted && life.Hollows.Any(h => s.Species.Id.StartsWith(h.Id, StringComparison.Ordinal)));
                if (hollows >= 2) hollowOk++;
                foreach (WildlifePatch.Haunt h in WildlifePatch.HauntsOf(new[] { new PoiSite { Instance = taken, Family = f, Name = f.Id } }, life))
                {
                    haunts++;
                    if (h.Want >= 3 && h.Kinds.Count > 0 && h.Kinds.All(k => k.Corrupted && !k.Flies)) hauntsOk++;
                }
            }
        }
        foreach (Occupant o in Enum.GetValues<Occupant>())
        {
            int bad = fails.TryGetValue(o, out var l) ? l.Count : 0;
            Check(bad == 0, $"{o}: {counts.GetValueOrDefault(o) - bad} of {counts.GetValueOrDefault(o)} places hold what they should ({notes.GetValueOrDefault(o)}){(bad > 0 ? $"; e.g. {l[0]}" : "")}");
        }
        Check(hollowOk == hollowPlaces, $"a place the growth took holds its Hollow beasts: {hollowOk} of {hollowPlaces}");
        Check(haunts == hollowPlaces && hauntsOk == haunts, $"and they keep to the ground round it outside: {hauntsOk} of {hollowPlaces} have Hollows haunting their ground");
    }

    /// <summary>Why a place doesn't hold what its occupant says (null when it does), and a note of what it holds.</summary>
    static string Judge(Occupant o, PoiInstance inst, PlaceFauna.Deep deep, WorldLife life, out string note)
    {
        var memory = new PlaceMemory(new PoiSite { Instance = inst, Family = inst.Family, Name = inst.Family.Id });
        PoiPopulation pop = PlaceFauna.Plan(memory, deep).Population;
        int people = inst.Townsfolk?.People.Count ?? 0;
        bool hostile = inst.Mood == Mood.Hostile;
        var spawns = pop.Silent ? new List<PoiSpawn>() : pop.Spawns;
        int vermin = spawns.Count(s => s.Species.Role == DeepRole.Subsister);
        int hunters = spawns.Count - vermin;
        int nests = pop.Nests.Count, machines = spawns.Count(s => s.Species.Mechanical), husks = spawns.Count(s => s.Species.Husk) + pop.Masses.Count;
        int twisted = spawns.Count(s => s.Species.Figure == null && s.Species.Mutation > 0.3f && !s.Species.Mechanical);
        int cyborgs = spawns.Count(s => s.Species.Id.StartsWith("cyborg", StringComparison.Ordinal));
        note = $"{people} people{(hostile ? " hostile" : "")}, {nests} nests, {hunters} hunters, {vermin} vermin, {machines} machines, {husks} Husk signs";
        switch (o)
        {
            case Occupant.Empty or Occupant.Sleepers:
                return hunters > 0 || nests > 0 || people > 0 ? "more than vermin" : null;
            case Occupant.Nesters:
                return nests < 1 || spawns.Count(s => s.Nest >= 0) < 1 ? "no nests" : people > 0 ? "people" : null;
            case Occupant.Prowlers:
                return twisted < 2 ? "no twisted beasts" : people > 0 ? "people" : null;
            case Occupant.Turned or Occupant.Warped:
                return husks < 1 ? "no Husks" : null;
            case Occupant.Machines or Occupant.Robots or Occupant.Guardians:
                return machines < 2 ? "no machines" : hunters - machines > 0 ? "wild hunters among the machines" : null;
            case Occupant.Cyborgs:
                return cyborgs < 2 ? "no cyborgs" : null;
            case Occupant.Gang or Occupant.Faction:
                return people < 2 || !hostile ? $"no raiders holding it ({people} people, {inst.Mood})" : hunters > 0 ? "wild hunters among the raiders" : Town.For(memory) == null ? "no town for them" : null;
            default:
                // Squatters, scavengers, cultists, keepers, copies: people, nobody's enemy, and only vermin about.
                return people < 2 ? "no people" : hostile ? "hostile" : hunters > 0 ? "wild hunters among them" : Town.For(memory) == null ? "no town for them" : null;
        }
    }
}
