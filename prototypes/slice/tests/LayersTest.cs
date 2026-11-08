using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using GalHack.Music;
using GalHack.Plants;
using Godot;
using Planets.Gen;
using PoiGenerator.Gen;
using PlanetSurface.Terrain;
using Slice.Game;
using Slice.Start;
using Wildlife;
using Habitat = GalHack.Plants.Habitat;
using Material = PoiGenerator.Gen.Material;
using Plant = GalHack.Plants.Plant;

namespace Slice.Tests;

/// <summary>
/// Layers everywhere: the world's roll read by the rest of the world. Over hundreds of fresh rolls,
/// every run new: plants' strangeness differs between worlds of one type and the world's history
/// changes what grows; the creature mix follows the theme, fall, tech and depth (stock on farmed
/// worlds, feral packs where cities fell, twisted predators after a biohazard, fewer on dead worlds);
/// Hollow worlds roll, and rarely, rarer still on the rim; each rare occupant leaves its own mark;
/// palettes differ from world to world and fade with age; the music hears a dead world, a living
/// one and a war-torn one apart; and the hooks the decay and the weather read are there.
/// Nothing is pinned: constraints only.
///
/// Run: .\tools\test.ps1 -Project prototypes/slice -Scene res://tests/LayersTest.tscn
/// </summary>
public partial class LayersTest : Node
{
    const int WatchdogMinutes = 12;
    int _failures, _passes;
    readonly Stopwatch _clock = Stopwatch.StartNew();
    readonly Random _rng = new();
    PoiLibrary _lib;

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
            // -- --strikes: the war's craters only, and what they cost.
            if (OS.GetCmdlineUserArgs().Contains("--strikes"))
            {
                WarFalls();
                StrikesOnTargets();
                WarFields();
                WarLoads();
                goto done;
            }
            Data();
            var worlds = RollMany(700);
            Flora(worlds);
            Creatures();
            Hollow();
            Occupants();
            Palettes(worlds);
            Sound();
            Hooks(worlds);
            WarFalls();
            StrikesOnTargets();
            WarFields();
            WarLoads();
            done:;
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

    WorldRoll Roll(ulong seed, string arch, WorldRoll.Given given = null, float depth = -1f, bool inhabited = true)
    {
        WorldProfile profile = WorldProfile.Roll(seed, arch, false);
        if (inhabited && !profile.Inhabited) profile = WorldProfile.Roll(seed ^ 0x5EED, arch, true);
        return WorldRoll.Roll(_lib, seed, arch, profile, false, given, depth);
    }

    List<(WorldRoll Roll, string Arch, ulong Seed)> RollMany(int n)
    {
        var all = new List<(WorldRoll, string, ulong)>();
        for (int i = 0; i < n; i++)
        {
            ulong seed = Seed();
            string arch = Archetypes[_rng.Next(Archetypes.Length)];
            WorldProfile profile = WorldProfile.Roll(seed, arch, false);
            if (WorldRoll.Roll(_lib, seed, arch, profile, false, null, (float)_rng.NextDouble()) is { } r) all.Add((r, arch, seed));
        }
        return all;
    }

    void Data()
    {
        GD.Print("-- the data --");
        var layers = _lib.Layers;
        Check(layers.Any(l => l.Kind == LayerKind.Occ && l.Id == "WRP") && layers.All(l => !(l.Kind == LayerKind.Occ && l.Id == "HOL")), "the Husks' warping is WRP now; no HOL left");
        Check(layers.All(l => !l.Is.Contains("bloom")) && layers.Any(l => l.Kind == LayerKind.Rule && l.Id == "warped") && layers.Any(l => l.Kind == LayerKind.Rule && l.Id == "hollow"),
            "bloom means Hollow growth, hollow means the warping: no bloom tag left, rules warped and hollow both there");
        Check(layers.Count(l => l.Kind == LayerKind.Palette && l.Paint.Count == 5) == layers.Count(l => l.Kind == LayerKind.Palette), "every palette gives five paints");
        Check(layers.Count(l => l.Bias.Count > 0) > 200, $"{layers.Count(l => l.Bias.Count > 0)} values lean the world");
    }

