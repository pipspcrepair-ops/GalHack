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
    /// <summary>The rooms every capital hull has (as it was built), whatever its role: the crew's and the ship's.</summary>
    static readonly string[][] Needs =
    {
        new[] { "bridge" }, new[] { "crew-quarters", "cabin", "barracks" }, new[] { "galley" }, new[] { "medbay" },
        new[] { "hold", "ore-hold" }, new[] { "engine-room" }, new[] { "reactor" },
    };

    /// <summary>
    /// Every capital site's hull (and two more of a random role and size) on fresh seeds, built as a wreck and as it was
    /// built, measured from its blocks (WreckAudit) and its rooms.
    /// </summary>
    IEnumerable<int> Fresh()
    {
        GD.Print("-- fresh hulls, every capital site, built away from the world --");
        PoiLibrary lib = WorldChoice.Library;
        Catalogue cat = _game.Zone.Data.Catalogue;
        var deco = Hulks.Deco();
        var sites = lib.Sites.Values.Where(s => s.Parts.Count > 0 && s.Parts[0].Arg("what") == "capital").ToList();
        var specs = sites.Select(s => (Site: s, Spec: CapitalHull.SpecOf(s.Parts[0]))).ToList();
        string[] roles = { "freighter", "warship", "liner", "miner", "carrier" }, sizes = { "medium", "large", "huge" }, crashes = { "broken", "nose", "scattered", "long" };
        for (int i = 0; i < 2; i++)
            specs.Add((sites[0], new CapitalHull.Spec(roles[Random.Shared.Next(roles.Length)], sizes[Random.Shared.Next(sizes.Length)], crashes[Random.Shared.Next(crashes.Length)])));
        var problems = new List<string>();
        int bad = 0, hulls = 0;
        void Bad(bool ok, string what) { if (!ok) { bad++; problems.Add(what); } }
        var shapes = new List<string>();
        float floor = 0, corridor = 0, machines = 0, clear4 = 0, lowLoose = 0, rooms = 0, dressed = 0, lit = 0, pieces = 0;
        double slowest = 0;
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
            string name = $"{spec.Role} {spec.Size} {spec.Crash} ({seed})";
            var (l0, l1, _, _, _) = CapitalHull.SizeOf(spec.Size);
            int length = plan.L + plan.Nac;
            Bad(plan.L >= l0 && length <= 290, $"{name}: {length} m long");
            // Its rooms, as built.
            var purposes = intact.SelectMany(r => r.Spaces).Where(s => !s.Corridor && s.Floor.Count >= 6).Select(s => s.Purpose).ToHashSet();
            foreach (string[] need in Needs) Bad(need.Any(purposes.Contains), $"{name}: no {need[0]}");
            // Its devices, as built: the player's real parts, no fuel or warp tanks (fuel is kept in the cargo containers).
            var built = intact.SelectMany(r => r.Structure.Blocks().Where(b => !b.block.IsFiller).Select(b => r.Structure.TypeOf(b.block))).ToList();
            int Count(string function) => built.Count(t => t.Function == function);
            Bad(Count("thruster") >= 8 && Count("generator") >= 2 && Count("gyro") >= 2 && Count("warp-drive") >= 1 && Count("turret") >= 2 && Count("container") >= 10 && Count("chair") >= 2 && built.Any(t => t.Id.StartsWith("cockpit")),
                $"{name}: devices {Count("thruster")} thrusters, {Count("generator")} generators, {Count("gyro")} gyros, {Count("warp-drive")} warp drives, {Count("turret")} turrets, {Count("container")} containers, {Count("chair")} chairs");
            Bad(Count("fuel") == 0 && Count("warp-tank") == 0, $"{name}: fuel or warp tanks");
            Bad(intact.All(r => r.Structure.Core == null) && intact.Count(r => r.CoreSlot is Int3 c && !r.Structure.Has(c) && r.Structure.Has(c - Int3.UnitY)) == 1, $"{name}: a core, or no empty seat for one");
            // Flight-worthy once repaired: lift for its weight at a heavy world's gravity, power for what draws it.
            foreach (HullVessel.Result r in intact)
            {
                var st = StructureStats.Compute(r.Structure);
                Bad(st.Thrust[(int)GalHack.Structures.Dir.PosY] > st.Mass * StructureStats.G * 1.3 && st.PowerOut >= st.PowerUse, $"{name}{r.Piece.Name}: lift {st.Thrust[(int)GalHack.Structures.Dir.PosY] / 1000:F0} MN for {st.Mass * StructureStats.G * 1.3 / 1000:F0} MN, power {st.PowerOut:F0} of {st.PowerUse:F0} MW");
            }
            // The wreck as found: measured from its blocks.
            foreach (HullVessel.Result r in wrecks)
            {
                WreckAudit a = WreckAudit.Of(r.Structure);
                GD.Print($"      {name}{r.Piece.Name}: {a}; {r.Decor.Count} pieces of dressing; built in {clock.Elapsed.TotalMilliseconds / wrecks.Count:F0} ms");
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
                    if (r.Structure.Blocks().Any(b => r.Structure.TypeOf(b.block).IsLight && s.Floor.Contains((b.pos.X, b.pos.Z + r.Piece.V0)) && b.pos.Y >= s.Y && b.pos.Y < s.Y + s.Height)) lit++;
                }
                pieces += r.Decor.Count;
            }
            // The silhouette: a tapered bow and sloped shoulders, of the shaped blocks.
            HullVessel.Result fore = intact[0];
            Structure f0 = fore.Structure;
            int Beam(int z) => f0.Blocks().Where(b => b.pos.Z == z).Select(b => b.pos.X).DefaultIfEmpty(0).Max() * 2 + 1;
            int mid = Math.Min(plan.Bz + 6, plan.Pieces[0].V1 - 1) - fore.Piece.V0;
            bool tapered = Beam(1) * 2 <= Beam(mid);
            int edge = f0.Blocks().Where(b => b.pos.Z == mid && b.pos.Y >= plan.Top - 3 && b.pos.Y <= plan.Top).Max(b => b.pos.X);
            bool shoulder = f0.TryGet(new Int3(edge, f0.Blocks().Where(b => b.pos.Z == mid && b.pos.X == edge).Max(b => b.pos.Y), mid), out Block top) && !f0.ShapeOf(top).IsCube;
            Bad(tapered && shoulder, $"{name}: beam {Beam(1)} m at the stem against {Beam(mid)} m, shoulder {(shoulder ? "sloped" : "square")}");
            shapes.Add($"{plan.L}/{plan.Half}/{string.Join(",", plan.Bays.Select(b => b.V1 - b.V0))}");
        }
        foreach (string p in problems.Take(12)) GD.PrintErr("      " + p);
        Check(bad == 0, $"{hulls} hulls on fresh seeds: each a ship's own plan of the player's blocks with bridge, quarters, galley, medbay, hold, engine room and reactor; thrusters, generators, gyros, a warp drive, turrets, cargo containers, chairs and a cockpit, no fuel tanks; coreless with a seat for a core; lift and power to fly once repaired; a tapered bow and sloped shoulders");
        Check(shapes.Distinct().Count() == shapes.Count, $"each hull its own: {shapes.Distinct().Count()} different plans of {shapes.Count}");
        Check(rooms > 0 && dressed >= rooms * 0.9f && lit >= rooms * 0.98f, $"rooms dressed and lit for their purpose: {dressed} of {rooms} dressed ({pieces} pieces), {lit} lit");
        float share = corridor / Math.Max(1f, floor), per100 = machines * 100f / Math.Max(1f, floor);
        Check(share is >= 0.5f and <= 0.7f, $"corridors {share:P0} of {floor:F0} m² of floor (Empyrion's derelicts 59%)");
        Check(per100 is >= 2.2f and <= 3.3f, $"{per100:F2} machines and containers per 100 m² of floor (Empyrion's wrecks 2.7)");
        Check(clear4 >= floor * 0.9f && lowLoose <= floor * 0.02f, $"4 m decks: {clear4 / Math.Max(1f, floor):P0} of the floor 4 m clear; under that only where crushed or on the stairs but {lowLoose:F0} m²");
        Check(slowest < 20000, $"the slowest hull built in {slowest:F0} ms on a worker");
    }

    /// <summary>Is a structure cell on a stair core's landings or flights (their headroom is the flight's)?</summary>
    static bool Stair(HullVessel.Result r, Int3 c) => r.Spaces.Any(s => s.Purpose == "stair" && s.Floor.Contains((c.X, c.Z + r.Piece.V0)) && Math.Abs(c.Y - s.Y) <= 4);

    /// <summary>A start system's derelict capital ship, adrift in orbit: the same hull builder's, dressed, its door to board by.</summary>
    IEnumerable<int> Adrift()
    {
        GD.Print("-- a derelict capital ship in orbit --");
        var data = _game.Zone.Data;
        DerelictPlan ship = null;
        for (int i = 0; i < 12 && ship == null; i++)
        {
            var map = new GlobeMap(16000, 8000, 16000 / Math.Tau);
            ship = Derelicts.Plan(WorldChoice.Library, (ulong)Random.Shared.NextInt64(), map, 16000 / Math.Tau * 1.2, OrbitRules.Shared, data.Catalogue, data.Items).FirstOrDefault(p => p.Hull != null);
            yield return 0;
        }
        if (!Check(ship != null, "a start system with a derelict capital ship in its near orbit")) yield break;
        WreckAudit a = WreckAudit.Of(ship.Structure);
        GD.Print($"      {ship.Name}: {a}; {ship.Hull.Decor.Count} pieces of dressing");
        Check(ship.Structure.Core == null && ship.Hull.Decor.Count > 0 && a.Length >= 60 && a.Clear4 >= a.Floor * 0.85f,
            $"{ship.Name} is the hull builder's: coreless, {a.Length} m long, {ship.Hull.Decor.Count} pieces of dressing, {a.Clear4 * 100f / Math.Max(1, a.Floor):F0}% of its floor 4 m clear");
    }
}
