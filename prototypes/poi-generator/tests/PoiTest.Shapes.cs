using System;
using System.Linq;
using PoiGenerator.Gen;

namespace PoiGenerator;

public partial class PoiTest
{
    /// <summary>
    /// The manner and kind a place's shape shares are judged by (data/references.txt "shapes"): high tech (and alien)
    /// chamfers, industry rounder; low and mid tech pitch the roofs of houses, farms and settlements, timber mines, and
    /// build the rest flat-roofed.
    /// </summary>
    static string ShapeKind(PoiInstance inst)
    {
        var tags = inst.Layout.Family.Tags;
        bool Any(params string[] t) => t.Any(tags.Contains);
        if (Shaping.High(inst)) return Any("industry", "utility", "machine", "power") ? "high industry" : "high base";
        if (Any("mine", "ore")) return "low mine";
        return Any("house", "farm", "farmstead", "settlement", "shed", "homestead") ? "low pitched" : "low flat";
    }

    /// <summary>
    /// A place's shapes by class into <paramref name="by"/>, over the blocks it shows: its structure (and stairs) with a
    /// face to the air or to what doesn't fill its cell. Buried fill (the core of a thick wall, a slab under a slab) is
    /// left out, as the games' own tallies leave out rock. Returns how many were counted.
    /// </summary>
    static long CountShapes(PoiInstance inst, long[] by)
    {
        BlockGrid g = inst.Blocks;
        var occ = inst.Layout.Occupied;
        bool space = inst.Layout.Extras.InSpace;
        bool Open(Int3 q)
        {
            Block n = g.Get(q);
            return n.IsAir ? space || q.Y > Assembler.GroundY || occ[q] : !BlockInfo.IsFullCube(n.Type);
        }
        long seen = 0;
        foreach (var (c, b) in g.Blocks())
        {
            if (!BlockInfo.IsStructure(b.Role) && b.Type != BlockType.Stair || BlockInfo.IsFinish(b.Type) || !Dirs.All.Any(d => Open(c + d.Vec()))) continue;
            by[(int)BlockInfo.ShapeOf(b)]++;
            seen++;
        }
        return seen;
    }
}