    void Flora(List<(WorldRoll Roll, string Arch, ulong Seed)> worlds)
    {
        GD.Print("-- flora --");
        // Strangeness within one type.
        foreach (Archetype a in new[] { Archetype.Temperate, Archetype.Alien })
        {
            var values = new List<float>();
            for (int i = 0; i < 80; i++)
            {
                ulong seed = Seed();
                WorldRoll r = Roll(seed, a.ToString(), null, (float)_rng.NextDouble());
                values.Add(PlanetFlora.StrangenessOf(new PlanetSpec(seed, a, 0, Modifier.None), WorldReading.Of(r).Alien));
            }
            float spread = values.Max() - values.Min();
            int distinct = values.Select(v => MathF.Round(v, 3)).Distinct().Count();
            Check(spread > 0.12f && distinct > 40, $"{a}: plants' strangeness varies from world to world, {values.Min():F2} to {values.Max():F2} ({distinct} of 80 apart)");
        }
        // History changes what grows: the same climate under a dead, an overgrown, a swollen and a fungal world.
        var habitats = new[] { new Habitat(0, 0.6f, 0.6f, 0.7f, new[] { PlantFamily.Conifer, PlantFamily.Broadleaf, PlantFamily.Shrub, PlantFamily.Grass }), new Habitat(1, 0.5f, 0.4f, 0.3f, new[] { PlantFamily.Broadleaf, PlantFamily.Grass, PlantFamily.Fungus }) };
        ulong fs = Seed();
        Herbarium Book(FloraMood m) => Herbarium.Roll(new FloraClimate(fs, 0.6f, 0.6f, 1f, new Tone(1f, 0.97f, 0.9f), 0.03f, 1f, m), habitats);
        Herbarium plain = Book(null), dead = Book(new FloraMood(Dead: 0.9f)), over = Book(new FloraMood(Overgrown: 0.9f)), swollen = Book(new FloraMood(Swollen: 0.9f)), burnt = Book(new FloraMood(Burnt: 0.9f));
        static float Sat(Tone t) { float mx = MathF.Max(t.R, MathF.Max(t.G, t.B)), mn = MathF.Min(t.R, MathF.Min(t.G, t.B)); return mx <= 0f ? 0f : (mx - mn) / mx; }
        static IEnumerable<Plant> Trees(Herbarium h) => h.Plants.Where(p => p.Family is PlantFamily.Conifer or PlantFamily.Broadleaf);
        Check(Trees(dead).Average(p => Sat(p.Leaf)) < Trees(plain).Average(p => Sat(p.Leaf)) * 0.8f, $"a dead world's trees go grey: saturation {Trees(dead).Average(p => Sat(p.Leaf)):F2} against {Trees(plain).Average(p => Sat(p.Leaf)):F2}");
        Check(Trees(over).Average(p => p.Height) > Trees(plain).Average(p => p.Height) * 1.05f && Trees(burnt).Average(p => p.Height) < Trees(plain).Average(p => p.Height) * 0.9f,
            $"overgrown worlds stand taller, burned ones lower: {Trees(over).Average(p => p.Height):F1} / {Trees(plain).Average(p => p.Height):F1} / {Trees(burnt).Average(p => p.Height):F1} m");
        Check(Trees(swollen).Average(p => p.Genome.TrunkRadius / p.Genome.Height) > Trees(plain).Average(p => p.Genome.TrunkRadius / p.Genome.Height) * 1.2f, "a mutagenic world's trees swell");
        Check(new[] { plain, dead, over, swollen, burnt }.Select(h => h.Fingerprint()).Distinct().Count() == 5, "each history grows something different from the same seed and climate");
        // And worlds of one type differ in what their layers did.
        var temperate = worlds.Where(w => w.Arch == "Temperate").Select(w => WorldReading.Of(w.Roll)).ToList();
        int moods = temperate.Select(r => (r.Dead > 0.3f, r.Overgrown > 0.3f, r.Swollen > 0.3f, r.Fungal > 0.25f, r.Burnt > 0.3f, r.Sparse > 0.3f)).Distinct().Count();
        Check(moods >= 6, $"{temperate.Count} temperate worlds fall into {moods} different flora histories");
    }

