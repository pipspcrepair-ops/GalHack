#nullable enable
using System.Collections.Generic;
using System.Linq;

namespace PoiGenerator.Gen;

/// <summary>
/// Loose things come to rest, whoever set them down (a builder, the fixtures, the years, the occupants): the last
/// word on a spawned place before it is turned and stamped. Nothing floats and nothing is set into the rock.
/// <list type="bullet">
/// <item>Something that stands on the floor of its cell (a crate, a table, rubble, a bed, a pod) with nothing under
/// it drops straight down onto the first thing that holds it, if the way down is clear; where it isn't, or it would
/// fall out of the place, it goes. The plan's caches, terminals and core are never moved.</item>
/// <item>Below ground, loose things lie only in the place's own dug cells: debris left in the rock (a wall below
/// ground broken into rubble where there was rock behind it) goes.</item>
/// </list>
/// </summary>
public static class Settle
{
    /// <summary>How far a thing may fall to its rest, cells; further and it goes.</summary>
    const int MaxDrop = 24;

    /// <summary>Settles a spawned place.</summary>
    public static void Apply(PoiInstance inst)
    {
        BlockGrid g = inst.Blocks;
        Layout layout = inst.Layout;
        // A lintel only a broken door held up comes down with it (the repair runs only where a door went from under one).
        if (Doors(inst) is var lintels && lintels.Count > 0 && lintels.Any(c => !g.Get(c).IsAir))
            foreach (Int3 p in Structure.Floating(g, layout.Occupied, layout.Extras.InSpace ? LayoutCheck.StationAnchors(layout) : null, layout.Extras.Plain.Count > 0)) g.Set(p, Block.Air);
        var nav = inst.Nav();
        // A lift's shaft is a way, not a floor: what would fall down one goes instead.
        var shafts = layout.Extras.Lifts.SelectMany(l => l.Footprint).Select(c => (c.X, c.Z)).ToHashSet();
        // Bottom up, so a stack settles from its foot.
        // (Walls, floors and roofs are most of a place and never move: only what may is listed.)
        foreach (var (p, b) in g.Blocks().Where(x => PlaceAudit.Rests(x.Block.Type) || BlockInfo.IsFinish(x.Block.Type) || Loose(x.Block)).ToList())
        {
            if (g.Get(p).Type != b.Type) continue; // already moved or taken
            if (nav.IsRock(p) && Loose(b))
            {
                g.Set(p, Block.Air);
                continue;
            }
            // A floor's finish is paint on the block under it: where that block went, so does the paint.
            if (BlockInfo.IsFinish(b.Type))
            {
                if (!Painted(g, nav, p + Int3.Down)) g.Set(p, Block.Air);
                continue;
            }
            // The plan's own caches, terminals and core are kept by cell and checked by the validator's walk: never moved here.
            if (!PlaceAudit.Rests(b.Type) || b.Type is BlockType.Cache or BlockType.Terminal or BlockType.Core || Holds(g, nav, p + Int3.Down)) continue;
            Int3 to = p;
            int fall = 0;
            while (fall < MaxDrop && Open(g, nav, to + Int3.Down) && !Holds(g, nav, to + Int3.Down))
            {
                to += Int3.Down;
                fall++;
            }
            g.Set(p, Block.Air);
            if (fall < MaxDrop && to != p && Open(g, nav, to) && Holds(g, nav, to + Int3.Down) && !shafts.Contains((to.X, to.Z))) g.Set(to, b);
        }
    }

    /// <summary>A doorway's door as it stands: its door cells, which way the doorway faces, its box, and the cells at its leaf's two ends.</summary>
    public readonly record struct DoorShape(List<Int3> Cells, Dir Facing, Int3 Lo, Int3 Hi, int W, int H, int D, Int3 EndA, Int3 EndB);

