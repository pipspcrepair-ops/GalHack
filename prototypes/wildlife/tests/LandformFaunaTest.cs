using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using Archetype = Planets.Gen.Archetype;
using Landform = Planets.Gen.Landform;
using LandformBlend = Planets.Gen.LandformBlend;
using LandformInfo = Planets.Gen.LandformInfo;
using LandformHabitat = Planets.Gen.LandformHabitat;
using PlanetGenerator = Planets.Gen.PlanetGenerator;
using PlanetSpec = Planets.Gen.PlanetSpec;
using WorldMap = Planets.Gen.WorldMap;
using CellFlags = Planets.Gen.CellFlags;

namespace Wildlife;

/// <summary>
/// Headless test of wildlife built for landforms (design doc rev 423, end of "Landforms"). Run with:
///   godot --headless --path prototypes/wildlife res://tests/LandformFaunaTest.tscn
/// Species are rolled for a real generated planet's landform mix; spawning is checked against the
/// planet's own landform layer. Exits 0 if every check passes.
/// </summary>
public partial class LandformFaunaTest : Node
{
    WildlifeData _data;
    FaunaGenerator _gen;
    int _failures, _checks;

    public override void _Ready()
    {
        try
        {
            _data = DataFiles.LoadWildlife();
            _gen = new FaunaGenerator(_data.Fauna, _data.Activity);
            SameSeedSameSpecies();
            GrowsFromTheMix();
            WorldWithLandforms(out WorldMap map, out PlanetFauna fauna);
            DistinctSetsPerLandform(map, fauna);
            BuiltForTheirGround();
            ColoursFitTheGround(fauna, map);
            PicksByLandform(map, fauna);
            SpawnerKeepsToHomes(map, fauna);
            OldFaunaUnchanged();
        }
        catch (Exception e)
        {
            Fail($"crashed: {e}");
        }
        GD.Print(_failures == 0 ? $"ALL PASSED ({_checks} checks)" : $"{_failures} OF {_checks} CHECK(S) FAILED");
        Shutdown.Quit(this, _failures == 0 ? 0 : 1);
    }

    static readonly Landform[] Mixed = { Landform.Plains, Landform.Mountains, Landform.DuneSea, Landform.Delta, Landform.Hills, Landform.Steppe };

    void SameSeedSameSpecies()
    {
        PlanetFauna a = _gen.Generate(77, 0.3f, false, Mixed), b = _gen.Generate(77, 0.3f, false, Mixed), c = _gen.Generate(78, 0.3f, false, Mixed);
        string Print(PlanetFauna f) => string.Join("\n", f.Species.Select(s =>
            $"{s.Id} {s.Name} {s.CommonName} {s.PartSignature} {string.Join("/", s.Homes)} {s.Adaptation} {s.Colors.Base.ToHtml()} {s.Body.LegLength:F4}"));
        Check(Print(a) == Print(b) && Print(a) != Print(c),
            $"same seed, same landform species: {a.Species.Count} species identical twice over; another seed differs");
    }

    void GrowsFromTheMix()
    {
        int one = _gen.Generate(5, 0.3f, false, new[] { Landform.DuneSea }).Species.Count;
        int two = _gen.Generate(5, 0.3f, false, new[] { Landform.DuneSea, Landform.Mountains }).Species.Count;
        int many = _gen.Generate(5, 0.3f, false, Mixed).Species.Count;
        Check(one is >= 2 and <= 3 && two > one && many >= 12,
            $"species grow from the landform mix: {one} on a one-landform world, {two} with two landforms, {many} with six");
    }

    /// <summary>A real mixed planet: temperate, with plains, hills, steppe, mountains and a delta.</summary>
    void WorldWithLandforms(out WorldMap map, out PlanetFauna fauna)
    {
        map = PlanetGenerator.Generate(PlanetSpec.Roll(22, Archetype.Temperate, 0, 16));
        fauna = _gen.Generate(map.Spec.Seed, 0.3f, false, map.Model.Mix.Landforms);
        GD.Print($"info  {map.Spec}: landforms {map.Model.Mix}; {fauna.Species.Count} species");
        foreach (Species s in fauna.Species)
            GD.Print($"info    {s.Name,-12} {s.CommonName,-34} {s.Behaviour,-16} home {string.Join(", ", s.Homes.Select(LandformInfo.Name))}");
    }