    WorldConditions Conditions(WorldRoll roll, ulong seed)
    {
        WorldReading r = WorldReading.Of(roll);
        var w = new WorldConditions { Seed = seed, Alienness = 0.25f, Coreward = Math.Max(0f, roll.Depth), Bloom = r.HollowWorld, Stock = r.Stock, Working = r.Working, Ferals = r.Ferals, Predators = r.Predators, Density = r.Density };
        w.Mutagen = Math.Max(w.Mutagen, r.Mutagen);
        return w;
    }

    void Creatures()
    {
        GD.Print("-- creatures --");
        WildlifeData data = DataFiles.LoadWildlife();
        (float Loose, float Ferals, float Twisted, float Kinds, float Density) Mix(Func<WorldRoll.Given> given, string arch = "Temperate", int n = 40)
        {
            float loose = 0, ferals = 0, twisted = 0, kinds = 0, density = 0;
            for (int i = 0; i < n; i++)
            {
                ulong seed = Seed();
                WorldRoll roll = Roll(seed, arch, given());
                WorldLife life = Bestiary.Roll(data, Conditions(roll, seed));
                loose += life.Moving.Any(s => s.Id.Contains("loose-Stock") || s.Id.Contains("loose-Working")) ? 1 : 0;
                ferals += life.Moving.Any(s => s.Id.Contains("loose-Pet")) ? 1 : 0;
                twisted += life.Moving.Any(s => s.Id.EndsWith("-twisted", StringComparison.Ordinal)) ? 1 : 0;
                kinds += life.Moving.Count(s => !s.Husk && !s.Mechanical);
                density += WorldReading.Of(roll).Density;
            }
            return (loose / n, ferals / n, twisted / n, kinds / n, density / n);
        }
        var farmed = Mix(() => new WorldRoll.Given().Set(LayerKind.Trade, "herding").Set(LayerKind.Post, "L").Set(LayerKind.Phase, "spared"));
        var works = Mix(() => new WorldRoll.Given().Set(LayerKind.Trade, "heavy_industry").Set(LayerKind.Post, "H").Set(LayerKind.Phase, "recovered"));
        Check(farmed.Loose > works.Loose + 0.3f, $"stock and working beasts on farmed worlds: {farmed.Loose:P0} of herding worlds, {works.Loose:P0} of works worlds");
        var fallen = Mix(() => new WorldRoll.Given().Set(LayerKind.Phase, "fallen").Set(LayerKind.Fall, "vanishing").Set(LayerKind.Theme, "overgrown_metropolis"));
        var kept = Mix(() => new WorldRoll.Given().Set(LayerKind.Phase, "recovered").Set(LayerKind.Fall, "supply_cutoff").Set(LayerKind.Theme, "plain"));
        Check(fallen.Ferals > kept.Ferals + 0.3f, $"feral packs where cities fell: {fallen.Ferals:P0} against {kept.Ferals:P0} on recovered worlds");
        var bio = Mix(() => new WorldRoll.Given().Set(LayerKind.Fall, "biohazard"));
        var slow = Mix(() => new WorldRoll.Given().Set(LayerKind.Fall, "slow_fade"));
        Check(bio.Twisted > slow.Twisted + 0.3f, $"twisted predators after a biohazard: {bio.Twisted:P0} against {slow.Twisted:P0} after a slow fade");
        var deadWorld = Mix(() => new WorldRoll.Given().Set(LayerKind.Fall, "grey_goo").Set(LayerKind.Fauna, "sparse").Set(LayerKind.Flora, "dead_forest"));
        var rich = Mix(() => new WorldRoll.Given().Set(LayerKind.Fauna, "herds_hunters").Set(LayerKind.Flora, "deep_forest"));
        Check(deadWorld.Kinds < rich.Kinds - 1f && deadWorld.Density < rich.Density * 0.6f, $"fewer creatures on dead worlds: {deadWorld.Kinds:F1} kinds at {deadWorld.Density:F2} density, against {rich.Kinds:F1} at {rich.Density:F2}");
        // Husk odds still follow depth.
        int rim = 0, core = 0;
        for (int i = 0; i < 60; i++)
        {
            ulong s = Seed();
            if (Bestiary.Roll(data, Conditions(Roll(s, "Temperate", null, 0.05f), s)).Moving.Any(x => x.Husk)) rim++;
            if (Bestiary.Roll(data, Conditions(Roll(s, "Temperate", null, 0.95f), s)).Moving.Any(x => x.Husk)) core++;
        }
        Check(core > rim, $"Husks still follow depth: {rim} of 60 rim worlds, {core} of 60 core worlds");
    }

