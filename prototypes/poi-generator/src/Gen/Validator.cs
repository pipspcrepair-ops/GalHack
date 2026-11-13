#nullable enable
using System.Collections.Generic;
using System.Linq;

namespace PoiGenerator.Gen;

/// <summary>
/// The rules every spawn must keep:
///   - from the way in, grid A* reaches the vault, the core (if there is one), every cache,
///     terminal, and every key room; hidden stashes only once their wall breaks
///   - the deepest point is reachable
///   - no block hangs in the air
///   - no two modules share a cell
///   - a core exactly where a faction holds the place, and none anywhere else
/// </summary>
public static class Validator
{
    public static List<string> Check(PoiInstance inst)
    {
        var problems = new List<string>();
        var layout = inst.Layout;
        var g = inst.Blocks;
        var nav = inst.Nav();

        // Overlaps: no cell may belong to two modules.
        var taken = new HashSet<Int3>();
        foreach (var m in layout.Modules)
            foreach (Int3 c in m.Cells)
                if (!taken.Add(c))
                {
                    problems.Add($"module {m} overlaps another at {c}");
                    goto doneOverlaps;
                }
        doneOverlaps:

        // The core, only where someone holds the place.
        int cores = 0;
        foreach (var (_, b) in g.Blocks()) if (b.Type == BlockType.Core) cores++;
        if (inst.HasCore && (cores != 1 || g.Get(inst.CoreCell).Type != BlockType.Core))
            problems.Add($"expected one core at {inst.CoreCell}, found {cores}");
        if (!inst.HasCore && cores != 0) problems.Add($"expected no core ({layout.Family.Id} {inst.State}), found {cores}");

        // Reachability.
        if (!nav.Standable(layout.EntranceCell))
        {
            problems.Add($"can't stand at the way in {layout.EntranceCell}");
            return problems;
        }
        var reach = nav.Reachable(layout.EntranceCell);

        var vaultCache = inst.Caches.FirstOrDefault(c => c.InVault);
        if (vaultCache == null) problems.Add("no vault cache");
        else if (nav.AccessCell(vaultCache.Cell, reach) is not Int3)
            problems.Add("the way in doesn't reach the vault");

        if (inst.HasCore && nav.AccessCell(inst.CoreCell, reach) is not Int3)
            problems.Add("the way in doesn't reach the core");

        foreach (var cache in inst.Caches.Where(c => !c.Secret))
            if (nav.AccessCell(cache.Cell, reach) == null) problems.Add($"cache at {cache.Cell} unreachable");
        foreach (var t in inst.Terminals)
            if (nav.AccessCell(t.Cell, reach) == null) problems.Add($"terminal at {t.Cell} unreachable");
        foreach (int k in layout.KeyModules)
            if (Fixtures.RoomCell(layout.Modules[k], reach) == null)
                problems.Add($"key room {layout.Modules[k]} unreachable");

        // The deepest point.
        int deepest = layout.DeepestModule;
        if (deepest >= 0 && Fixtures.RoomCell(layout.Modules[deepest], reach) == null)
            problems.Add($"the deepest point ({layout.Modules[deepest].Purpose}) is unreachable");

        // Floating sections.
        var anchors = layout.Extras.InSpace ? LayoutCheck.StationAnchors(layout) : null;
        var floating = Structure.Floating(g, layout.Occupied, anchors);
        if (floating.Count > 0) problems.Add($"{floating.Count} floating blocks, first at {floating[0]} ({g.Get(floating[0])})");
        // Cables, wires and tendrils always hang from something.
        var loose = Structure.Untethered(g);
        if (loose.Count > 0) problems.Add($"{loose.Count} cables or tendrils hang from nothing, first at {loose[0]}");

        return problems;
    }
}
