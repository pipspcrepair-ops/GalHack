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
/// Placement by purpose, over fresh random worlds every run (nothing pinned):
/// - encounters per place by depth: every place has something by its state, threats build with
///   depth, the way in is quiet and one room in five holds nothing;
/// - no spawn inside a wall, floor, furniture or a brick, and none in view, beside or behind you;
/// - a place the Hollow growth took always has Hollow beasts in it and round it, however it is held;
/// - robots only where the world's old or current era was mid or high tech, never of a low-tech make;
/// - every place holds what its rolled occupant says (the occupant guides);
/// - people in the rooms of their jobs, a guard on the lookout.
///
/// Run: G:\GalHack\.tools\queue\test-q.ps1 -Project prototypes/slice -Scene res://tests/SpawnTest.tscn
/// </summary>
public partial class SpawnTest : Node
{
    const int WatchdogMinutes = 12;
    int _failures, _passes;
    readonly Stopwatch _clock = Stopwatch.StartNew();
    readonly System.Random _rng = new();
    PoiLibrary _lib;
    WildlifeData _data;

    public override void _Ready()
    {
        new System.Threading.Thread(() =>
        {
            System.Threading.Thread.Sleep(TimeSpan.FromMinutes(WatchdogMinutes));
            Console.Error.WriteLine($"FAIL  the run took over {WatchdogMinutes} minutes: watchdog");
            System.Environment.Exit(1);
        }) { IsBackground = true }.Start();
        try
        {
            _lib = WorldChoice.Library;
            _data = DataFiles.LoadWildlife();
            Encounters();
            Solids();
            Hollows();
            Robots_();
            Guides();
            People();
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

    /// <summary>A fresh world's life, its story and eras from its roll (eras forced where given).</summary>
    WorldLife Life(out WorldRoll roll, string arch = "Temperate", WorldRoll.Given given = null, int? before = null, int? now = null)
    {
        ulong seed = Seed();
        roll = Roll(seed, arch, given);
        WorldReading r = WorldReading.Of(roll);
        var w = new WorldConditions { Seed = seed, Alienness = 0.25f, Coreward = Math.Max(0f, roll.Depth), Bloom = r.HollowWorld, Stock = r.Stock, Working = r.Working, Ferals = r.Ferals, Predators = r.Predators, Density = r.Density, Mutagen = r.Mutagen };
        WildlifePatch.Story(w, roll, WorldProfile.Roll(seed, arch, false), r);
        if (before is int b) w.Before = b;
        if (now is int n) w.Now = n;
        return Bestiary.Roll(_data, w);
    }

    List<FamilyDef> Held() => _lib.Families.Where(f => OccupancyLayer.Applies(f, PoiState.Abandoned)).ToList();

    PoiInstance Place(FamilyDef f, PoiState state, OccupancyRoll occ = null) =>
        PoiFactory.Spawn(_lib, PoiFactory.BuildLayout(_lib, f, 0, f.Rarity, Seed()), state, Seed(), null, occ);

    static PlaceMemory Memory(PoiInstance inst) => new(new PoiSite { Instance = inst, Family = inst.Family, Name = inst.Family.Id });

    static PoiPlan PlanOf(PlaceMemory m, PlaceFauna.Deep deep, out PoiPopulation pop)
    {
        pop = PlaceFauna.Plan(m, deep).Population;
        // The plan's rooms again (the population keeps only its spawns): hops and depths by room.
        var plan = new PoiPlan();
        foreach (PlacedModule mod in m.Inst.Layout.Modules)
            plan.Rooms.Add(new PoiRoom { Index = mod.Index, Depth = Math.Max(0, -mod.Storey), Kind = mod.Kind == ModuleKind.Entrance ? RoomKind.Entrance : mod.Kind == ModuleKind.Corridor ? RoomKind.Corridor : RoomKind.Room });
        foreach (Link l in m.Inst.Layout.Links)
        {
            plan.Rooms.Find(r => r.Index == l.A)?.Links.Add(l.B);
            plan.Rooms.Find(r => r.Index == l.B)?.Links.Add(l.A);
        }
        return plan;
    }

    /// <summary>Encounters by depth, the floor by state, the quiet way in and quiet rooms.</summary>
    void Encounters()
    {
        GD.Print("-- encounters by depth --");
        var families = Held();
        var under = families.Where(f => f.Tags.Contains("underground")).ToList();
        int places = 0, silent = 0, withSomething = 0, levels = 0, levelsHeld = 0, quietOk = 0, quietOf = 0, gateLoud = 0, clashes = 0, guardedOf = 0, guarded = 0;
        float nearT = 0, nearR = 0, groundT = 0, groundR = 0, deepT = 0, deepR = 0, floor = 0, threats = 0;
        var byState = new Dictionary<string, (int Places, int Threats, int Vermin)>();
        for (int w = 0; w < 10; w++)
        {
            WorldLife life = Life(out _, Archetypes[_rng.Next(Archetypes.Length)]);
            var deep = new PlaceFauna.Deep(life, null, (float)_rng.NextDouble());
            for (int i = 0; i < 14; i++)
            {
                FamilyDef f = i % 2 == 0 && under.Count > 0 ? under[_rng.Next(under.Count)] : families[_rng.Next(families.Count)];
                PoiState state = _rng.Next(3) switch { 0 => PoiState.Ruined, _ => PoiState.Abandoned };
                PoiInstance inst = Place(f, state, i % 3 == 0 ? new OccupancyRoll(Occupant.Empty) : null);
                PlaceMemory m = Memory(inst);
                PoiPlan plan = PlanOf(m, deep, out PoiPopulation pop);
                places++;
                string key = $"{state}{(inst.Occupancy != null ? " (nobody holds it)" : "")}";
                var spawns = pop.Silent ? new List<PoiSpawn>() : pop.Spawns;
                int t = spawns.Count(PoiFauna.Threat), v = spawns.Count(s => !PoiFauna.Threat(s) && !s.Species.Passive);
                var b = byState.GetValueOrDefault(key);
                byState[key] = (b.Places + 1, b.Threats + t, b.Vermin + v);
                if (pop.Silent) { silent++; continue; }
                if (spawns.Any(s => !s.Species.Passive)) withSomething++;
                if (pop.Clash != null) clashes++;
                Dictionary<int, int> hops = PoiFauna.Hops(plan);
                var rooms = plan.Rooms.Where(r => inst.Layout.Modules[r.Index].Kind != ModuleKind.Outdoor).ToList();
                int Threats(PoiRoom r) => spawns.Count(s => PoiFauna.Threat(s) && s.Route == null && s.Room == r.Index);
                foreach (PoiRoom r in rooms)
                {
                    int n = Threats(r);
                    int h = hops.GetValueOrDefault(r.Index, 99);
                    if (r.Depth >= 1) { deepT += n; deepR++; }
                    else if (h <= 1) { nearT += n; nearR++; }
                    else { groundT += n; groundR++; }
                    if (r.Kind == RoomKind.Entrance && n > 0 && rooms.Count > 2 && !spawns.Any(s => s.Room == r.Index && (s.Species.Husk || s.Species.Corrupted || s.Nest >= 0))) gateLoud++;
                }
                // Every level below ground has its monster (where a level has a dark room for one).
                foreach (int d in rooms.Where(r => r.Depth >= 1).Select(r => r.Depth).Distinct())
                {
                    levels++;
                    if (rooms.Any(r => r.Depth == d && Threats(r) > 0) || spawns.Any(s => s.Route != null && PoiFauna.Threat(s) && rooms.Find(r => r.Index == s.Room)?.Depth == d)) levelsHeld++;
                }
                if (rooms.Count >= 5)
                {
                    quietOf++;
                    if (rooms.Count(r => Threats(r) == 0) >= rooms.Count * 0.2f) quietOk++;
                }
                // Loot worth having: something on the way to it.
                foreach (LootCache c in inst.Caches.Where(c => !c.PickedOver && (c.Tier >= 2 || c.InVault)))
                {
                    if (inst.Occupancy?.Main == Occupant.Empty) break;
                    guardedOf++;
                    var near = new HashSet<int> { c.Module };
                    foreach (Link l in inst.Layout.Links) { if (l.A == c.Module) near.Add(l.B); if (l.B == c.Module) near.Add(l.A); }
                    if (spawns.Any(s => PoiFauna.Threat(s) && near.Contains(s.Room))) guarded++;
                }
                threats += t;
                floor += rooms.Sum(r => inst.Layout.Modules[r.Index].Cells.Count(c => c.Y == inst.Layout.Modules[r.Index].Min.Y));
            }
        }
        foreach (var (k, b) in byState.OrderBy(kv => kv.Key)) GD.Print($"    {k}: {b.Places} places, {b.Threats / (float)Math.Max(1, b.Places):F1} threats and {b.Vermin / (float)Math.Max(1, b.Places):F1} vermin a place");
        float near_ = nearT / Math.Max(1f, nearR), ground = groundT / Math.Max(1f, groundR), deep_ = deepT / Math.Max(1f, deepR);
        GD.Print($"    threats a room: by the way in {near_:F2}, further in {ground:F2}, below ground {deep_:F2}; {threats * 100f / Math.Max(1f, floor):F2} a 100 m2 of floor (place-patterns E2: 0.3 to 3)");
        Check(silent <= places * 0.15f, $"few places are silent on fresh worlds: {silent} of {places} (aliveness follows the world)");
        Check(withSomething == places - silent, $"every place that isn't silent has something in it: {withSomething} of {places - silent}");
        Check(deep_ >= near_ * 1.5f && deep_ > ground, $"encounters build with depth: {near_:F2} a room by the way in, {ground:F2} further in, {deep_:F2} below ground");
        Check(levels > 20 && levelsHeld >= levels * 0.9f, $"the dark has its monsters: {levelsHeld} of {levels} levels below ground have one");
        Check(gateLoud == 0, $"the way in is quiet: {gateLoud} places have something waiting in it");
        Check(quietOf > 20 && quietOk >= quietOf * 0.9f, $"one room in five is quiet: {quietOk} of {quietOf} places of five rooms or more");
        Check(guardedOf == 0 || guarded >= guardedOf * 0.8f, $"loot worth having is guarded: {guarded} of {guardedOf} good caches have something in or next to their room");
        Check(clashes > 0, $"mixed encounters: an armed group against a nest in {clashes} places");
    }

    /// <summary>Spawned into a place's own blocks and furniture: nothing inside anything; and with you at the way in, nothing in view, beside or behind you.</summary>
    void Solids()
    {
        GD.Print("-- fair spawning --");
        var families = Held().Where(f => f.Tags.Contains("underground") || _rng.Next(3) == 0).ToList();
        Decor decor = Decor.Shared;
        int made = 0, inside = 0, dropped = 0, seen = 0, places = 0, fairMade = 0, waited = 0, left = 0;
        string example = "", seenExample = "";
        for (int w = 0; w < 3; w++)
        {
            WorldLife life = Life(out _);
            var deep = new PlaceFauna.Deep(life, null, 1f);
            for (int i = 0; i < 6; i++)
            {
                FamilyDef f = families[_rng.Next(families.Count)];
                PoiInstance inst = Place(f, PoiState.Abandoned);
                PlaceMemory m = Memory(inst);
                m.Rooms = decor?.Plan(m);
                for (int pass = 0; pass < 2; pass++)
                {
                    PoiPopulation pop = PlaceFauna.Plan(m, deep).Population;
                    if (pop.Silent) continue;
                    places++;
                    var terrain = new Wildlife.Terrain((x, z) => -50f, 64f);
                    var sim = new WildSim(_data, terrain, new Wildlife.NavGrid(terrain.HalfSize, 3f));
                    BlockSpace space = PlaceFauna.SpaceOf(m);
                    Func<Vector3, bool> fair = null;
                    Vector3 eye = default, look = default;
                    if (pass == 1)
                    {
                        // You at the way in, looking in.
                        PoiGenerator.Gen.Int3 gate = inst.Layout.EntranceCell;
                        eye = new Vector3(gate.X + 0.5f, gate.Y + 1.6f, gate.Z + 0.5f);
                        PlacedModule first = inst.Layout.Modules.FirstOrDefault(mm => mm.Kind != ModuleKind.Outdoor && mm.Kind != ModuleKind.Entrance) ?? inst.Layout.Modules[0];
                        look = new Vector3((first.Min.X + first.Max.X) * 0.5f, eye.Y, (first.Min.Z + first.Max.Z) * 0.5f) - eye;
                        look = look.LengthSquared() > 0.01f ? look.Normalized() : Vector3.Forward;
                        deep.Eye = new PlaceFauna.Deep.Viewer(eye, look);
                        fair = PlaceFauna.Fair(deep, null, Vector3.Zero, space);
                        deep.Eye = null;
                    }
                    int planned = pop.Spawns.Count;
                    List<Creature> cs = PoiFauna.Spawn(sim, pop, space, fair);
                    foreach (Creature c in cs)
                    {
                        if (c.Removed) { dropped++; continue; }
                        made++;
                        if (pass == 1) fairMade++;
                        if (!c.InWalls && Interpenetration.Inside(sim, c) && inside++ == 0) example = $"; e.g. a {c.Species.CommonName} at {c.Position} in {f.Id}";
                        if (pass == 1 && !c.InWalls && !PlaceFauna.Unseen(eye, look, MathF.Cos(Mathf.DegToRad(_data.Spawning.ViewConeDegrees)), _data.Spawning.ViewDistance, _data.Spawning.CaveClearance, space, c.Position) && seen++ == 0)
                            seenExample = $"; e.g. a {c.Species.CommonName} {c.Position.DistanceTo(eye):F1} m off in {f.Id}";
                    }
                    if (pass == 1) { waited += pop.Deferred.Count; PoiFauna.PutDeferred(sim, pop, space, null); left += pop.Deferred.Count; }
                }
            }
        }
        Check(made > 100 && inside == 0, $"nothing starts inside a wall, floor, furniture or brick: {inside} of {made} in {places} places ({dropped} put down nowhere){example}");
        Check(fairMade > 30 && seen == 0 && left == 0, $"with you at the way in, nothing is put down in view, beside or behind you: {seen} of {fairMade}{seenExample}; {waited} waited until out of sight, none lost ({left})");
    }

    /// <summary>A place the growth took always has its Hollow beasts, in it and round it, however it is held and whatever it is (a camp too).</summary>
    void Hollows()
    {
        GD.Print("-- the growth's places --");
        var families = _lib.Families.Where(f => !WorldProfile.Unbuilt(f)).ToList();
        var camps = families.Where(f => f.Id.Contains("camp") || f.Tags.Contains("camp")).ToList();
        int places = 0, inside = 0, round = 0;
        string miss = "";
        for (int w = 0; w < 8; w++)
        {
            var given = new WorldRoll.Given();
            if (w % 2 == 0) given.Set(LayerKind.Flora, "hollow_overrun");
            WorldLife life = Life(out _, Archetypes[_rng.Next(Archetypes.Length)], given);
            var deep = new PlaceFauna.Deep(life, null, (float)_rng.NextDouble());
            for (int i = 0; i < 10; i++)
            {
                FamilyDef f = i < 2 && camps.Count > 0 ? camps[_rng.Next(camps.Count)] : families[_rng.Next(families.Count)];
                // Held by nobody, or forced held by another as well: the growth's are always there.
                OccupancyRoll occ = i % 3 == 1 ? new OccupancyRoll(Occupant.Scavengers) : i % 3 == 2 ? new OccupancyRoll(Occupant.Nesters) : null;
                PoiInstance inst = Place(f, PoiState.Infested, occ);
                PlaceMemory m = Memory(inst);
                var plan = new PoiPlan();
                PlaceFauna.HoldersOf(inst, life, plan);
                PoiPopulation pop = PlaceFauna.Plan(m, deep).Population;
                places++;
                int hollows = pop.Silent ? 0 : pop.Spawns.Count(s => s.Species.Corrupted && plan.Hollows.Any(h => s.Species.Id.StartsWith(h.Id, StringComparison.Ordinal)));
                if (hollows >= 2) inside++;
                else if (miss == "") miss = $"; e.g. {f.Id} held by {occ?.Main.ToString() ?? "the growth"}: {hollows} Hollows of {plan.Hollows.Count} kinds, {pop.Spawns.Count} spawns";
                var site = new PoiSite { Instance = inst, Family = f, Name = f.Id, Footprint = new TerrainDigging.Terrain.CellBox(new TerrainDigging.Terrain.Int3(-10, 0, -10), new TerrainDigging.Terrain.Int3(10, 0, 10)) };
                if (WildlifePatch.HauntsOf(new[] { site }, life).Any(h => h.Kinds.Count > 0 && h.Kinds.All(k => k.Corrupted) && h.Want >= 3)) round++;
            }
        }
        Check(inside == places, $"every place the growth took has Hollow beasts in it: {inside} of {places} (camps, and places others hold too){miss}");
        Check(round == places, $"and on the ground round it: {round} of {places} have a Hollows haunt");
    }

    /// <summary>Robots only where the world's old or current era was mid or high tech, and never of a low-tech make.</summary>
    void Robots_()
    {
        GD.Print("-- robots and the tech era --");
        int lowWorlds = 0, lowClean = 0, techWorlds = 0, clockwork = 0, techMachines = 0;
        var families = Held();
        string bad = "";
        for (int w = 0; w < 16; w++)
        {
            bool low = w % 2 == 0;
            var given = new WorldRoll.Given();
            if (w % 4 < 2) given.Set(LayerKind.Fall, "machine_uprising");
            WorldLife life = Life(out _, Archetypes[_rng.Next(Archetypes.Length)], given, low ? 0 : _rng.Next(0, 3), low ? 0 : 2);
            var deep = new PlaceFauna.Deep(life, null, 1f);
            var all = new List<Species>(life.Fauna.Species);
            all.AddRange(life.Machines);
            all.AddRange(life.Cyborgs);
            all.AddRange(life.Dark);
            all.AddRange(life.Signature.Machines);
            all.AddRange(life.Signature.Kinds.Select(k => k.Species));
            foreach (Occupant o in new[] { Occupant.Machines, Occupant.Robots, Occupant.Guardians, Occupant.Cyborgs })
            {
                PoiPopulation pop = PlaceFauna.Plan(Memory(Place(families[_rng.Next(families.Count)], PoiState.Abandoned, new OccupancyRoll(o))), deep).Population;
                if (!pop.Silent) all.AddRange(pop.Spawns.Select(s => s.Species));
            }
            int machines = all.Count(s => s.Mechanical);
            if (low)
            {
                lowWorlds++;
                if (machines == 0) lowClean++;
                else if (bad == "") bad = $"; e.g. {string.Join(", ", all.Where(s => s.Mechanical).Select(s => s.CommonName).Distinct())}";
            }
            else
            {
                techWorlds++;
                techMachines += machines;
                clockwork += all.Count(s => s.Mechanical && (s.CommonName ?? "").Contains("clockwork"));
            }
        }
        Check(lowClean == lowWorlds, $"low-tech worlds (old and current era low) have no robots anywhere, nor machine-held places: {lowClean} of {lowWorlds}{bad}");
        Check(techMachines > 0 && clockwork == 0, $"mid and high-tech worlds have their machines ({techMachines} across {techWorlds} worlds), none of a clockwork make ({clockwork})");
    }

    /// <summary>Every place holds what its rolled occupant says, over places rolled from fresh worlds' own mixes.</summary>
    void Guides()
    {
        GD.Print("-- the occupant guides --");
        var families = Held();
        var fails = new Dictionary<string, int>();
        var counts = new Dictionary<string, int>();
        string example = "";
        for (int w = 0; w < 12; w++)
        {
            WorldLife life = Life(out WorldRoll roll, Archetypes[_rng.Next(Archetypes.Length)]);
            if (roll.Occupancy.Count == 0) continue;
            var deep = new PlaceFauna.Deep(life, null, (float)_rng.NextDouble());
            for (int i = 0; i < 20; i++)
            {
                FamilyDef f = families[_rng.Next(families.Count)];
                OccupancyRoll occ = OccupancyLayer.Roll(roll.Occupancy, f, Seed());
                PoiInstance inst = Place(f, PoiState.Abandoned, occ);
                PoiPopulation pop = PlaceFauna.Plan(Memory(inst), deep).Population;
                var plan = new PoiPlan();
                PlaceFauna.HoldersOf(inst, life, plan);
                var spawns = pop.Silent ? new List<PoiSpawn>() : pop.Spawns;
                bool Under(PoiSpawn s) => inst.Layout.Modules.FirstOrDefault(m => m.Index == s.Room) is { Storey: < 0 };
                int people = inst.Townsfolk?.People.Count ?? 0;
                string why = plan.Holder switch
                {
                    PlaceHolder.Empty => spawns.Any(s => PoiFauna.Threat(s) && !Under(s)) || pop.Nests.Count > 0 ? "more than vermin above ground" : null,
                    PlaceHolder.Nesters => spawns.Count(s => s.Role is DeepRole.Nester or DeepRole.DerelictNester) < 1 ? "no nesters" : null,
                    PlaceHolder.Beasts => spawns.Count(s => plan.Holders.Any(h => s.Species.Id.StartsWith(h.Id, StringComparison.Ordinal))) < 1 ? "none of its beasts" : null,
                    PlaceHolder.Machines => spawns.Count(s => s.Species.Mechanical) < 1 ? "no machines" : spawns.Any(s => PoiFauna.Threat(s) && !s.Species.Mechanical) ? "wild hunters among the machines" : null,
                    PlaceHolder.Husks => spawns.Count(s => s.Species.Husk) + pop.Masses.Count < 1 ? "no Husks" : null,
                    PlaceHolder.People => people < 1 && inst.Townsfolk != null ? "no people" : spawns.Any(s => PoiFauna.Threat(s) && !Under(s) && !s.Species.Husk) ? "wild hunters among the people" : null,
                    _ => null,
                };
                string code = occ.Main.ToString();
                counts[code] = counts.GetValueOrDefault(code) + 1;
                if (why != null)
                {
                    fails[code] = fails.GetValueOrDefault(code) + 1;
                    if (example == "") example = $"; e.g. {f.Id} held by {occ}: {why}";
                }
            }
        }
        int total = counts.Values.Sum(), bad = fails.Values.Sum();
        GD.Print($"    {string.Join(", ", counts.OrderByDescending(kv => kv.Value).Select(kv => $"{kv.Key} {kv.Value - fails.GetValueOrDefault(kv.Key)}/{kv.Value}"))}");
        Check(total > 150 && bad == 0, $"every place holds what its occupant says: {total - bad} of {total} places rolled from the worlds' own mixes{example}");
    }

    /// <summary>People in the rooms of their jobs where the place has them, and a guard up on the lookout.</summary>
    void People()
    {
        GD.Print("-- people where they'd be --");
        var settled = _lib.Families.Where(f => f.Tags.Contains("settlement") || f.Tags.Contains("civilian")).ToList();
        Decor decor = Decor.Shared;
        int towns = 0, eligible = 0, atWork = 0, lookouts = 0, twoGuards = 0;
        for (int w = 0; w < 3; w++)
        {
            WorldLife life = Life(out _);
            foreach (FamilyDef f in settled.OrderBy(_ => _rng.Next()).Take(6))
            {
                PoiInstance inst = Place(f, PoiState.Active);
                PlaceMemory m = Memory(inst);
                m.Rooms = decor?.Plan(m);
                Town town = Town.For(m);
                if (town == null) continue;
                towns++;
                var roll = Wildlife.Rng.For(Seed(), "test");
                town.Rehearse(0f, 0.1f, _ => false, (p, i) =>
                {
                    PersonRole job = PeopleGenerator.RoleFor(p.Role, f.Tags.Contains("farm"), roll);
                    return (life.People.Person(Seed(), job, Occupancy.Friendly), job);
                }, (_, _) => { });
                town.Stations();
                foreach (Town.Person p in town.People)
                    if (p.Job is not (PersonRole.Guard or PersonRole.Child) && Enumerable.Range(0, town.Spots.Count).Any(i => town.ForJob(p, i)))
                    {
                        eligible++;
                        if (town.ForJob(p, p.Spot)) atWork++;
                    }
                if (town.People.Count(p => p.Job == PersonRole.Guard || p.Role == "guard") >= 2) twoGuards++;
                lookouts += town.Lookouts;
            }
        }
        GD.Print($"    {towns} towns, {twoGuards} with two guards or more, {lookouts} lookouts posted");
        Check(towns >= 6 && (eligible == 0 || atWork >= eligible * 0.8f), $"people start in the rooms of their jobs where the place has them: {atWork} of {eligible}");
    }
}
