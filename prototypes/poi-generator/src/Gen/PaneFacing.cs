#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;

namespace PoiGenerator.Gen;

/// <summary>
/// Which way each pane faces, settled once when the place is laid out and kept on the block. A pane is a thin
/// sheet in the plane of the wall it is set in; its <see cref="Block.Facing"/> is that wall's outward normal, so
/// north or south for a sheet that runs along x and east or west for one that runs along z. The mesher draws what
/// the block says (<see cref="Shapes.PaneAlongX"/>) and never reads the cells round it: it used to guess from
/// whether anything stood east or west of the pane, and was fooled by every wall that runs north to south and is two
/// cells thick (each layer's pane has its partner east or west of it), by every partition that meets a window, and by
/// every ruin that takes the wall from round a pane which stood. The builders only put panes where they want glass.
/// <para>The wall's run is read from the frame: a pane is held at the two ends of its sheet by the wall (or the post)
/// it is set in, while across it is open air, the room, or its other layer. Where the cells either side along both
/// axes are held (a partition meets the wall) or neither is (a lone sheet), the panes that are plain decide for it,
/// then the longer line of solids through it.</para>
/// <para>The panes of a wall that is several cells thick are one window: they share their facing, and only the
/// outermost of them is drawn (<see cref="Shapes.PaneHidden"/>), so a window is one sheet at one depth.</para>
/// </summary>
public static class PaneFacing
{
    /// <summary>How far a line of solids is followed to tell which way a wall runs, cells.</summary>
    const int Reach = 24;

    /// <summary>How far the air outside a wall is looked at to tell which side is out, cells.</summary>
    const int Look = 8;

    public static void Apply(BlockGrid g)
    {
        var panes = new List<Int3>();
        foreach (var (p, b) in g.Blocks())
            if (b.Type == BlockType.Pane && b.Role != Role.Prop) panes.Add(p);
        if (panes.Count == 0) return;

        // The plain ones first: held at both ends along one axis only.
        var run = new Dictionary<Int3, int>(panes.Count);
        var open = new List<Int3>();
        foreach (Int3 p in panes)
        {
            bool hx = Held(g, p, Dir.East, Dir.West), hz = Held(g, p, Dir.North, Dir.South);
            if (hx != hz) run[p] = hx ? 0 : 1;
            else open.Add(p);
        }
        // The rest follow the plain panes beside them (a partner in the next layer, the panes along the wall), and failing
        // that the longer line of solids through them; a pane that has neither runs along x.
        var settled = new Dictionary<Int3, int>(open.Count);
        foreach (Int3 p in open)
        {
            int[] votes = new int[2];
            foreach (Dir d in Dirs.Horizontal)
                if (run.TryGetValue(p + d.Vec(), out int axis)) votes[axis]++;
            int axisOf = votes[0] != votes[1] ? (votes[0] > votes[1] ? 0 : 1) : Longer(g, p);
            settled[p] = axisOf < 0 ? 0 : axisOf;
        }
        foreach (var (p, axis) in settled) run[p] = axis;

        // Which side is out is a window's to say, not each cell's: the panes that touch in a wall that runs one way vote
        // on it by what lies beyond the stack they stand in, and all of them face as the vote goes.
        var vote = new Dictionary<Int3, int>(panes.Count);
        var parent = new Dictionary<Int3, Int3>(panes.Count);
        foreach (Int3 p in panes) { vote[p] = Side(g, p, run); parent[p] = p; }
        Int3 Root(Int3 c) { while (parent[c] != c) c = parent[c] = parent[parent[c]]; return c; }
        foreach (Int3 p in panes)
            foreach (Int3 d in new[] { new Int3(1, 0, 0), new Int3(0, 1, 0), new Int3(0, 0, 1) })
                if (run.TryGetValue(p + d, out int axis) && axis == run[p] && parent.ContainsKey(p + d)) parent[Root(p)] = Root(p + d);
        var total = new Dictionary<Int3, int>();
        foreach (Int3 p in panes) total[Root(p)] = total.GetValueOrDefault(Root(p)) + vote[p];
        foreach (Int3 p in panes)
        {
            Dir plus = run[p] == 0 ? Dir.South : Dir.East;
            Block b = g.Get(p);
            b.Facing = total[Root(p)] >= 0 ? plus : plus.Opposite();
            g.Set(p, b);
        }
    }

    static bool Held(BlockGrid g, Int3 p, Dir a, Dir b) => !g.Get(p + a.Vec()).IsAir && !g.Get(p + b.Vec()).IsAir;

    /// <summary>The axis (0 along x, 1 along z) with the longer line of solid cells through the cell, -1 if they are as long.</summary>
    static int Longer(BlockGrid g, Int3 p)
    {
        int Line(Dir d)
        {
            int n = 0;
            while (n < Reach && !g.Get(p + d.Vec() * (n + 1)).IsAir) n++;
            return n;
        }
        int x = Line(Dir.East) + Line(Dir.West), z = Line(Dir.North) + Line(Dir.South);
        return x == z ? -1 : x > z ? 0 : 1;
    }

    /// <summary>
    /// Which side of its wall a cell's stack of panes says is out: +1 for south or east, -1 for north or west, 0 for neither. The
    /// stack is followed to its ends, and the side with the sky over the air beyond it is out; failing that, the side with more
    /// open air.
    /// </summary>
    static int Side(BlockGrid g, Int3 p, Dictionary<Int3, int> run)
    {
        int axis = run[p];
        Dir plus = axis == 0 ? Dir.South : Dir.East;
        int Air(Dir d)
        {
            Int3 end = p;
            for (int k = 0; k < Look && run.TryGetValue(end + d.Vec(), out int a) && a == axis; k++) end += d.Vec();
            int n = 0;
            while (n < Look && g.Get(end + d.Vec() * (n + 1)).IsAir) n++;
            // The sky over the first cell of air beyond the stack counts for more than any depth of it: that is the outside.
            bool sky = n > 0 && SkyOver(g, end + d.Vec());
            return n + (sky ? 1000 : 0);
        }
        return Math.Sign(Air(plus) - Air(plus.Opposite()));
    }

    static bool SkyOver(BlockGrid g, Int3 c)
    {
        int top = g.Min.Y + g.Size.Y;
        for (int y = c.Y + 1; y < top; y++)
            if (BlockInfo.Collides(g.Get(c with { Y = y }).Type)) return false;
        return true;
    }
}
