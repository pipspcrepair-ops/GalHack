#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;

namespace PoiGenerator.Gen;

/// <summary>
/// The architect's front door. Plans a building or composes a site, then checks the layout before
/// anything can place it: corridors whole from end to end, doors set in walls with floor both
/// sides, stairs connecting their storeys, every space reachable, nothing floating. A layout that
/// fails is never returned: the architect regenerates from the next sub-seed and counts it.
/// </summary>
public static class Architect
{
    public const int MaxAttempts = 24;

    /// <summary>Running totals for the tests: how many layouts were generated, failed and regenerated.</summary>
    public static class Stats
    {
        public static int Generated, Failed, Regenerated;
        public static readonly List<string> FirstFailures = new();
        public static void Reset()
        {
            Generated = Failed = Regenerated = 0;
            lock (FirstFailures) FirstFailures.Clear();
        }
    }

    public static Layout Build(PoiLibrary lib, FamilyDef family, VariantDef variant, Rarity rarity, ulong seed)
    {
        string? last = null;
        for (int attempt = 0; attempt < MaxAttempts; attempt++)
        {
            var rng = new Rng(Hash.Combine(seed, (ulong)rarity + 1, 0xA2C417EUL + (ulong)attempt * 0x9E37UL));
            // The family's own style kit re-skins its program or site (a flavour, such as alien).
            string kitId = family.Style ?? (family.Site != null || variant.Site != null
                ? lib.Sites[variant.Site ?? family.Site!].Style
                : lib.Programs[variant.Program ?? family.Program!].Style);
            var canvas = new Canvas(lib.Kits[kitId]);
            canvas.X.Wrong = rng.Chance(family.Wrong);
            try
            {
                if (variant.Site != null || family.Site != null) Sites.Compose(lib, canvas, lib.Sites[variant.Site ?? family.Site!], family, rarity, rng.Fork("site"));
                else
                {
                    var built = Building.Build(canvas, lib, family, rarity, new BuildingSpec { Program = lib.Programs[variant.Program ?? family.Program!] }, rng.Fork("building"));
                    canvas.EntranceCell = built.EntranceCell;
                    canvas.EntranceFacing = built.EntranceFacing;
                    canvas.ExitCells = built.ExitCells;
                    canvas.EntranceModule = built.EntranceModule;
                    canvas.VaultModule = built.VaultModule >= 0 ? built.VaultModule : built.DeepestModule;
                }
                Decay.Plan(canvas, family, rarity, rng.Fork("wrong"));
                var layout = canvas.Finish(family, variant, rarity, seed, variant.Name);
                var problems = LayoutCheck.Check(layout);
                lock (typeof(Stats)) Stats.Generated++;
                if (problems.Count == 0)
                {
                    layout.Attempts = attempt + 1;
                    layout.Regenerations = attempt;
                    if (attempt > 0) lock (typeof(Stats)) Stats.Regenerated++;
                    return layout;
                }
                last = problems[0];
            }
            catch (Exception e) when (e is InvalidOperationException or ArgumentException or KeyNotFoundException)
            {
                lock (typeof(Stats)) Stats.Generated++;
                last = e.Message + " @ " + (e.StackTrace ?? "").Split('\n').FirstOrDefault(l => l.Contains(".cs:"))?.Trim();
            }
            lock (typeof(Stats))
            {
                Stats.Failed++;
                if (Stats.FirstFailures.Count < 12) Stats.FirstFailures.Add($"{family.Id} seed {seed} attempt {attempt}: {last}");
            }
        }
        throw new InvalidOperationException($"{family.Id} '{variant.Name}' ({rarity}, seed {seed}): no valid layout after {MaxAttempts} attempts; last: {last}");
    }
}