    /// <summary>Every doorway with a door in it (the way in too), each door cell counted once.</summary>
    public static IEnumerable<DoorShape> DoorsOf(Layout layout, BlockGrid g)
    {
        var seen = new HashSet<Int3>();
        foreach (var (cells, facing) in layout.Doorways.Select(d => (d.Cells, d.Facing)).Append((layout.ExitCells, layout.EntranceFacing)))
        {
            var doors = cells.Where(c => g.Get(c).Type == BlockType.Door && seen.Add(c)).ToList();
            if (doors.Count == 0) continue;
            Int3 lo = doors.Aggregate(Int3.Min), hi = doors.Aggregate(Int3.Max);
            bool alongX = facing is Dir.North or Dir.South;
            int ex = hi.X - lo.X + 1, ez = hi.Z - lo.Z + 1;
            Int3 side = alongX ? new Int3(1, 0, 0) : new Int3(0, 0, 1);
            yield return new DoorShape(doors, facing, lo, hi, alongX ? ex : ez, hi.Y - lo.Y + 1, alongX ? ez : ex, lo - side, (hi with { Y = lo.Y }) + side);
        }
    }

    /// <summary>
    /// A door is a whole door in a whole wall over a floor, or it isn't there: where the years broke a doorway's leaves into
    /// pieces (a lone 1 m leaf, a strip a metre high), took the wall from either end of it or the floor from under it, what
    /// is left of the door goes and the doorway is a gap.
    /// </summary>
    static List<Int3> Doors(PoiInstance inst)
    {
        var over = new List<Int3>();
        var nav = inst.Nav();
        BlockGrid g = inst.Blocks;
        Layout layout = inst.Layout;
        // What holds a leaf at its ends: a wall, a post, the next door's frame, the rock.
        bool Held(Int3 p) => PieceFacing.Wall(g, layout.Occupied, p, layout.Extras.InSpace) || g.Get(p).Type is BlockType.Door;
        foreach (DoorShape d in DoorsOf(layout, g).ToList())
        {
            bool whole = d.H >= 2 && d.Cells.Count == d.W * d.H * d.D;
            if (whole && Held(d.EndA) && Held(d.EndB) && d.Cells.Where(c => c.Y == d.Lo.Y).All(c => Holds(g, nav, c + Int3.Down))) continue;
            foreach (Int3 c in d.Cells) g.Set(c, Block.Air);
            over.AddRange(d.Cells.Where(c => c.Y == d.Hi.Y).Select(c => c + Int3.Up));
        }
        return over;
    }

    /// <summary>What isn't built: debris, props, growth.</summary>
    static bool Loose(Block b) => b.Role is Role.Debris or Role.Prop or Role.Growth;

    /// <summary>What a floor's finish can be painted on: the top of a full block or a lattice, or the ground.</summary>
    public static bool Painted(BlockGrid g, NavGrid nav, Int3 below)
    {
        Block b = g.Get(below);
        return b.IsAir ? nav.IsRock(below) : BlockInfo.IsFullCube(b.Type) || b.Type == BlockType.Lattice;
    }

    /// <summary>A cell a thing can fall through: air or a floor's finish, not the rock.</summary>
    static bool Open(BlockGrid g, NavGrid nav, Int3 p)
    {
        if (!g.InBounds(p)) return false;
        Block b = g.Get(p);
        return (b.IsAir || BlockInfo.IsFinish(b.Type)) && !nav.IsRock(p);
    }

    /// <summary>What a thing can rest on: a floor, the ground, a slope, a table or a bench, a machine, a heap, a growth.</summary>
    public static bool Holds(BlockGrid g, NavGrid nav, Int3 below)
    {
        Block b = g.Get(below);
        return b.IsAir ? nav.IsRock(below) : BlockInfo.Supports(b.Type) || BlockInfo.IsSlope(b.Type) || BlockInfo.IsCornerSlope(b.Type)
            || b.Type is BlockType.Table or BlockType.Meal or BlockType.Bench or BlockType.Machine or BlockType.Tank or BlockType.Shelf or BlockType.Rubble
                or BlockType.Growth or BlockType.Bunk or BlockType.Cache;
    }
}
