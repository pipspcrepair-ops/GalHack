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
using PInt3 = PoiGenerator.Gen.Int3;
using WNav = Wildlife.NavGrid;
using WTerrain = Wildlife.Terrain;

namespace Slice.Tests;

/// <summary>
/// People, animals and crawlers get about the most cluttered rooms the game makes, round furniture and
/// clutter as it really stands. Over fresh worlds every run (nothing pinned), lived-in places, settlements,
/// ruins with their debris and dark infested ones are made and dressed as in play; the most crowded rooms
/// of them all are picked (parts per floor cell), and in each, walkers of several sizes (a person, small,
/// middling and big animals) are sent between points they can really stand at and really get between (a
/// fine flood fill of where their body fits), past monitors, lockers, chairs, tables and heaps; climbers are
/// sent along walls past what is mounted on them. Each walk is made twice, by the old whole-cell rule and by
/// the parts, and measured the same way against the real collision shapes:
///
/// - how many get there (one that gives up, stands jammed or goes through anything has failed), and how far they walk;
/// - how often a body is in a part (it must never be, under the parts rule), climbers' middles included;
/// - that small animals do use the gaps under tables and chairs.
///
/// Run: .\tools\test.ps1 -Project prototypes/slice -Scene res://tests/FurnitureTest.tscn
/// </summary>
public partial class FurnitureTest : Node
{
    const int WatchdogMinutes = 9;
    const float Dt = 1f / 30f;
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
            Kinds();
            Clutter();
        }
        catch (Exception e) { Check(false, $"the test threw: {e}"); }
        finally { PlaceParts.Legacy = false; }
        GD.Print(_failures == 0 ? $"ALL PASSED ({_passes} checks in {_clock.Elapsed.TotalSeconds:F1} s)" : $"{_failures} CHECK(S) FAILED ({_passes} passed)");
        Shutdown.Quit(this, _failures == 0 ? 0 : 1);
    }

    void Check(bool ok, string what)
    {
        if (ok) _passes++;
        else _failures++;
        GD.Print($"  {(ok ? "ok   " : "FAIL ")} {what}");
    }

    ulong Seed() => (ulong)_rng.NextInt64();

    /// <summary>What each furniture and fitting block is as a walker meets it, from its real collision shape.</summary>
    void Kinds()
    {
        GD.Print("-- furniture blocks by their real shapes --");
        var by = new SortedDictionary<string, List<string>>();
        foreach (BlockType t in Enum.GetValues<BlockType>())
        {
            if (!BlockInfo.IsPart(t)) continue;
            var boxes = Shapes.CollisionBoxes(new Block(t, PoiGenerator.Gen.Material.Steel, default, Role.Prop), new PInt3(0, 0, 0))
                .Select(b => (new Vector3(b.lo.X, b.lo.Y, b.lo.Z), new Vector3(b.hi.X, b.hi.Y, b.hi.Z)));
            string kind = PartSet.KindOf(boxes);
            if (!by.TryGetValue(kind, out var l)) by[kind] = l = new List<string>();
            l.Add(t.ToString());
        }
        foreach (var (k, l) in by) GD.Print($"    {k}: {string.Join(", ", l)}");
        // A table's real shape leaves the floor between its legs open: something small goes under it.
        var table = new PartSet();
        foreach (var (lo, hi) in Shapes.CollisionBoxes(new Block(BlockType.Table, PoiGenerator.Gen.Material.Timber, default, Role.Prop), new PInt3(0, 0, 0)))
            table.AddBox(new Vector3(lo.X, lo.Y, lo.Z), new Vector3(hi.X, hi.Y, hi.Z), 1);
        Check(table.Fits(new Vector3(0.5f, 0f, 0.5f), 0.15f, 0.4f) && !table.Fits(new Vector3(0.5f, 0f, 0.5f), 0.3f, 1.7f) && !table.Fits(new Vector3(0.14f, 0f, 0.14f), 0.1f, 0.3f),
            "a table block collides as its top and four legs: a small body fits under it, a person doesn't, nobody stands in a leg");
    }

    /// <summary>A room picked for its clutter: its place, its walk level and floor, and its floor cells.</summary>
    sealed class Room
    {
        public PlaceMemory Memory;
        public string Name;
        public int Module, Y;
        public float Floor;
        public List<PInt3> Cells;
        public int Parts;
        public float Density;
        public WorldLife Life;
    }

    static readonly Occupant[] Dwellers = { Occupant.Squatters, Occupant.Scavengers, Occupant.Cult, Occupant.Keepers };

    List<Room> Rooms(Decor decor, out int made)
    {
        var all = new List<Room>();
        made = 0;
        var held = _lib.Families.Where(f => OccupancyLayer.Applies(f, PoiState.Abandoned)).ToList();
        var settled = _lib.Families.Where(f => f.Tags.Contains("settlement")).ToList();
        var any = _lib.Families.ToList();
        for (int w = 0; w < 3; w++)
        {
            ulong seed = Seed();
            WorldProfile profile = WorldProfile.Roll(seed, "Temperate", false);
            if (!profile.Inhabited) profile = WorldProfile.Roll(seed ^ 0x5EED, "Temperate", true);
            WorldRoll roll = WorldRoll.Roll(_lib, seed, "Temperate", profile, false, null, (float)_rng.NextDouble());
            WorldReading r = WorldReading.Of(roll);
            WorldLife life = Bestiary.Roll(_data, new WorldConditions { Seed = seed, Alienness = 0.25f, Coreward = Math.Max(0f, roll.Depth), Bloom = r.HollowWorld, Stock = r.Stock, Working = r.Working, Ferals = r.Ferals, Predators = r.Predators, Density = r.Density, Mutagen = r.Mutagen });
            // Lived-in homes and workshops, settlements as they stand, ruins with their debris, and the dark of infested places.
            var picks = held.OrderBy(_ => _rng.Next()).Take(6).Select(f => (f, PoiState.Abandoned))
                .Concat(settled.OrderBy(_ => _rng.Next()).Take(3).Select(f => (f, PoiState.Active)))
                .Concat(any.OrderBy(_ => _rng.Next()).Take(4).Select(f => (f, PoiState.Ruined)))
                .Concat(any.OrderBy(_ => _rng.Next()).Take(3).Select(f => (f, PoiState.Infested)));
            foreach (var (f, state) in picks)
            {
                PoiInstance inst;
                try
                {
                    Layout layout = PoiFactory.BuildLayout(_lib, f, 0, f.Rarity, Seed());
                    inst = PoiFactory.Spawn(_lib, layout, state, Seed(), null, state == PoiState.Abandoned ? new OccupancyRoll(Dwellers[_rng.Next(Dwellers.Length)]) : null);
                }
                catch (Exception) { continue; }
                var memory = new PlaceMemory(new PoiSite { Instance = inst, Family = inst.Family, Name = inst.Family.Id });
                memory.Rooms = decor.Plan(memory);
                PartSet parts = PlaceParts.Of(memory);
                made++;
                foreach (PlacedModule m in inst.Layout.Modules)
                {
                    if (m.Kind is not (ModuleKind.Room or ModuleKind.Hall or ModuleKind.Vault)) continue;
                    if (Decor.Interior(inst, m) is not { } i) continue;
                    var cells = m.Cells.Where(c => c.Y == i.Y).ToList();
                    if (cells.Count < 9) continue;
                    var set = cells.ToHashSet();
                    var owners = new HashSet<long>();
                    foreach (PartSet.Box b in parts.All)
                    {
                        var c = new PInt3((int)MathF.Floor(b.Centre.X), i.Y, (int)MathF.Floor(b.Centre.Z));
                        if (set.Contains(c) && b.Centre.Y < i.Y + 2.2f && b.Centre.Y > i.Y - 0.2f) owners.Add(b.Owner);
                    }
                    if (owners.Count < 5) continue;
                    all.Add(new Room { Memory = memory, Name = $"{f.Id} ({state}) module {m.Index} {m.Purpose}", Module = m.Index, Y = i.Y, Floor = i.Floor, Cells = cells, Parts = owners.Count, Density = owners.Count / (float)cells.Count, Life = life });
                }
            }
        }
        // The most crowded of all, at most two from a place.
        return all.OrderByDescending(r => r.Density).GroupBy(r => r.Memory).SelectMany(g => g.Take(2)).OrderByDescending(r => r.Density).Take(10).ToList();
    }

    /// <summary>A body as it is tested: its radius and height against the parts, and how far it is held off walls.</summary>
    readonly record struct Body(string Name, float Radius, float Height, float WallRadius, Species Species, bool Person);

    /// <summary>
    /// Points on the room's floor where a body really fits (clear of the real shapes and the walls), on a 12.5 cm
    /// lattice, and which of them join up (a step to the next fits as well): the truth any rule is measured against.
    /// </summary>
    static (List<Vector3> Points, int[] Group) Truth(Room room, PartSet real, Func<int, int, int, bool> walls, Body b)
    {
        const float S = 0.125f;
        var pts = new List<Vector3>();
        var at = new Dictionary<(int, int), int>();
        float y = room.Y + room.Floor;
        foreach (PInt3 c in room.Cells)
            for (int i = 0; i < 8; i++)
                for (int j = 0; j < 8; j++)
                {
                    var p = new Vector3(c.X + S * (i + 0.5f), y, c.Z + S * (j + 0.5f));
                    if (walls(c.X, c.Y - 1, c.Z) is false || walls(c.X, c.Y, c.Z)) continue;
                    if (!PartSet.WallClear(p, b.Height, walls, b.WallRadius) || real.Gap(p, b.Radius, b.Height) < 0.01f) continue;
                    at[((int)MathF.Floor(p.X / S), (int)MathF.Floor(p.Z / S))] = pts.Count;
                    pts.Add(p);
                }
        var group = Enumerable.Repeat(-1, pts.Count).ToArray();
        int g = 0;
        for (int s = 0; s < pts.Count; s++)
        {
            if (group[s] >= 0) continue;
            var q = new Queue<int>();
            q.Enqueue(s);
            group[s] = g;
            while (q.Count > 0)
            {
                int k = q.Dequeue();
                var key = ((int)MathF.Floor(pts[k].X / S), (int)MathF.Floor(pts[k].Z / S));
                foreach (var (dx, dz) in new[] { (1, 0), (-1, 0), (0, 1), (0, -1) })
                    if (at.TryGetValue((key.Item1 + dx, key.Item2 + dz), out int n) && group[n] < 0) { group[n] = g; q.Enqueue(n); }
            }
            g++;
        }
        return (pts, group);
    }

    /// <summary>Pairs of points a body can really get between, a few metres apart, with furniture across the straight way where it can be found.</summary>
    List<(Vector3 From, Vector3 To)> Pairs(List<Vector3> pts, int[] group, PartSet real, Body b, int want)
    {
        var pairs = new List<(Vector3, Vector3)>();
        if (pts.Count < 2) return pairs;
        var sizes = group.GroupBy(x => x).ToDictionary(x => x.Key, x => x.Count());
        int main = sizes.OrderByDescending(kv => kv.Value).First().Key;
        var mine = Enumerable.Range(0, pts.Count).Where(i => group[i] == main).ToList();
        for (int tries = 0; tries < 400 && pairs.Count < want; tries++)
        {
            Vector3 a = pts[mine[_rng.Next(mine.Count)]], c = pts[mine[_rng.Next(mine.Count)]];
            if (new Vector2(a.X - c.X, a.Z - c.Z).Length() < 2.5f) continue;
            bool past = !real.Sweep(a, c, b.Radius, b.Height);
            if (!past && tries < 300) continue;
            pairs.Add((a, c));
        }
        return pairs;
    }

    /// <summary>How the walks of one sort of body went by one rule.</summary>
    sealed class Tally
    {
        public int Tried, Reached, Failed, Jammed, NoWay, Through, OverlapSamples, Under, Samples;
        public float Length;
        public float Worst;
        public readonly List<string> Notes = new();
        public string Rate => Tried == 0 ? "none" : $"{Reached}/{Tried} ({100f * Reached / Tried:F0}%)";
        public string Mean => Reached == 0 ? "-" : $"{Length / Reached:F1} m";
    }

    void Clutter()
    {
        GD.Print("-- the most cluttered rooms --");
        Decor decor = Decor.Shared;
        Check(decor != null, "the dressing library loads");
        List<Room> rooms = Rooms(decor, out int made);
        foreach (Room r in rooms) GD.Print($"    {r.Name}: {r.Parts} pieces over {r.Cells.Count} floor cells ({r.Density:F2} a cell)");
        Check(rooms.Count >= 6, $"{rooms.Count} crowded rooms picked from {made} places on 3 fresh worlds");
        var fauna = new DeepFauna(_data, (ulong)_rng.NextInt64(), 0.5f);
        var walkers = new List<Species>();
        var climbers = new List<Species>();
        foreach (PoiTheme theme in Enum.GetValues<PoiTheme>())
            for (int depth = 0; depth < 4; depth++)
                foreach (DeepRole role in Enum.GetValues<DeepRole>())
                {
                    Species sp;
                    try { sp = fauna.For(role, theme, depth); }
                    catch (Exception) { continue; }
                    if (sp == null || sp.Flies || sp.Rig == RigKind.Swarm) continue;
                    if (sp.Climbs) { if (climbers.All(w => w.CommonName != sp.CommonName)) climbers.Add(sp); }
                    else if ((sp = fauna.Indoor(sp)) != null && sp.BodyWidth < 1.6f && walkers.All(w => w.CommonName != sp.CommonName)) walkers.Add(sp);
                }
        // And the worlds' own animals, the small among them (the ones that go under a table).
        foreach (Room r in rooms)
            foreach (Species sp in r.Life.Fauna.Species.Concat(Enumerable.Range(0, 6).Select(n => r.Life.Domestic(DomesticKind.Pet, n))))
                if (!sp.Flies && !sp.Climbs && sp.Rig != RigKind.Swarm && sp.BodyWidth < 1.6f && sp.BodyHeight <= DeepFauna.IndoorMax && walkers.All(w => w.CommonName != sp.CommonName)) walkers.Add(sp);
        walkers = walkers.OrderBy(s => s.BodyHeight).ToList();
        var pickW = new List<Species>();
        if (walkers.Count > 0)
        {
            pickW.Add(walkers[0]);
            if (walkers.Count > 2) pickW.Add(walkers[walkers.Count / 2]);
            if (walkers.Count > 1) pickW.Add(walkers[^1]);
        }
        var bodies = new List<Body>();
        Species person = rooms.Count > 0 ? rooms[0].Life.People.Person(Wildlife.Rng.Mix(1, (ulong)_rng.Next()), PersonRole.Worker, Occupancy.Friendly) : null;
        if (person != null) bodies.Add(new Body("a person", PlaceParts.PersonRadius, PlaceParts.PersonHeight, PlaceParts.PersonRadius, person, true));
        foreach (Species s in pickW) bodies.Add(new Body($"{s.CommonName} ({s.BodyHeight:F2} m tall, r {SpaceNav.PartRadius(s):F2})", SpaceNav.PartRadius(s), SpaceNav.PartHeight(s), Crawl.ClearanceFor(s), s, false));
        Check(bodies.Count >= 3 && pickW.Count >= 2, $"walkers: {string.Join("; ", bodies.Select(b => b.Name))}");

        var before = bodies.ToDictionary(b => b.Name, _ => new Tally());
        var after = bodies.ToDictionary(b => b.Name, _ => new Tally());
        var climbBefore = new Tally();
        var climbAfter = new Tally();
        foreach (Room room in rooms)
        {
            if (_clock.Elapsed.TotalMinutes > 7) break;
            PartSet real = PlaceParts.Of(room.Memory);
            PoiInstance inst = room.Memory.Inst;
            Func<int, int, int, bool> peopleWalls = Place.WallsOf(Place.FootNavOf(room.Memory));
            Func<int, int, int, bool> creatureWalls = (x, y, z) => PlaceFauna.Solid(inst, x, y, z);
            foreach (Body b in bodies)
            {
                var (pts, group) = Truth(room, real, b.Person ? peopleWalls : creatureWalls, b);
                var pairs = Pairs(pts, group, real, b, b.Person ? 6 : 4);
                foreach (var (from, to) in pairs)
                    foreach (bool legacy in new[] { true, false })
                    {
                        PlaceParts.Legacy = legacy;
                        Tally t = legacy ? before[b.Name] : after[b.Name];
                        if (b.Person) WalkPerson(room, real, b, from, to, t);
                        else WalkAnimal(room, real, b, from, to, t);
                        PlaceParts.Legacy = false;
                    }
            }
            if (climbers.Count > 0)
                foreach (var (from, to, mount, outward) in WallRuns(room, real).Take(3))
                {
                    Species sp = climbers[_rng.Next(climbers.Count)];
                    foreach (bool legacy in new[] { true, false })
                    {
                        PlaceParts.Legacy = legacy;
                        Climb(room, real, sp, from, to, mount, outward, legacy ? climbBefore : climbAfter);
                        PlaceParts.Legacy = false;
                    }
                }
        }
        GD.Print("-- before (whole cells) -> after (real shapes) --");
        foreach (Body b in bodies)
        {
            Tally o = before[b.Name], n = after[b.Name];
            GD.Print($"    {b.Name}: got there {o.Rate} -> {n.Rate}; mean walk {o.Mean} -> {n.Mean}; no way found {o.NoWay} -> {n.NoWay}, jammed {o.Jammed} -> {n.Jammed}, went through furniture {o.Through} -> {n.Through} walks ({o.OverlapSamples} -> {n.OverlapSamples} of {o.Samples} / {n.Samples} steps, deepest {o.Worst:F2} -> {n.Worst:F2} m){(b.Person ? "" : $", under a table or chair {o.Under} -> {n.Under} walks")}");
            foreach (string s in n.Notes.Take(3)) GD.Print($"      after: {s}");
        }
        GD.Print($"    climbers along walls past mounted pieces: got there {climbBefore.Rate} -> {climbAfter.Rate}; middle inside a piece {climbBefore.Through} -> {climbAfter.Through} runs ({climbBefore.OverlapSamples} -> {climbAfter.OverlapSamples} of {climbBefore.Samples} / {climbAfter.Samples} steps); went over or round it {climbBefore.Under} -> {climbAfter.Under}");
        foreach (string s in climbAfter.Notes.Take(3)) GD.Print($"      after: {s}");

        int tried = after.Values.Sum(t => t.Tried), reached = after.Values.Sum(t => t.Reached), overlaps = after.Values.Sum(t => t.OverlapSamples) + climbAfter.OverlapSamples;
        int triedBefore = before.Values.Sum(t => t.Tried), reachedBefore = before.Values.Sum(t => t.Reached);
        Tally people = bodies.Where(b => b.Person).Select(b => after[b.Name]).FirstOrDefault() ?? new Tally();
        Tally peopleBefore = bodies.Where(b => b.Person).Select(b => before[b.Name]).FirstOrDefault() ?? new Tally();
        Check(tried >= 60, $"{tried} walks between points really reachable, in {rooms.Count} rooms");
        Check(overlaps == 0, $"no body is ever in a piece of furniture or clutter (its real shape): {overlaps} steps in one (before: {before.Values.Sum(t => t.OverlapSamples) + climbBefore.OverlapSamples})");
        Check(people.Tried > 0 && people.Reached >= people.Tried * 0.9f && people.Reached >= peopleBefore.Reached,
            $"people get past monitors, lockers, chairs and tables: {peopleBefore.Rate} -> {people.Rate}");
        Check(reached >= tried * 0.85f && reached > reachedBefore,
            $"walkers of every size get there: {reachedBefore}/{triedBefore} -> {reached}/{tried}");
        Check(after.Values.Sum(t => t.Jammed) <= Math.Max(1, tried / 40), $"few stand jammed: {before.Values.Sum(t => t.Jammed)} -> {after.Values.Sum(t => t.Jammed)}");
        Check(after.Values.Sum(t => t.Under) > 0, $"small animals use the gaps under tables and chairs: {after.Values.Sum(t => t.Under)} walks went under one");
        Check(climbAfter.Tried >= 3 && climbAfter.Through == 0 && climbAfter.Reached >= climbAfter.Tried * 0.7f,
            $"crawlers climb walls past what is mounted on them, never inside one: {climbAfter.Reached} of {climbAfter.Tried} got along, {climbAfter.Through} were inside (before: {climbBefore.Through} of {climbBefore.Tried})");
    }

    /// <summary>One person walked from one point to another by the town's walking grid and legs.</summary>
    void WalkPerson(Room room, PartSet real, Body b, Vector3 from, Vector3 to, Tally t)
    {
        t.Tried++;
        Town town = Town.Walking(room.Memory);
        Town.Person p = town.Walker(b.Species, from, to);
        if (p == null)
        {
            t.NoWay++;
            t.Failed++;
            return;
        }
        float r = Town.RadiusOf(p), h = Town.HeightOf(p);
        float walked = 0f, best = float.MaxValue, since = 0f;
        bool through = false, done = false;
        for (float time = 0f; time < 45f && !done; time += Dt)
        {
            Vector3 was = p.Pos;
            done = town.StepWalker(p, Dt);
            walked += new Vector2(p.Pos.X - was.X, p.Pos.Z - was.Z).Length();
            t.Samples++;
            float gap = real.Gap(p.Pos, r, h);
            if (gap < -0.01f)
            {
                t.OverlapSamples++;
                t.Worst = MathF.Max(t.Worst, -gap);
                if (!through && !PlaceParts.Legacy && t.Notes.Count < 3) t.Notes.Add($"{room.Name}: a person {(-gap):F2} m into a piece at {p.Pos.X:F2},{p.Pos.Y:F2},{p.Pos.Z:F2}");
                through = true;
            }
            float d = new Vector2(p.Pos.X - to.X, p.Pos.Z - to.Z).Length();
            if (d < best - 0.05f) { best = d; since = 0f; }
            else if ((since += Dt) > 4f) break;
        }
        bool there = new Vector2(p.Pos.X - to.X, p.Pos.Z - to.Z).Length() < 0.35f;
        Score(t, there, through, walked, room, "a person", p.Pos);
    }

    void Score(Tally t, bool there, bool through, float walked, Room room, string who, Vector3 at)
    {
        if (through) t.Through++;
        if (there && !through)
        {
            t.Reached++;
            t.Length += walked;
            return;
        }
        t.Failed++;
        if (!there) t.Jammed++;
        if (!there && !PlaceParts.Legacy && t.Notes.Count < 3) t.Notes.Add($"{room.Name}: {who} stopped at {at.X:F2},{at.Y:F2},{at.Z:F2}");
    }

    /// <summary>One animal walked there as a place's creature chases: its path through the place, then its legs.</summary>
    void WalkAnimal(Room room, PartSet real, Body b, Vector3 from, Vector3 to, Tally t)
    {
        t.Tried++;
        BlockSpace space = PlaceFauna.SpaceOf(room.Memory);
        var sim = new WildSim(_data, new WTerrain((x, z) => -80f, 400f), new WNav(400f, 3f));
        Creature c = sim.SpawnIn(b.Species, space, from + Vector3.Up * 0.05f);
        if (c == null)
        {
            t.NoWay++;
            t.Failed++;
            return;
        }
        float walked = 0f, best = float.MaxValue, since = 0f;
        bool through = false, under = false, there = false;
        for (float time = 0f; time < 45f; time += Dt)
        {
            Vector3 was = c.Position;
            sim.RefillPathBudget();
            Brain.Toward(sim, c, to + Vector3.Up * 0.3f, MathF.Max(1f, c.Stats.WalkSpeed));
            Locomotion.Move(sim, c, Dt);
            sim.Time += Dt;
            walked += new Vector2(c.Position.X - was.X, c.Position.Z - was.Z).Length();
            if (c.Falling) continue;
            t.Samples++;
            float gap = real.Gap(c.Position, b.Radius, b.Height);
            if (gap < -0.01f)
            {
                t.OverlapSamples++;
                t.Worst = MathF.Max(t.Worst, -gap);
                if (!through && !PlaceParts.Legacy && t.Notes.Count < 3) t.Notes.Add($"{room.Name}: {b.Species.CommonName} {(-gap):F2} m into a piece at {c.Position.X:F2},{c.Position.Y:F2},{c.Position.Z:F2} at {time:F1} s ({Worst(real, c.Position, b)}), from {from}");
                through = true;
            }
            else if (b.Height < 0.65f && real.Gap(c.Position, b.Radius, 1.9f) < 0f) under = true;
            float d = new Vector2(c.Position.X - to.X, c.Position.Z - to.Z).Length();
            if (d < MathF.Max(0.4f, b.Radius + 0.15f) && MathF.Abs(c.Position.Y - to.Y) < 0.5f) { there = true; break; }
            if (d < best - 0.05f) { best = d; since = 0f; }
            else if ((since += Dt) > 5f) break;
        }
        if (under && !through) t.Under++;
        if (!there && !PlaceParts.Legacy && c.SpacePath.Count <= 1 && t.Notes.Count < 3)
        {
            // Why there was no way: the cells round where it stood, as its paths see them.
            SpaceNav.Body body = SpaceNav.BodyOf(b.Species);
            Vector3I at = SpaceMath.Cell(c.Position + Vector3.Up * 0.3f);
            var cells = new List<string>();
            for (int dz = -1; dz <= 1; dz++)
                for (int dx = -1; dx <= 1; dx++)
                {
                    var n = new Vector3I(at.X + dx, at.Y, at.Z + dz);
                    bool w = SpaceNav.Walkable(space, n, body);
                    cells.Add($"{dx},{dz}:{(space.SolidCell(n.X, n.Y, n.Z) ? "S" : w ? "w" : "-")}{(w && (dx != 0 || dz != 0) ? (SpaceNav.Steps(space, at, n, body) ? ">" : "x") : "")}");
                }
            t.Notes.Add($"{room.Name}: stuck at cell {at} (body r {body.Radius:F2} h {body.Height:F2} walls {body.WallRadius:F2}): {string.Join(" ", cells)}");
        }
        Score(t, there, through, walked, room, $"{b.Species.CommonName} (from {from.X:F2},{from.Y:F2},{from.Z:F2} to {to.X:F2},{to.Z:F2}; way {c.SpacePath.Count} points, at {c.SpacePathIndex}{(c.SpacePath.Count > 0 ? $", next {c.SpacePath[Math.Min(c.SpacePathIndex, c.SpacePath.Count - 1)]}" : "")})", c.Position);
    }

    static string Worst(PartSet real, Vector3 feet, Body b)
    {
        PartSet.Box worst = default;
        float g = float.MaxValue;
        foreach (PartSet.Box x in real.All)
        {
            var one = new PartSet();
            one.Add(x.Centre, x.X, x.Y, x.Z, x.Half, 1);
            float d = one.Gap(feet, b.Radius, b.Height);
            if (d < g) { g = d; worst = x; }
        }
        return $"box at {worst.Centre} half {worst.Half} up {worst.Y}, owner {worst.Owner}";
    }

    /// <summary>Runs along a room's walls past a piece mounted on them: from a metre to one side of it to a metre to the other, at its height.</summary>
    IEnumerable<(Vector3 From, Vector3 To, PartSet.Box Mount, Vector3 Out)> WallRuns(Room room, PartSet real)
    {
        PoiInstance inst = room.Memory.Inst;
        var set = room.Cells.ToHashSet();
        foreach (PartSet.Box b in real.All.OrderBy(_ => _rng.Next()))
        {
            float floor = room.Y + room.Floor;
            if (b.Lo.Y < floor + 0.5f || b.Hi.Y > floor + 2.4f) continue;
            if (!set.Contains(new PInt3((int)MathF.Floor(b.Centre.X), room.Y, (int)MathF.Floor(b.Centre.Z)))) continue;
            foreach (var n in new[] { Vector3.Right, Vector3.Left, Vector3.Back, Vector3.Forward })
            {
                // The wall behind it, within a hand of its back.
                Vector3 back = b.Centre + n * (MathF.Abs(n.X) > 0 ? (b.Hi.X - b.Lo.X) * 0.5f + 0.15f : (b.Hi.Z - b.Lo.Z) * 0.5f + 0.15f);
                if (!PlaceFauna.Solid(inst, (int)MathF.Floor(back.X), (int)MathF.Floor(back.Y), (int)MathF.Floor(back.Z))) continue;
                Vector3 side = new(-n.Z, 0f, n.X);
                float face = MathF.Abs(n.X) > 0 ? (n.X > 0 ? MathF.Floor(back.X) : MathF.Floor(back.X) + 1f) : (n.Z > 0 ? MathF.Floor(back.Z) : MathF.Floor(back.Z) + 1f);
                Vector3 onWall = MathF.Abs(n.X) > 0 ? new Vector3(face, b.Centre.Y, b.Centre.Z) : new Vector3(b.Centre.X, b.Centre.Y, face);
                float reach = (MathF.Abs(side.X) > 0 ? b.Hi.X - b.Lo.X : b.Hi.Z - b.Lo.Z) * 0.5f + 0.8f;
                Vector3 a = onWall + side * reach - n * 0.05f, c = onWall - side * reach - n * 0.05f;
                bool ok = true;
                foreach (Vector3 q in new[] { a, c })
                {
                    Vector3 behind = q + n * 0.3f;
                    if (!PlaceFauna.Solid(inst, (int)MathF.Floor(behind.X), (int)MathF.Floor(behind.Y), (int)MathF.Floor(behind.Z)) || PlaceFauna.Solid(inst, (int)MathF.Floor(q.X - n.X * 0.2f), (int)MathF.Floor(q.Y), (int)MathF.Floor(q.Z - n.Z * 0.2f))) ok = false;
                }
                if (ok) { yield return (a, c, b, -n); break; }
            }
        }
    }

    void Climb(Room room, PartSet real, Species sp, Vector3 from, Vector3 to, PartSet.Box mount, Vector3 outward, Tally t)
    {
        t.Tried++;
        BlockSpace space = PlaceFauna.SpaceOf(room.Memory);
        var sim = new WildSim(_data, new WTerrain((x, z) => -80f, 400f), new WNav(400f, 3f));
        Creature c = sim.SpawnIn(sp, space, from, outward);
        if (c == null) { t.NoWay++; t.Failed++; return; }
        bool inside = false, passed = false, there = false;
        float best = float.MaxValue, since = 0f;
        var trail = new List<string>();
        int tick = 0;
        for (float time = 0f; time < 30f; time += Dt)
        {
            if (tick++ % 45 == 0) { Vector3 q = Crawl.Center(c); trail.Add($"{q.X:F2},{q.Y:F2},{q.Z:F2}{(c.Falling ? " falling" : "")} n{c.Facing.Y.X:F1},{c.Facing.Y.Y:F1},{c.Facing.Y.Z:F1} way {c.SpacePath.Count}/{c.SpacePathIndex}"); }
            sim.RefillPathBudget();
            Brain.Toward(sim, c, to, MathF.Max(0.8f, c.Stats.WalkSpeed));
            Locomotion.Move(sim, c, Dt);
            sim.Time += Dt;
            if (c.Falling) continue;
            t.Samples++;
            Vector3 mid = Crawl.Center(c);
            if (real.Distance(mid) < -0.01f)
            {
                t.OverlapSamples++;
                if (!inside && !PlaceParts.Legacy && t.Notes.Count < 3) t.Notes.Add($"{room.Name}: {sp.CommonName}'s middle in a piece at {mid.X:F2},{mid.Y:F2},{mid.Z:F2}");
                inside = true;
            }
            if (mount.Distance(mid) < 0.7f) passed = true;
            float d = mid.DistanceTo(to);
            if (d < 0.7f) { there = true; break; }
            if (d < best - 0.05f) { best = d; since = 0f; }
            else if ((since += Dt) > 6f) break;
        }
        if (passed) t.Under++;
        if (inside) t.Through++;
        if (there && !inside) t.Reached++;
        else
        {
            t.Failed++;
            if (!there && !PlaceParts.Legacy && t.Notes.Count < 3) t.Notes.Add($"{room.Name}: {sp.CommonName} (clearance {c.Clearance:F2}) stopped at {Crawl.Center(c)} heading for {to} from {from}, past a piece at {mount.Centre} half {mount.Half}: {string.Join(" | ", trail)}");
        }
    }
}
