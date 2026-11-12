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
/// fall out of the place, it goes. Kept records (caches, terminals, the core) are never moved: they are the plan's,
/// and their checks are the validator's.</item>
/// <item>Below ground, loose things lie only in the place's own dug cells: debris left in the rock (a wall below
/// ground broken into rubble where there was rock behind it) goes.</item>
/// </list>
/// </summary>
public static class Settle
{
    /// <summary>How far a thing may fall to its rest, cells; further and it goes.</summary>
    const int MaxDrop = 24;

    /// <summary>Settles a spawned place; returns how many things moved and how many went.</summary>
    public static (int Moved, int Removed) Apply(PoiInstance inst)
    {
        BlockGrid g = inst.Blocks;
        Doors(inst);
        var nav = inst.Nav();
        int moved = 0, removed = 0;
        // Bottom up, so a stack settles from its foot.
        foreach (var (p, b) in g.Blocks().ToList())
        {
            if (g.Get(p).Type != b.Type) continue; // already moved or taken
            if (nav.IsRock(p) && Loose(b))
            {
                g.Set(p, Block.Air);
                removed++;
                continue;
            }
            // A floor's finish is paint on the block under it: where that block went, so does the paint.
            if (BlockInfo.IsFinish(b.Type))
            {
                if (!Painted(g, nav, p + Int3.Down)) { g.Set(p, Block.Air); removed++; }
                continue;
            }
            if (!PlaceAudit.Rests(b.Type) || Kept(b) || Holds(g, nav, p + Int3.Down)) continue;
            Int3 to = p;
            int fall = 0;
            while (fall < MaxDrop && Open(g, nav, to + Int3.Down) && !Holds(g, nav, to + Int3.Down))
            {
                to += Int3.Down;
                fall++;
            }
            g.Set(p, Block.Air);
            if (fall < MaxDrop && to != p && Open(g, nav, to) && Holds(g, nav, to + Int3.Down))
            {
                g.Set(to, b);
                moved++;
            }
            else removed++;
        }
        return (moved, removed);
    }

    /// <summary>
    /// A door is a whole door in a whole wall, or it isn't there: where the years broke a doorway's leaves into pieces
    /// (a lone 1 m leaf, a strip a metre high) or took the wall from either end of it, what is left of the door goes,
    /// and the doorway is a gap. Returns how many door cells went.
    /// </summary>
    public static int Doors(PoiInstance inst)
    {
        BlockGrid g = inst.Blocks;
        var nav = inst.Nav();
        Layout layout = inst.Layout;
        var groups = layout.Doorways.Select(d => (d.Cells, d.Facing)).ToList();
        groups.Add((layout.ExitCells, layout.EntranceFacing));
        int gone = 0;
        // What holds a door's leaf at its ends: a wall, a post, the next door's frame, the rock.
        bool Held(Int3 p) => PieceFacing.Wall(g, layout.Occupied, p, layout.Extras.InSpace) || g.Get(p).Type is BlockType.Door;
        foreach (var (cells, facing) in groups)
        {
            var doors = cells.Where(c => g.Get(c).Type == BlockType.Door).Distinct().ToList();
            if (doors.Count == 0) continue;
            Int3 lo = doors.Aggregate(Int3.Min), hi = doors.Aggregate(Int3.Max);
            bool alongX = facing is Dir.North or Dir.South;
            int w = alongX ? hi.X - lo.X + 1 : hi.Z - lo.Z + 1, d = alongX ? hi.Z - lo.Z + 1 : hi.X - lo.X + 1, h = hi.Y - lo.Y + 1;
            Int3 endA = alongX ? lo + new Int3(-1, 0, 0) : lo + new Int3(0, 0, -1);
            Int3 endB = alongX ? hi with { Y = lo.Y } + new Int3(1, 0, 0) : hi with { Y = lo.Y } + new Int3(0, 0, 1);
            bool whole = h >= 2 && doors.Count == w * h * d;
            // ...and with floor under its foot (where the years took the floor from under a doorway, the door went with it).
            bool walled = Held(endA) && Held(endB) && doors.Where(c => c.Y == lo.Y).All(c => Holds(g, nav, c + Int3.Down));
            if (whole && walled) continue;
            if (!whole) System.Threading.Interlocked.Increment(ref Broken);
            else
            {
                System.Threading.Interlocked.Increment(ref Unwalled);
                lock (Ends) { Ends[Name(g.Get(endA))] = Ends.GetValueOrDefault(Name(g.Get(endA))) + 1; Ends[Name(g.Get(endB))] = Ends.GetValueOrDefault(Name(g.Get(endB))) + 1; }
            }
            foreach (Int3 c in doors) g.Set(c, Block.Air);
            gone += doors.Count;
        }
        return gone;
    }

    /// <summary>Doors taken out as broken or as left with no wall at an end, and what stood at the ends (tests read them).</summary>
    public static int Broken, Unwalled;
    public static readonly Dictionary<string, int> Ends = new();
    static string Name(Block b) => b.IsAir ? "air" : b.Type.ToString();

    /// <summary>The plan's own things, recorded by cell: never moved here.</summary>
    static bool Kept(Block b) => b.Type is BlockType.Cache or BlockType.Terminal or BlockType.Core || b.Role is Role.Loot or Role.Core or Role.Terminal or Role.Story;

    /// <summary>What isn't built: debris, props, growth.</summary>
    static bool Loose(Block b) => b.Role is Role.Debris or Role.Prop or Role.Growth && !Kept(b);

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
