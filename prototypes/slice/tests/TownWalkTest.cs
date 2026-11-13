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

namespace Slice.Tests;

/// <summary>
/// People walk where there is floor, and use the rooms. Over fresh worlds every run (nothing
/// pinned), lived-in places (a coal power station's hall among them, held by squatters,
/// scavengers, cultists or keepers, and settlements as they stand) are dressed as in play and
/// their people walked through a day, a night and a morning: every person's feet stay within
/// 0.3 m of a floor, a stair or a ramp; no route crosses a piece of furniture or dressing, and no one's body is ever in one (its real shape); going
/// from one part of a place to another is done through a doorway; people go into rooms and come
/// out again, sleep in beds, sit on seats and stand at benches; and nobody is stuck on their way
/// for over 5 s.
///
/// Run: .\tools\test.ps1 -Project prototypes/slice -Scene res://tests/TownWalkTest.tscn
/// </summary>
public partial class TownWalkTest : Node
{
    const int WatchdogMinutes = 9;
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
            Walks();
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

    ulong Seed() => (ulong)_rng.NextInt64();

    WorldRoll Roll(ulong seed, string arch)
    {
        WorldProfile profile = WorldProfile.Roll(seed, arch, false);
        if (!profile.Inhabited) profile = WorldProfile.Roll(seed ^ 0x5EED, arch, true);
        return WorldRoll.Roll(_lib, seed, arch, profile, false, null, (float)_rng.NextDouble());
    }

    static WorldConditions Conditions(WorldRoll roll, ulong seed)
    {
        WorldReading r = WorldReading.Of(roll);
        return new WorldConditions { Seed = seed, Alienness = 0.25f, Coreward = Math.Max(0f, roll.Depth), Bloom = r.HollowWorld, Stock = r.Stock, Working = r.Working, Ferals = r.Ferals, Predators = r.Predators, Density = r.Density, Mutagen = r.Mutagen };
    }

    static readonly Occupant[] Dwellers = { Occupant.Squatters, Occupant.Scavengers, Occupant.Cult, Occupant.Keepers };