    void Hollow()
    {
        GD.Print("-- Hollow worlds --");
        int n = 1500, any = 0, rim = 0, rimN = 0, deep = 0, deepN = 0;
        for (int i = 0; i < n; i++)
        {
            float depth = (float)_rng.NextDouble();
            ulong seed = Seed();
            string arch = Archetypes[_rng.Next(Archetypes.Length)];
            WorldRoll r = WorldRoll.Roll(_lib, seed, arch, WorldProfile.Roll(seed, arch, false), false, null, depth);
            if (r == null) continue;
            bool h = r.HollowWorld;
            any += h ? 1 : 0;
            if (depth < 0.3f) { rimN++; rim += h ? 1 : 0; }
            else if (depth > 0.6f) { deepN++; deep += h ? 1 : 0; }
        }
        float rate = any / (float)n;
        Check(any > 0 && rate < 0.06f, $"Hollow worlds roll, rarely: {any} of {n} ({rate:P1})");
        Check(rim / (float)Math.Max(1, rimN) <= deep / (float)Math.Max(1, deepN), $"rarer on the rim: {rim} of {rimN} rimward, {deep} of {deepN} coreward");
        // On a Hollow world the fungus has the wildlife.
        WildlifeData data = DataFiles.LoadWildlife();
        ulong s = Seed();
        WorldRoll hollow = Roll(s, "Temperate", new WorldRoll.Given().Set(LayerKind.Flora, "hollow_overrun"), 0.8f);
        WorldConditions w = Conditions(hollow, s);
        WorldLife life = Bestiary.Roll(data, w);
        int taken = life.Moving.Count(x => x.Corrupted), wild = life.Moving.Count(x => !x.Mechanical && !x.Husk && x.Figure == null);
        Check(hollow.HollowWorld && w.Bloom && taken * 2 > wild, $"a Hollow world's fungus has its wildlife: {taken} of {wild} kinds taken");
        Check(WorldReading.Of(hollow).Fungal > 0.8f, "and its plants are fungal");
        Check(hollow.States is { } st && st[(int)PoiState.Infested] > st[(int)PoiState.Active] + st[(int)PoiState.Abandoned] * 0.5f, "and the growth has most of its places");
    }