/// <summary>
/// The release-blocking checks on a layout as built, before any state damages it on purpose:
///   - every corridor cell has floor under it, three cells of headroom and a ceiling, and on each
///     side a wall, a doorway, another corridor cell or an opening onto walkable floor; never a
///     hole into the void;
///   - every doorway sits in a wall (its jambs are solid) with walkable floor on both sides;
///   - every stairwell connects the storey it starts on to the one above;
///   - from the way in, every room, corridor, stair and lift stop is reachable (hidden rooms
///     once their wall breaks);
///   - no block floats, no two modules share a cell.
/// </summary>
public static class LayoutCheck
{
    public static List<string> Check(Layout layout)
    {
        var problems = new List<string>();
        var g = layout.Blocks;
        var x = layout.Extras;
        var nav = new NavGrid(g, layout.Occupied, x.Lifts, x.InSpace);

        // Overlaps.
        var taken = new HashSet<Int3>();
        foreach (var m in layout.Modules)
            foreach (Int3 c in m.Cells)
                if (!taken.Add(c)) { problems.Add($"module {m.Purpose} overlaps another at {c}"); goto overlapsDone; }
        overlapsDone:

        var doorCells = new HashSet<Int3>(layout.Doorways.SelectMany(d => d.Cells).Concat(layout.ExitCells));
        // Corridors: floor, headroom, ceiling, and closed sides.
        foreach (Int3 c in x.CorridorCells)
        {
            var below = g.Get(c + Int3.Down);
            bool floor = below.IsAir ? nav.IsRock(c + Int3.Down) : BlockInfo.Supports(below.Type) || BlockInfo.IsSlope(below.Type);
            if (!floor) { problems.Add($"corridor cell {c} has no floor"); break; }
            if (nav.Solid(c) || nav.Solid(c + Int3.Up) || nav.Solid(c + Int3.Up * 2)) { problems.Add($"corridor cell {c} lacks headroom"); break; }
            var ceil = g.Get(c + Int3.Up * 3);
            if (ceil.IsAir && !nav.IsRock(c + Int3.Up * 3) && !doorCells.Contains(c + Int3.Up * 2)) { problems.Add($"corridor cell {c} is open to the sky"); break; }
            foreach (Dir h in Dirs.Horizontal)
            {
                Int3 n = c + h.Vec();
                if (x.CorridorCells.Contains(n) || doorCells.Contains(n)) continue;
                if (nav.Solid(n) && nav.Solid(n + Int3.Up) && nav.Solid(n + Int3.Up * 2)) continue; // a wall
                if (nav.Standable(n) && layout.Occupied[n]) continue;                              // an opening onto the building's floor
                if (nav.LadderAt(n) || nav.LiftAt(n)) continue;
                if (g.Get(n).Type == BlockType.Rail) continue;                                       // a gallery's rail over a void
                if (BlockInfo.IsSlope(g.Get(n).Type)) continue;
                problems.Add($"corridor cell {c} opens into the void to its {h}");
                goto corridorsDone;
            }
        }
        corridorsDone:

        // Doorways: in a wall, floor both sides.
        foreach (var d in layout.Doorways)
        {
            Int3 lo = d.Cells.Aggregate(Int3.Min), hi = d.Cells.Aggregate(Int3.Max);
            Int3 across = d.Facing is Dir.North or Dir.South ? new Int3(1, 0, 0) : new Int3(0, 0, 1);
            Int3 jamb1 = lo - across, jamb2 = hi + across;
            jamb1 = jamb1 with { Y = lo.Y };
            jamb2 = jamb2 with { Y = lo.Y };
            if (!nav.Solid(jamb1) && !nav.Solid(jamb2)) { problems.Add($"doorway at {lo} isn't set in a wall ({layout.Modules[d.ModuleA].Purpose} to {layout.Modules[d.ModuleB].Purpose}, facing {d.Facing}, jambs {g.Get(jamb1)} {g.Get(jamb2)}, {d.Cells.Count} cells)"); break; }
            Int3 inSide = lo + d.Facing.Vec(), outSide = lo - d.Facing.Vec();
            if (!nav.Standable(inSide) && !nav.LiftAt(inSide)) { problems.Add($"doorway at {lo} opens onto no floor (inside {inSide})"); break; }
            if (!nav.Standable(outSide) && !nav.LiftAt(outSide)) { problems.Add($"doorway at {lo} opens onto no floor (outside {outSide})"); break; }
        }

        // Stairs and ramps never clip: three clear cells over every slope a body walks.
        foreach (var (p, b) in g.Blocks())
        {
            if (!BlockInfo.IsSlope(b.Type) || !layout.Occupied[p + Int3.Up] && p.Y >= 0) continue;
            if (nav.Solid(p + Int3.Up) || nav.Solid(p + Int3.Up * 2) || nav.Solid(p + Int3.Up * 3)) { problems.Add($"stair at {p} has less than three cells of headroom"); break; }
        }

        // Reachability: every module (hidden rooms aside) has a cell you can stand in and reach.
        if (!nav.Standable(layout.EntranceCell)) { problems.Add($"can't stand at the way in {layout.EntranceCell}"); return problems; }
        var reach = nav.Reachable(layout.EntranceCell);
        var hidden = new HashSet<int>(x.Secrets.Select(s => s.Module));
        foreach (var m in layout.Modules)
        {
            if (hidden.Contains(m.Index) || m.Shape.Occupied.Count == 0) continue;
            if (!m.Cells.Any(reach.Contains)) { problems.Add($"{m.Purpose} (storey {m.Storey}) at {m.Min} is unreachable from the way in"); break; }
        }
        // Stairwells join their storeys: the next storey up is reached through the stair.
        foreach (var lvl in x.Levels)
            if (!reach.Any(c => c.Y == lvl.WalkY)) { problems.Add($"storey {lvl.Name} can't be reached"); break; }

        // Hidden rooms open when their wall breaks.
        foreach (var s in x.Secrets)
        {
            var room = layout.Modules[s.Module];
            if (room.Cells.Any(reach.Contains)) { problems.Add($"hidden room at {room.Min} is reachable without breaking in"); continue; }
            var broken = g.Clone();
            foreach (Int3 w in s.WallCells) broken.Set(w, Block.Air);
            var reach2 = new NavGrid(broken, layout.Occupied, x.Lifts, x.InSpace).Reachable(layout.EntranceCell);
            if (!room.Cells.Any(reach2.Contains)) problems.Add($"hidden room at {room.Min} stays shut after its wall breaks");
        }

        // Breaches are alternative ways in: from the lip, down the slope, into the room.
        foreach (var b in x.Breaches)
        {
            var fromLip = nav.Reachable(b.SurfaceCell);
            if (!layout.Modules[b.Module].Cells.Any(fromLip.Contains)) problems.Add($"breach at {b.SurfaceCell} doesn't lead into its room");
        }

        var floating = Structure.Floating(g, layout.Occupied, x.InSpace ? StationAnchors(layout) : null);
        if (floating.Count > 0) problems.Add($"{floating.Count} floating blocks, first at {floating[0]} ({g.Get(floating[0])})");
        return problems;
    }

    /// <summary>In space nothing rests on the ground: a station hangs together from its hub.</summary>
    public static List<Int3> StationAnchors(Layout layout) =>
        layout.Modules.Count == 0 ? new List<Int3>() : layout.Modules[0].Cells.Select(c => c + Int3.Down).ToList();
}
