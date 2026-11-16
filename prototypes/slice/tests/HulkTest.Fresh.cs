using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using GalHack.Structures;
using Godot;
using PoiGenerator.Gen;
using Slice.Game;
using Slice.Orbit;
using Slice.Places;
using Int3 = GalHack.Structures.Int3;
using Block = GalHack.Structures.Block;
using Structure = GalHack.Structures.Structure;

namespace Slice.Tests;

public partial class HulkTest
{
    /// <summary>
    /// The rooms a capital hull has (as it was built), whatever its role, by how much floor its form gave it: a bridge
    /// always (and a seat for a core, checked apart); from 200 m² an engine room and a reactor, from 400 m² a hold, and on
    /// a hull crewed by people (not the Kessari's machines) from 800 m² the crew's quarters, a galley and a medbay. A
    /// small hull folds the rest into what it has (the generators into the engine room, the core's seat into the
    /// reactor, the engine room or the bridge).
    /// </summary>
    static IEnumerable<string[]> Needs(float floor, bool people)
    {
        yield return new[] { "bridge" };
        if (floor < 200) yield break;
        yield return new[] { "engine-room" };
        yield return new[] { "reactor" };
        if (floor >= 400) yield return new[] { "hold", "ore-hold" };
        if (!people || floor < 800) yield break;
        yield return new[] { "crew-quarters", "cabin", "barracks" };
        yield return new[] { "galley" };
        yield return new[] { "medbay" };
    }