    /// <summary>Each landform has species of its own: at least two found nowhere else, and no two landforms share the same set.</summary>
    void DistinctSetsPerLandform(WorldMap map, PlanetFauna fauna)
    {
        var sets = new Dictionary<Landform, HashSet<string>>();
        foreach (Landform l in map.Model.Mix.Landforms) sets[l] = LandformFauna.At(fauna, l).Select(s => s.Id).ToHashSet();
        int ownOk = 0, distinctPairs = 0, pairs = 0;
        foreach (var (l, set) in sets)
        {
            int own = fauna.Species.Count(s => s.Homes.Length == 1 && s.Homes[0] == l);
            if (own >= 2) ownOk++;
            foreach (var (m, other) in sets)
            {
                if (m <= l) continue;
                pairs++;
                if (!set.SetEquals(other)) distinctPairs++;
            }
        }
        Check(ownOk == sets.Count && distinctPairs == pairs,
            $"distinct species per landform: {ownOk} of {sets.Count} landforms have two or more species of their own; {distinctPairs} of {pairs} pairs of landforms differ");
    }

    /// <summary>Bodies built for the ground: herds and pack hunters on open ground, climbers and gliders on steep, burrowers in sand, waders in the wet.</summary>
    void BuiltForTheirGround()
    {
        int problems = 0, checkedSpecies = 0;
        bool openOk = true, steepOk = true, sandOk = true, wetOk = true;
        foreach (ulong seed in new ulong[] { 3, 17, 29, 41, 53, 67 })
        {
            PlanetFauna f = _gen.Generate(seed, 0.35f, false, new[] { Landform.Plains, Landform.Mountains, Landform.DuneSea, Landform.Wetlands, Landform.Hills });
            foreach (Species s in f.Species)
            {
                checkedSpecies++;
                problems += FaunaQuality.Problems(_data.Fauna, s).Count;
            }
            List<Species> Own(Landform l) => f.Species.Where(s => s.Homes.Length == 1 && s.Homes[0] == l).ToList();
            var open = Own(Landform.Plains);
            openOk &= open.Any(s => s.Behaviour == Behaviour.Herd) && open.Any(s => s.Behaviour is Behaviour.PackHunter or Behaviour.Predator)
                && open.Where(s => s.Adaptation == Adaptation.Runner).All(s => s.Stats.RunSpeed > 0f);
            var steep = Own(Landform.Mountains);
            steepOk &= steep.Any(s => s.Adaptation == Adaptation.Climber && (s.Rig != RigKind.Legged || s.Body.LegStyle == LegStyle.Splayed))
                && steep.Any(s => s.Flies);
            var sand = Own(Landform.DuneSea);
            sandOk &= sand.Count(s => s.Burrows) >= 2;
            var wet = Own(Landform.Wetlands);
            wetOk &= wet.Any(s => s.Adaptation == Adaptation.Wader && (s.Rig != RigKind.Legged || s.Body.LegStyle == LegStyle.Stilt))
                && wet.Any(s => s.Behaviour == Behaviour.Ambusher);
        }
        Check(problems == 0 && openOk && steepOk && sandOk && wetOk,
            $"built for the ground over six planets: plains have herds and their hunters ({openOk}), mountains splay-legged climbers and gliders ({steepOk}), " +
            $"dune seas burrowers and sand-swimmers ({sandOk}), wetlands stilt-legged waders and ambushers ({wetOk}); {problems} quality problems in {checkedSpecies} species");
    }

    /// <summary>
    /// Colours that fit the ground: a species' coat is nearer its own ground's colour than the other landforms'.
    /// Judged over ten fresh planets of the test world's landforms (a coat keeps some of its own colour, so one
    /// planet's dozen species is too few to judge by); a coat of no ground's colour would be nearer about half the time.
    /// </summary>
    void ColoursFitTheGround(PlanetFauna fauna, WorldMap map)
    {
        int nearer = 0, judged = 0;
        for (int w = 0; w < 10; w++)
        {
            PlanetFauna f = w == 0 ? fauna : _gen.Generate((ulong)System.Random.Shared.NextInt64(), 0.3f, false, map.Model.Mix.Landforms);
            var homes = f.Species.SelectMany(s => s.Homes).Distinct().ToArray();
            foreach (Species s in f.Species)
            {
                // (A Hollow-infected animal has gone the fungus's colours: it is judged as what it became, not by its ground.)
                if (s.Homes.Length != 1 || s.Corrupted) continue;
                Color own = LandformFauna.Ground(s.Homes[0]);
                float d = Dist(s.Colors.Base, own);
                float others = homes.Where(h => h != s.Homes[0] && Dist(LandformFauna.Ground(h), own) > 0.15f)
                    .Select(h => Dist(s.Colors.Base, LandformFauna.Ground(h))).DefaultIfEmpty(float.MaxValue).Average();
                judged++;
                if (d < others) nearer++;
            }
        }
        Check(judged > 50 && nearer >= judged * 0.75f, $"colours fit the ground: {nearer} of {judged} species over ten planets are nearer their own ground's colour than other landforms'");
    }