    void Walks()
    {
        GD.Print("-- people in lived-in places --");
        Decor decor = Decor.Shared;
        Check(decor != null, "the dressing library loads");
        var held = _lib.Families.Where(f => OccupancyLayer.Applies(f, PoiState.Abandoned)).ToList();
        var settled = _lib.Families.Where(f => f.Tags.Contains("settlement")).ToList();
        int places = 0, people = 0, samples = 0, sunk = 0, routes = 0, crossing = 0, changes = 0, doorless = 0, ins = 0, outs = 0, slept = 0, sat = 0, worked = 0, stuck = 0, beds = 0, seats = 0;
        float worst = 0f, longest = 0f, deepest = 0f;
        int inParts = 0;
        var inAt = new List<string>();
        var sunkAt = new List<string>();
        var crossAt = new List<string>();
        var doorAt = new List<string>();
        var stuckAt = new List<string>();
        for (int w = 0; w < 5 && _clock.Elapsed.TotalMinutes < 6; w++)
        {
            ulong seed = Seed();
            WorldRoll roll = Roll(seed, "Temperate");
            WorldLife life = Bestiary.Roll(_data, Conditions(roll, seed));
            var families = held.Where(f => f.Id == "coal_plant").Concat(held.OrderBy(_ => _rng.Next()).Take(5)).Select(f => (f, false))
                .Concat(settled.OrderBy(_ => _rng.Next()).Take(2).Select(f => (f, true))).ToList();
            foreach (var (f, settlement) in families)
            {
                PoiInstance inst;
                try
                {
                    Layout layout = PoiFactory.BuildLayout(_lib, f, 0, f.Rarity, Seed());
                    inst = settlement ? PoiFactory.Spawn(_lib, layout, PoiState.Active, Seed(), null, null)
                        : PoiFactory.Spawn(_lib, layout, PoiState.Abandoned, Seed(), null, new OccupancyRoll(Dwellers[_rng.Next(Dwellers.Length)]));
                }
                catch (Exception) { continue; }
                var memory = new PlaceMemory(new PoiSite { Instance = inst, Family = inst.Family, Name = inst.Family.Id });
                memory.Rooms = decor.Plan(memory);
                Town town = Town.For(memory);
                if (town == null) continue;
                places++;
                beds += town.Spots.Count(s => s.Tag == "bed");
                seats += town.Spots.Count(s => s.Tag == "sit");
                // Which part of the place each cell is, and the doorways between them.
                var moduleOf = new Dictionary<PInt3, int>();
                var partsOf = new Dictionary<PInt3, HashSet<int>>();
                foreach (PlacedModule m in inst.Layout.Modules)
                    foreach (PInt3 c in m.Cells)
                    {
                        moduleOf.TryAdd(c, m.Index);
                        (partsOf.TryGetValue(c, out var s) ? s : partsOf[c] = new HashSet<int>()).Add(m.Index);
                    }
                var doors = new HashSet<PInt3>(inst.Layout.Doorways.SelectMany(d => d.Cells).Concat(inst.Layout.ExitCells));
                foreach (var (a, b) in Decor.Openings(inst)) { doors.Add(a); doors.Add(b); }
                var rooms = new HashSet<int>(inst.Layout.Modules.Where(m => m.Kind == ModuleKind.Room).Select(m => m.Index));
                var last = new Dictionary<Town.Person, (int Module, float Since, Vector3 At, float Still, bool InRoom, bool WasIn)>();
                var used = new HashSet<Town.Person>();
                var roles = Wildlife.Rng.For(inst.SpawnSeed, "walk-test");
                town.Rehearse(300f, 0.1f, t => t >= 110f && t < 220f,
                    (p, i) =>
                    {
                        PersonRole job = PeopleGenerator.RoleFor(p.Role, f.Tags.Contains("farm"), roles);
                        return (life.People.Person(Wildlife.Rng.Mix(inst.SpawnSeed, (ulong)i + 1), job, Occupancy.Friendly), job);
                    },
                    (p, t) =>
                    {
                        if (p.Indoors || p.Asleep)
                        {
                            if (p.Asleep && used.Add(p)) slept++;
                            return;
                        }
                        samples++;
                        // Never in the furniture or the clutter (its real shape), but the seat or bed in use.
                        float into = town.InParts(p);
                        if (into > 0.01f) { inParts++; deepest = MathF.Max(deepest, into); if (inAt.Count < 3) inAt.Add($"{f.Id}: {p.Role} {into:F2} m in at {p.Pos.X:F2},{p.Pos.Y:F2},{p.Pos.Z:F2}, spot {p.Spot} {town.Spots[p.Spot].Tag}, left {p.Left}, step {p.Step}/{p.Path?.Count ?? -1}, seat {p.Seat}, {Town.HeightOf(p):F2} m tall, r {Town.RadiusOf(p):F2}"); }
                        float? g = town.Ground(p.Pos);
                        float off = g is float h ? MathF.Abs(h - p.Pos.Y) : 9f;
                        if (!p.Sitting && !(p.Slept > 0f) && !town.IntoSeat(p))
                        {
                            worst = MathF.Max(worst, MathF.Min(off, 9f));
                            if (off > 0.3f && sunk++ < 1000 && sunkAt.Count < 3)
                            {
                                var nav = Place.FootNavOf(memory);
                                int x = (int)MathF.Floor(p.Pos.X), z = (int)MathF.Floor(p.Pos.Z);
                                string col = string.Join(" ", Enumerable.Range(-2, 5).Select(dy => { var c = new PInt3(x, (int)MathF.Floor(p.Pos.Y) + dy, z); return $"{c.Y}:{inst.Blocks.Get(c).Type}{(nav.Standable(c) ? "*" : "")}{(memory.Rooms.Values.Any(r => r.Cells.Contains(c)) ? "D" : "")}"; }));
                                string to = p.Path != null && p.Step < p.Path.Count ? $"{p.Path[p.Step].X:F1},{p.Path[p.Step].Y:F1},{p.Path[p.Step].Z:F1} step {p.Step}/{p.Path.Count}" : "none";
                                sunkAt.Add($"{f.Id}: {p.Role} at {p.Pos.X:F1},{p.Pos.Y:F2},{p.Pos.Z:F1}, ground {(g is float gg ? gg.ToString("F2") : "none")} [{col}] heading {to}, spot {p.Spot} {town.Spots[p.Spot].Tag} {town.Spots[p.Spot].Cell}{(nav.Standable(town.Spots[p.Spot].Cell) ? " standable" : " not standable")}{(p.Spot < town.Data.Spots.Count ? " (place's own)" : " (furniture)")}, left {p.Left}{(p.Left >= 0 && p.Left < town.Spots.Count ? $" {town.Spots[p.Left].Tag} {town.Spots[p.Left].Cell}" : "")}, seat {p.Seat}, at {t:F1} s, {(p.Path is { Count: > 0 } path ? $"path from {path[0].X:F1},{path[0].Y:F1},{path[0].Z:F1}" : "no path")}");
                            }
                        }
                        PInt3 cell = new((int)MathF.Floor(p.Pos.X), (int)MathF.Floor(p.Pos.Y + 0.05f), (int)MathF.Floor(p.Pos.Z));
                        int module = moduleOf.TryGetValue(cell, out int mi) ? mi : moduleOf.TryGetValue(cell + PInt3.Up, out mi) ? mi : -1;
                        bool walking = p.Path != null && p.Step < p.Path.Count;
                        if (!last.TryGetValue(p, out var was)) was = (module, t, p.Pos, 0f, rooms.Contains(module), false);
                        bool inRoom = rooms.Contains(module);
                        bool wasIn = was.WasIn;
                        if (inRoom && !was.InRoom && module >= 0) { ins++; wasIn = true; }
                        if (!inRoom && was.InRoom && module >= 0 && wasIn) outs++;
                        float still = walking && (p.Pos - was.At).Length() < 0.02f ? was.Still + 0.1f : 0f;
                        if (still > 5f && was.Still <= 5f) { stuck++; if (stuckAt.Count < 3) stuckAt.Add($"{f.Id}: {p.Role} at {p.Pos.X:F2},{p.Pos.Y:F2},{p.Pos.Z:F2} heading {(p.Step < p.Path.Count ? p.Path[p.Step].ToString() : "-")} step {p.Step}/{p.Path.Count}, spot {p.Spot} {town.Spots[p.Spot].Tag}, left {p.Left}, seat {p.Seat}, up {p.GettingUp}, gap {(town.Parts != null ? town.Parts.Gap(p.Pos, Town.RadiusOf(p), Town.HeightOf(p)) : 9f):F2}, next gap {(town.Parts != null && p.Step < p.Path.Count ? town.Parts.Gap(p.Path[p.Step], Town.RadiusOf(p), Town.HeightOf(p)) : 9f):F2}, path {string.Join(" ", p.Path.Take(4).Select(q => $"{q.X:F2},{q.Z:F2}"))}{(p.Step < p.Path.Count && DoorWays.At(memory, new PInt3((int)MathF.Floor(p.Path[p.Step].X), (int)MathF.Floor(p.Path[p.Step].Y + 0.05f), (int)MathF.Floor(p.Path[p.Step].Z))) is { } dw ? $", door {dw.Index} open {dw.Open:F2} target {dw.Target:F2} jammed {dw.Jammed} lift {dw.Lift} gap {dw.Gap:F2}" : "")}"); }
                        longest = MathF.Max(longest, still);
                        last[p] = (module >= 0 ? module : was.Module, t, p.Pos, still, module >= 0 ? inRoom : was.InRoom, wasIn);
                        if (!walking && p.Spot < town.Spots.Count)
                        {
                            string tag = town.Spots[p.Spot].Tag;
                            if (p.Sitting && p.Spot >= town.Data.Spots.Count) sat++;
                            if (tag is "work" or "stall" && p.Spot >= town.Data.Spots.Count) worked++;
                        }
                    });
                people += town.People.Count;
                foreach (var (a, b, path) in town.Routes)
                {
                    routes++;
                    var bad = town.Blocked(path, b, a).ToList();
                    // From one part of the place to another on the level: by a doorway, or where the two lie open to each other.
                    for (int k = 1; k < path.Count; k++)
                    {
                        PInt3 c0 = new((int)MathF.Floor(path[k - 1].X), (int)MathF.Floor(path[k - 1].Y + 0.05f), (int)MathF.Floor(path[k - 1].Z));
                        PInt3 c1 = new((int)MathF.Floor(path[k].X), (int)MathF.Floor(path[k].Y + 0.05f), (int)MathF.Floor(path[k].Z));
                        if (c0.Y != c1.Y || !partsOf.TryGetValue(c0, out var m0) || !partsOf.TryGetValue(c1, out var m1) || m0.Overlaps(m1)) continue;
                        changes++;
                        if (doors.Contains(c0) || doors.Contains(c1)) continue;
                        doorless++;
                        if (doorAt.Count < 3) doorAt.Add($"{f.Id}: spot {a} to {b}, from module {string.Join("/", m0)} at {c0} to {string.Join("/", m1)} at {c1}, {inst.Blocks.Get(c0).Type}/{inst.Blocks.Get(c1).Type}");
                    }
                    if (bad.Count > 0 && crossing++ < 1000 && crossAt.Count < 3) crossAt.Add($"{f.Id}: spot {a} ({town.Spots[a].Tag}) to {b} ({town.Spots[b].Tag}) through {bad[0]} ({(memory.Rooms.Values.Any(r => r.Cells.Contains(bad[0])) ? "a piece" : inst.Blocks.Get(bad[0]).Type.ToString())}, point {path.FindIndex(q => new PInt3((int)MathF.Floor(q.X), (int)MathF.Floor(q.Y + 0.05f), (int)MathF.Floor(q.Z)) == bad[0])} of {path.Count})");
                }
            }
        }
        Check(places >= 8 && people > 0, $"{places} lived-in places walked, {people} people, {samples} steps watched ({beds} beds and {seats} seats furnished as spots)");
        Check(sunk == 0, $"every person's feet stay within 0.3 m of a floor, a stair or a ramp (worst {worst:F2} m; {sunk} steps off){(sunkAt.Count > 0 ? "; e.g. " + string.Join("; ", sunkAt) : "")}");
        Check(crossing == 0, $"no route crosses a piece of furniture or dressing ({routes} routes){(crossAt.Count > 0 ? "; e.g. " + string.Join("; ", crossAt) : "")}");
        Check(inParts == 0, $"no one's body is ever in a piece of furniture or clutter as it really stands ({inParts} steps in one, deepest {deepest:F2} m){(inAt.Count > 0 ? "; e.g. " + string.Join("; ", inAt) : "")}");
        Check(changes > 0 && doorless == 0, $"from one part of a place to another through a doorway ({changes} crossings, {doorless} not by a door){(doorAt.Count > 0 ? "; e.g. " + string.Join("; ", doorAt) : "")}");
        Check(ins > 0 && outs > 0, $"people go into rooms and come out again ({ins} in, {outs} out)");
        Check(slept > 0 && sat + worked > 0, $"and use them: {slept} slept in beds, {sat} steps sat on seats, {worked} steps at benches and counters");
        Check(stuck == 0, $"nobody is stuck on their way for over 5 s (longest {longest:F1} s){(stuckAt.Count > 0 ? "; e.g. " + string.Join("; ", stuckAt) : "")}");
    }
}
