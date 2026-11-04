#nullable enable
using System.Collections.Generic;
using System.Linq;

namespace PoiGenerator.Gen;

/// <summary>
/// The rules every spawn must keep:
///   - from the way in, grid A* reaches the vault, the core, every cache, every terminal and
///     every key room
///   - no block hangs in the air
///   - no two modules share a cell
///   - there is exactly one core, where the layout says
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
        var taken = new CellMask(g.Min, g.Size);
        foreach (var m in layout.Modules)
            foreach (Int3 c in m.Cells)
            {
                if (taken[c])
                {
                    problems.Add($"module {m} overlaps another at {c}");
                    break;
                }
                taken[c] = true;
            }

        // The core.
        int cores = 0;
        foreach (var (_, b) in g.Blocks()) if (b.Type == BlockType.Core) cores++;
        if (cores != 1 || g.Get(inst.CoreCell).Type != BlockType.Core)
            problems.Add($"expected one core at {inst.CoreCell}, found {cores}");

        // Reachability.
        if (!nav.Standable(layout.EntranceCell))
        {
            problems.Add($"can't stand at the way in {layout.EntranceCell}");
            return problems;
        }
        var reach = nav.Reachable(layout.EntranceCell);

        var vaultCache = inst.Caches.FirstOrDefault(c => c.InVault);
        if (vaultCache == null) problems.Add("no vault cache");
        else if (nav.AccessCell(vaultCache.Cell, reach) is not Int3 va || nav.FindPath(layout.EntranceCell, va) == null)
            problems.Add("A* can't reach the vault");

        if (nav.AccessCell(inst.CoreCell, reach) is not Int3 ca || nav.FindPath(layout.EntranceCell, ca) == null)
            problems.Add("A* can't reach the core");

        foreach (var cache in inst.Caches)
            if (nav.AccessCell(cache.Cell, reach) == null) problems.Add($"cache at {cache.Cell} unreachable");
        foreach (var t in inst.Terminals)
            if (nav.AccessCell(t.Cell, reach) == null) problems.Add($"terminal at {t.Cell} unreachable");
        foreach (int k in layout.KeyModules)
            if (Fixtures.RoomCell(layout.Modules[k], reach) == null)
                problems.Add($"key room {layout.Modules[k]} unreachable");

        // Floating sections.
        var floating = Structure.Floating(g, layout.Occupied);
        if (floating.Count > 0) problems.Add($"{floating.Count} floating blocks, first at {floating[0]} ({g.Get(floating[0])})");

        return problems;
    }
}