    /// <summary>
    /// Every capital site's hull (and two more of a random role and size) on fresh seeds, built as a wreck and as it was
    /// built, measured from its blocks (WreckAudit) and its rooms.
    /// </summary>
    IEnumerable<int> Fresh()
    {
        GD.Print("-- fresh hulls, every capital site, built away from the world --");
        PoiLibrary lib = WorldChoice.Library;
        Catalogue cat = Cat;
        var deco = Hulks.Deco();
        var sites = lib.Sites.Values.Where(s => s.Parts.Count > 0 && s.Parts[0].Arg("what") == "capital").ToList();
        var specs = sites.Select(s => (Site: s, Spec: CapitalHull.SpecOf(s.Parts[0]))).ToList();
        string[] roles = { "freighter", "warship", "liner", "miner", "carrier" }, sizes = { "medium", "large", "huge" }, crashes = { "broken", "nose", "scattered", "long" };
        // And one more of every faction's style, of a random role, size and crash (each must still be a ship a crew can walk).
        foreach (string faction in Styled)
            specs.Add((sites[0], new CapitalHull.Spec(roles[Random.Shared.Next(roles.Length)], sizes[Random.Shared.Next(sizes.Length)], crashes[Random.Shared.Next(crashes.Length)], faction)));
        var problems = new List<string>();
        int bad = 0, hulls = 0;
        void Bad(bool ok, string what) { if (!ok) { bad++; problems.Add(what); } }
        var shapes = new List<string>();
        float floor = 0, corridor = 0, machines = 0, clear4 = 0, lowLoose = 0, rooms = 0, dressed = 0, lit = 0, litRooms = 0, pieces = 0;
        double slowest = 0;
        var byPurpose = new Dictionary<string, (int Floor, int Corridor, int Taken)>();
        foreach (var (site, spec) in specs)
        {
            ulong seed = (ulong)Random.Shared.NextInt64();
            CapitalHull.HullPlan plan = CapitalHull.Plan(lib.Kits[site.Style], WorldProfile.Parse(site.Parts[0].Arg("tech", "mid")), spec, seed);
            var clock = Stopwatch.StartNew();
            List<HullVessel.Result> wrecks = Hulks.Build(plan, cat, PoiState.Abandoned, seed, deco);
            slowest = Math.Max(slowest, clock.Elapsed.TotalMilliseconds);
            var intact = plan.Pieces.Select((p, i) => HullVessel.Build(plan, p, cat, PoiState.Abandoned, seed + (ulong)i * 7919UL, intact: true)).ToList();
            yield return 0;
            hulls++;
            string name = $"{plan.Style.Id} {spec.Role} {spec.Size} {spec.Crash} ({seed})";
            var (l0, l1, _, _, _) = CapitalHull.SizeOf(spec.Size);
            int length = plan.L;
            // (Each faction's plan its own share of the class's length: the Reavers' stubby, the Meridian's long.)
            Bad(plan.L >= l0 * 0.7f && length <= 290, $"{name}: {length} m long");
            // Its rooms, as built.
            var purposes = intact.SelectMany(r => r.Spaces).Where(s => !s.Corridor && s.Floor.Count >= 6).Select(s => s.Purpose).ToHashSet();
            float inside = plan.Inside.Rooms.Sum(r => r.Area);
            foreach (string[] need in Needs(inside, plan.Faction != Faction.Kessari)) Bad(need.Any(purposes.Contains), $"{name}: no {need[0]} ({inside:F0} m² inside; {plan.Inside.Log})");
            // Its devices, as built: the player's real parts, no fuel or warp tanks (fuel is kept in the cargo containers).
            var built = intact.SelectMany(r => r.Structure.Blocks().Where(b => !b.block.IsFiller).Select(b => r.Structure.TypeOf(b.block))).ToList();
            int Count(string function) => built.Count(t => t.Function == function);
            Bad(Count("thruster") >= 8 && Count("generator") >= 1 && Count("gyro") >= 2 && Count("warp-drive") >= 1 && Count("turret") >= 2 && Count("container") >= 1 && Count("chair") >= 2 && !built.Any(t => t.Id.StartsWith("cockpit")),
                $"{name}: devices {Count("thruster")} thrusters, {Count("generator")} generators, {Count("gyro")} gyros, {Count("warp-drive")} warp drives, {Count("turret")} turrets, {Count("container")} containers, {Count("chair")} chairs");
            Bad(Count("fuel") == 0 && Count("warp-tank") == 0, $"{name}: fuel or warp tanks");
            Bad(intact.All(r => r.Structure.Core == null) && intact.Count(r => r.CoreSlot is Int3 c && !r.Structure.Has(c) && r.Structure.Has(c - Int3.UnitY)) == 1, $"{name}: a core, or no empty seat for one");
            // Flight-worthy once repaired, by the player's rules (DESIGN 5): the piece with the core's seat has a command chair,
            // thrusters on every axis with lift for its weight at a heavy world's gravity, gyros, generators for what draws
            // power, and containers for its fuel (no fuel tanks; no capacitor component exists to need).
            foreach (HullVessel.Result r in intact.Where(r => r.CoreSlot != null))
            {
                // (A structure's stats count thrust only once a core flies it: summed here as the stats would.)
                var st = StructureStats.Compute(r.Structure);
                var push = new double[6];
                foreach (var (_, b) in r.Structure.Blocks())
                    if (!b.IsFiller && r.Structure.TypeOf(b).IsThruster) push[(int)Orientations.ToWorld(b.Rot, GalHack.Structures.Dir.NegZ)] += r.Structure.TypeOf(b).Thrust;
                double up = push[(int)GalHack.Structures.Dir.PosY];
                var types = r.Structure.Blocks().Where(b => !b.block.IsFiller).Select(b => r.Structure.TypeOf(b.block)).ToList();
                bool chair = types.Any(t => t.IsCommandChair), gyros = types.Any(t => t.Function == "gyro"), fuel = types.Any(t => t.IsContainer);
                Bad(up > st.Mass * StructureStats.G * 1.3 && push.All(p => p > 0) && chair && gyros && fuel && st.PowerOut >= st.PowerUse,
                    $"{name}{r.Piece.Name}: lift {up / 1000:F0} MN for {st.Mass * StructureStats.G * 1.3 / 1000:F0} MN, thrust by axis {string.Join("/", push.Select(p => (p / 1000).ToString("F1")))} MN, chair {chair}, gyros {gyros}, containers {fuel}, power {st.PowerOut:F0} of {st.PowerUse:F0} MW; as built: {r.Thrust}");
            }
            // The wreck as found: measured from its blocks.
            foreach (HullVessel.Result r in wrecks)
            {
                WreckAudit a = WreckAudit.Of(r.Structure);
                var loose = a.LowCells.Where(c => !r.Crushed.Contains(c) && !Stair(r, c)).ToList();
                GD.Print($"      {name}{r.Piece.Name}: {a}; {r.Decor.Count} pieces of dressing; built in {clock.Elapsed.TotalMilliseconds / wrecks.Count:F0} ms ({r.Took}); {r.Thrust}" +
                         (loose.Count > 0 ? $"; under 4 m outside the crush: {string.Join(", ", loose.GroupBy(c => c.Y).Select(g => $"{g.Count()} at y {g.Key} (e.g. {g.First()} in {r.Spaces.FirstOrDefault(s => s.Y == g.Key && s.Floor.Contains((g.First().X, g.First().Z + r.Piece.V0)))?.Purpose ?? "no space"} under {Over(r.Structure, g.First())})"))}" : ""));
                floor += a.Floor;
                corridor += a.Corridor;
                machines += a.Machines;
                clear4 += a.Clear4;
                lowLoose += a.LowCells.Count(c => !r.Crushed.Contains(c) && !Stair(r, c));
                var cells = r.Decor.SelectMany(d => d.Cells).ToHashSet();
                foreach (HullVessel.Space s in r.Spaces.Where(s => !s.Corridor && s.Floor.Count >= 6))
                {
                    var floorCells = s.Floor.Select(f => new Int3(f.U, s.Y, f.V - r.Piece.V0)).Where(c => r.Structure.Has(c - Int3.UnitY)).ToList();
                    if (floorCells.Count < 6) continue;
                    rooms++;
                    if (floorCells.Any(cells.Contains) || s.Hall) dressed++;
                }
                pieces += r.Decor.Count;
                foreach (HullVessel.Space s in r.Spaces)
                {
                    var on = s.Floor.Select(f => new Int3(f.U, s.Y, f.V - r.Piece.V0)).Where(a.FloorCells.Contains).ToList();
                    var k = byPurpose.GetValueOrDefault(s.Purpose);
                    byPurpose[s.Purpose] = (k.Floor + on.Count, k.Corridor + on.Count(a.CorridorCells.Contains), k.Taken + s.Taken.Count);
                }
            }
            // Every space's floor got into as built, by a door or a stair (the voids left sealed inside solid machinery are no one's).
            foreach (HullVessel.Result r in intact)
                foreach (List<Int3> part in WreckAudit.Of(r.Structure).Unreached)
                    if (r.Spaces.FirstOrDefault(s => part.Any(c => s.Y == c.Y && s.Floor.Contains((c.X, c.Z + r.Piece.V0)))) is HullVessel.Space lost)
                        Bad(false, $"{name}{r.Piece.Name}: {part.Count} m² of floor no door or stair gets into, the {lost.Purpose} on deck {lost.Deck}, x {part.Min(c => c.X)}..{part.Max(c => c.X)} y {part.Min(c => c.Y)}..{part.Max(c => c.Y)} z {part.Min(c => c.Z)}..{part.Max(c => c.Z)}");
            // Every room lit as it was built (the years since broke some of the lamps).
            foreach (HullVessel.Result r in intact)
            {
                var lights = r.Structure.Blocks().Where(b => r.Structure.TypeOf(b.block).IsLight).Select(b => b.pos).ToList();
                foreach (HullVessel.Space s in r.Spaces.Where(s => !s.Corridor && s.Floor.Count >= 6))
                    if (lights.Any(p => s.Floor.Contains((p.X, p.Z + r.Piece.V0)) && p.Y >= s.Y && p.Y < s.Y + s.Height)) lit++;
                    else if (lit >= 0)
                    {
                        var (cu, cv) = s.Floor.OrderBy(c => Math.Abs(c.U - (s.U0 + s.U1) / 2.0) + Math.Abs(c.V - (s.V0 + s.V1) / 2.0)).First();
                        Int3 at = new(cu, s.Y + s.Height - 1, cv - r.Piece.V0);
                        string What(Int3 p) => r.Structure.TryGet(p, out Block b) ? r.Structure.TypeOf(b).Id : "nothing";
                        problems.Add($"{name}{r.Piece.Name}: an unlit {s.Purpose} of {s.Floor.Count} m² on deck {s.Deck} (y {s.Y}, {s.Height} m; at its middle {What(at)} under {What(at + Int3.UnitY)})");
                    }
                litRooms += r.Spaces.Count(s => !s.Corridor && s.Floor.Count >= 6);
            }
            shapes.Add($"{plan.Form.Plan}/{plan.L}/{plan.Inside.Style}/{string.Join(",", plan.Inside.Rooms.Where(x => x.Area > 0).Select(x => x.Area))}");
        }
        foreach (string p in problems.Take(12)) GD.PrintErr("      " + p);
        Check(bad == 0, $"{hulls} hulls on fresh seeds: each its decks fitted inside its form, of the player's blocks, with a bridge, engine room, reactor and core room (a hold, quarters, galley and medbay as its floor allows), every deck got into by a door, a stair or a hatch; thrusters, generators, gyros, a warp drive, turrets, cargo containers and command chairs, no cockpit and no fuel tanks; coreless with a seat for a core; once repaired it lacks nothing to fly: thrust on every axis, lift for its weight, gyros, power, a chair and containers for fuel");
        Check(shapes.Distinct().Count() == shapes.Count, $"each hull its own: {shapes.Distinct().Count()} different plans of {shapes.Count}");
        Check(rooms > 0 && dressed >= rooms * 0.9f && lit >= litRooms, $"rooms dressed and lit for their purpose: {dressed} of {rooms} dressed ({pieces} pieces), {lit} of {litRooms} lit as built");
        float share = corridor / Math.Max(1f, floor), per100 = machines * 100f / Math.Max(1f, floor);
        GD.Print($"      floor by purpose (corridor share): {string.Join(", ", byPurpose.OrderByDescending(kv => kv.Value.Floor).Select(kv => $"{kv.Key} {kv.Value.Floor} m² ({kv.Value.Corridor * 100 / Math.Max(1, kv.Value.Floor)}%, {kv.Value.Taken} taken)"))}");
        Check(share is >= 0.5f and <= 0.7f, $"corridors {share:P0} of {floor:F0} m² of floor (Empyrion's derelicts 59%)");
        Check(per100 is >= 2.2f and <= 3.3f, $"{per100:F2} machines and containers per 100 m² of floor (Empyrion's wrecks 2.7)");
        Check(clear4 >= floor * 0.9f && lowLoose <= floor * 0.02f, $"4 m decks: {clear4 / Math.Max(1f, floor):P0} of the floor 4 m clear; under that only where crushed, on the stairs or in crawlways but {lowLoose:F0} m²");
        Check(slowest < 20000, $"the slowest hull built in {slowest:F0} ms on a worker");
    }