    static float Dist(Color a, Color b) => MathF.Sqrt((a.R - b.R) * (a.R - b.R) + (a.G - b.G) * (a.G - b.G) + (a.B - b.B) * (a.B - b.B));

    /// <summary>Spawn picks across the planet: deep in a region only its own species; near borders, some from the landform next door.</summary>
    void PicksByLandform(WorldMap map, PlanetFauna fauna)
    {
        var rng = new Rng(4242);
        int deep = 0, deepWrong = 0, border = 0, borderOther = 0;
        for (int t = 0; t < 40000 && (deep < 2000 || border < 600); t++)
        {
            double x = rng.Float() * map.WidthMeters, y = rng.Float() * map.HeightMeters;
            if (map.Has(map.CellAt(x, y), CellFlags.Water)) continue;
            LandformBlend b = map.LandformsAt(x, y);
            Species s = LandformFauna.PickFor(fauna, b, rng);
            if (s == null) continue;
            if (b.W0 >= 0.99f)
            {
                deep++;
                if (Array.IndexOf(s.Homes, b.Dominant) < 0) deepWrong++;
            }
            else if (b.W0 < 0.7f)
            {
                border++;
                if (Array.IndexOf(s.Homes, b.Dominant) < 0) borderOther++;
            }
        }
        Check(deep > 500 && deepWrong == 0 && border > 100 && borderOther > border * 0.1f,
            $"spawns by landform: {deep} picks deep in regions, {deepWrong} not at home there; {border} picks near borders, {borderOther} from the landform next door");
    }

    /// <summary>The spawner on a patch of the planet across a border: every creature stands in its home ground.</summary>
    void SpawnerKeepsToHomes(WorldMap map, PlanetFauna fauna)
    {
        // A border between two landforms, on land, with both sides pure a little way off.
        double cx = 0, cz = 0;
        bool found = false;
        var rng = new Rng(77);
        for (int t = 0; t < 200000 && !found; t++)
        {
            double x = rng.Float() * map.WidthMeters, y = rng.Float() * map.HeightMeters;
            LandformBlend b = map.LandformsAt(x, y);
            if (b.Count < 2 || b.W1 < 0.35f || map.Sample(map.Elevation, x, y) < 3f) continue;
            bool dry = true;
            for (int d = 0; d < 8 && dry; d++) dry = map.Sample(map.Elevation, x + Math.Cos(d * 0.785) * 140, y + Math.Sin(d * 0.785) * 140) > 1f;
            if (!dry) continue;
            cx = x;
            cz = y;
            found = true;
        }
        if (!found)
        {
            Fail("spawner: no border on dry land found");
            return;
        }
        var terrain = new Terrain((x, z) => map.Sample(map.Elevation, cx + x, cz + z), 160f);
        var nav = new NavGrid(terrain.HalfSize, 3f);
        var sim = new WildSim(_data, terrain, nav);
        var spawner = new Spawner(sim, fauna, Array.Empty<Vector3>(), 9) { LandformAt = p => map.LandformsAt(cx + p.X, cz + p.Z) };
        spawner.Populate();
        int creatures = 0, away = 0;
        var here = new HashSet<Landform>();
        foreach (Creature c in sim.Creatures)
        {
            creatures++;
            LandformBlend b = map.LandformsAt(cx + c.Position.X, cz + c.Position.Z);
            for (int i = 0; i < b.Count; i++) here.Add(b.LandformAt(i));
            if (c.Species.HomeWeight(b) <= 0.01f) away++;
        }
        LandformBlend centre = map.LandformsAt(cx, cz);
        Check(creatures > 0 && away == 0,
            $"spawner keeps to home ground: {creatures} creatures on a patch across a border ({centre}), {away} away from home ground; " +
            $"{spawner.RejectedForLandform} spots turned down for the wrong ground");
    }

    /// <summary>Fauna rolled the old way (no landforms) is exactly as before: no homes, no adaptations.</summary>
    void OldFaunaUnchanged()
    {
        PlanetFauna f = _gen.Generate(1977, 0.36f, false);
        Check(f.Species.Count is >= 6 and <= 10 && f.Species.All(s => s.Homes.Length == 0 && s.Adaptation == Adaptation.None),
            $"fauna without landforms unchanged: {f.Species.Count} species, none with home landforms or adaptations");
    }

    void Check(bool ok, string what)
    {
        _checks++;
        if (ok) GD.Print($"PASS  {what}");
        else Fail(what);
    }

    void Fail(string what)
    {
        _failures++;
        GD.PrintErr($"FAIL  {what}");
    }
}