    void Occupants()
    {
        GD.Print("-- the rarer occupants --");
        string[] marks = { "keeper_post", "sleepers", "guardian_ring", "wrong_object", "warped" };
        Occupant[] who = { Occupant.Keepers, Occupant.Sleepers, Occupant.Guardians, Occupant.Mimics, Occupant.Warped };
        var families = _lib.Families.Where(f => f.Id is "hospital" or "sewage_works" or "meridian_apartments" or "research_lab").ToList();
        foreach (FamilyDef f in families)
        {
            ulong seed = Seed();
            Layout layout = PoiFactory.BuildLayout(_lib, f, 0, f.Rarity, seed);
            var signature = new Dictionary<Occupant, string>();
            for (int i = 0; i < who.Length; i++)
            {
                PoiInstance inst = PoiFactory.Spawn(_lib, layout, PoiState.Abandoned, seed + 3, null, new OccupancyRoll(who[i]));
                bool marked = inst.Markers.Any(m => m.Kind == marks[i]);
                string extra = who[i] switch
                {
                    Occupant.Keepers => $"{inst.Townsfolk?.People.Count ?? 0} keepers, {inst.Lights.Count(l => l.Mode == LightMode.Steady)} lamps",
                    Occupant.Sleepers => inst.Markers.FirstOrDefault(m => m.Kind == "sleepers")?.Note ?? "",
                    Occupant.Guardians => $"{inst.Blocks.Blocks().Count(b => b.Block.Type == BlockType.Pillar && b.Block.Material == Material.Basalt)} posts, {inst.Enemies.Count} guardians",
                    Occupant.Mimics => inst.Townsfolk?.Copies == true ? $"{inst.Townsfolk.People.Count} copies" : "no copies",
                    _ => $"{inst.Enemies.Count} turned",
                };
                Check(marked && (who[i] != Occupant.Mimics || inst.Townsfolk?.Copies == true), $"{f.Id}: {who[i]} leave their mark ({extra})");
                signature[who[i]] = string.Join(",", inst.Blocks.Blocks().Where(b => b.Block.Role == Role.Story).GroupBy(b => (b.Block.Type, b.Block.Material)).OrderBy(g => g.Key.ToString()).Select(g => g.Key.ToString()));
            }
            Check(signature.Values.Distinct().Count() == who.Length, $"{f.Id}: each of the five leaves something different");
        }
        // Who's in a place follows the roll: a keepers' world has keepers.
        // (Keepers are living people: a world with nobody left alive has none, whatever its theme.)
        var kept = Enumerable.Range(0, 20).Select(_ => Roll(Seed(), "Temperate", new WorldRoll.Given().Set(LayerKind.Theme, "lighthouses").Set(LayerKind.Presence, "keepers")))
            .Where(r => r.Fact("people") != "none").ToList();
        Check(kept.Count > 0 && kept.All(r => r.Occupancy.GetValueOrDefault("KPR") > 0f), $"a world of keepers with anyone alive has keeper places: {kept.Count(r => r.Occupancy.GetValueOrDefault("KPR") > 0f)} of {kept.Count}");
    }

    void Palettes(List<(WorldRoll Roll, string Arch, ulong Seed)> worlds)
    {
        GD.Print("-- paint --");
        var paints = worlds.Select(w => WorldReading.Of(w.Roll).Paint).Where(p => p != null).ToList();
        int distinct = paints.Select(p => p.Main).Distinct().Count();
        Check(paints.Count > worlds.Count * 0.8f && distinct >= 12, $"{paints.Count} of {worlds.Count} worlds have a paint, in {distinct} different mains");
        // The same place under two worlds' paint.
        FamilyDef f = _lib.Families.First(x => x.Id == "meridian_apartments");
        ulong seed = Seed();
        Layout layout = PoiFactory.BuildLayout(_lib, f, 0, f.Rarity, seed);
        var pair = paints.Where(p => p.Roof != paints[0].Roof).Take(1).Prepend(paints[0]).ToList();
        var looks = pair.Select(p =>
        {
            PoiInstance inst = PoiFactory.Spawn(_lib, layout, PoiState.Abandoned, seed + 1, null, null);
            p.Apply(inst, inst.SpawnSeed);
            return inst.Blocks.Blocks().Where(b => b.Block.Role == Role.Roof || b.Block.Type == BlockType.Sign || b.Block.Material == Material.Cloth).Select(b => b.Block.Color.ToString()).ToList();
        }).ToList();
        Check(looks.Count == 2 && looks[0].Count > 0 && !looks[0].SequenceEqual(looks[1]), $"one place under two worlds' paint looks different ({looks[0].Count} painted blocks)");
        // Age and the fall fade it.
        var fresh = new WorldPaint(paints[0].Main, paints[0].Accent, paints[0].Roof, paints[0].Cloth, paints[0].Light, 0.05f, 0.05f);
        var old = fresh with { Fade = 0.9f, Dirt = 0.8f };
        static float Sat(Rgb c) { int mx = Math.Max(c.R, Math.Max(c.G, c.B)), mn = Math.Min(c.R, Math.Min(c.G, c.B)); return mx == 0 ? 0f : (mx - mn) / (float)mx; }
        var red = new Rgb(0xc0, 0x30, 0x28);
        Check(Sat(old.Weather(red)) < Sat(fresh.Weather(red)) * 0.7f, "years and the fall fade paint and dirty it");
        var ages = worlds.Select(w => WorldReading.Of(w.Roll)).Where(r => r.Paint != null && r.Settled).ToList();
        var young = ages.Where(r => r.Age < 0.35f).ToList();
        var aged = ages.Where(r => r.Age > 0.9f).ToList();
        Check(young.Count > 0 && aged.Count > 0 && aged.Average(r => r.Paint.Fade) > young.Average(r => r.Paint.Fade) + 0.15f, $"older falls fade more: {(aged.Count > 0 ? aged.Average(r => r.Paint.Fade) : 0):F2} against {(young.Count > 0 ? young.Average(r => r.Paint.Fade) : 0):F2}");
    }