    /// <summary>What a low floor cell has over it: the first block up, its type and how high.</summary>
    static string Over(Structure s, Int3 c)
    {
        for (int h = 1; h < 11; h++)
            if (s.TryGet(c + Int3.UnitY * h, out Block b) && s.TypeOf(b).Collide != Collide.None) return $"{s.TypeOf(b).Id}/{s.ShapeOf(b).Id} at +{h}";
        return "nothing";
    }

    /// <summary>Is a structure cell on a stair core's landings or flights (their headroom is the flight's), or in a crawlway (2 m by its nature)?</summary>
    static bool Stair(HullVessel.Result r, Int3 c) => r.Spaces.Any(s => s.Floor.Contains((c.X, c.Z + r.Piece.V0)) && (s.Purpose == "stair" && Math.Abs(c.Y - s.Y) <= 4 || s.Purpose == "crawlway" && c.Y == s.Y));

    /// <summary>A start system's derelict capital ship, adrift in orbit: the same hull builder's, dressed, its door to board by.</summary>
    IEnumerable<int> Adrift()
    {
        GD.Print("-- a derelict capital ship in orbit --");
        var data = _game.Zone.Data;
        DerelictPlan ship = null;
        var map = new GlobeMap(16000, 8000, 16000 / Math.Tau);
        DerelictPlan Of(ulong system) => Derelicts.Plan(WorldChoice.Library, system, map, 16000 / Math.Tau * 1.2, OrbitRules.Shared, data.Catalogue, data.Items).FirstOrDefault(p => p.Hull != null);
        ulong seed = 0;
        for (int i = 0; i < 12 && ship == null; i++)
        {
            seed = (ulong)Random.Shared.NextInt64();
            ship = Of(seed);
            yield return 0;
        }
        if (!Check(ship != null, "a start system with a derelict capital ship in its near orbit")) yield break;
        // A save keeps what play changed on top of the seed (DESIGN 21: edits stored as differences), so the seed must build it again block for block.
        DerelictPlan again = Of(seed);
        Check(again?.Id == ship.Id && Blueprint.Save(again.Structure) == Blueprint.Save(ship.Structure) && again.Loot.Equals(ship.Loot) && again.Hull.Decor.Count == ship.Hull.Decor.Count,
            $"{ship.Name} built again from its system's seed block for block, its hold and its dressing the same (what a save keeps its changes against)");
        WreckAudit a = WreckAudit.Of(ship.Structure);
        GD.Print($"      {ship.Name}: {a}; {ship.Hull.Decor.Count} pieces of dressing");
        int length = ship.Hull.Plan.L;
        Check(ship.Structure.Core == null && ship.Hull.Decor.Count > 0 && length >= 60 && a.Clear4 >= a.Floor * 0.85f,
            $"{ship.Name} is the hull builder's: coreless, a {length} m hull ({a.Length} m of it in this piece), {ship.Hull.Decor.Count} pieces of dressing, {a.Clear4 * 100f / Math.Max(1, a.Floor):F0}% of its floor 4 m clear");
    }
}