    void Sound()
    {
        GD.Print("-- sound --");
        MoodProfile Of(WorldRoll r)
        {
            WorldReading l = WorldReading.Of(r);
            var w = new MusicWorld(DroneMood.Temperate, 7) { Desolate = l.Desolate, Strife = l.Strife, Living = l.Living, Inhabited = true };
            return Composer.Compose(w, new MusicScene(), WorldVoice.Of(w));
        }
        MoodProfile dead = Of(Roll(Seed(), "Temperate", new WorldRoll.Given().Set(LayerKind.Phase, "fallen").Set(LayerKind.Fall, "vanishing").Set(LayerKind.Sound, "silence").Set(LayerKind.Theme, "stillness")));
        MoodProfile living = Of(Roll(Seed(), "Temperate", new WorldRoll.Given().Set(LayerKind.Phase, "recovered").Set(LayerKind.Fall, "slow_fade").Set(LayerKind.Sound, "pastoral").Set(LayerKind.Theme, "diesel_frontier")));
        MoodProfile war = Of(Roll(Seed(), "Temperate", new WorldRoll.Given().Set(LayerKind.Phase, "falling").Set(LayerKind.Fall, "civil_war").Set(LayerKind.Sound, "distant_guns").Set(LayerKind.Presence, "warlords")));
        Check(dead.PadGain < living.PadGain * 0.8f && dead.MelodyMin > living.MelodyMin * 1.4f, $"a dead world thins and slows: pad {dead.PadGain:F3} against {living.PadGain:F3}, a tune every {dead.MelodyMin:F0} s against {living.MelodyMin:F0} s");
        Check(war.Bed == BedKind.Rumble && war.Sag > living.Sag && war.Tones.Contains(ToneKind.Groan), $"a war-torn world rumbles and sags: bed {war.Bed}, sag {war.Sag:F1}");
        Check(dead.Bed != war.Bed && dead.Bright < living.Bright, $"the three sound apart: beds {dead.Bed} / {living.Bed} / {war.Bed}");
    }

    void Hooks(List<(WorldRoll Roll, string Arch, ulong Seed)> worlds)
    {
        GD.Print("-- hooks --");
        var r = worlds.Select(w => WorldReading.Of(w.Roll)).ToList();
        bool In(float v) => v >= 0f && v <= 1f;
        Check(r.All(x => In(x.Age) && In(x.Severity) && In(x.Storm) && In(x.Dry)), "age, severity, storminess and dryness are all 0 to 1");
        Check(r.Select(x => MathF.Round(x.Severity, 1)).Distinct().Count() >= 4 && r.Select(x => MathF.Round(x.Storm, 1)).Distinct().Count() >= 4 && r.Select(x => MathF.Round(x.Dry, 1)).Distinct().Count() >= 4,
            "and they vary from world to world");
        WorldReading falling = WorldReading.Of(Roll(Seed(), "Temperate", new WorldRoll.Given().Set(LayerKind.Phase, "falling").Set(LayerKind.Age, "now")));
        WorldReading spared = WorldReading.Of(Roll(Seed(), "Temperate", new WorldRoll.Given().Set(LayerKind.Phase, "spared").Set(LayerKind.Age, "long").Set(LayerKind.Fall, "slow_fade")));
        Check(falling.Severity > spared.Severity + 0.4f && spared.Age > falling.Age + 0.6f, $"a world falling now is severe and young ({falling.Severity:F2}, {falling.Age:F2}); one spared long ago mild and old ({spared.Severity:F2}, {spared.Age:F2})");
        GD.Print($"  a sample: {WorldReading.Of(worlds[0].Roll)}");
    }
}
