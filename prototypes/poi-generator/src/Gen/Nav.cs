#nullable enable
using System.Collections.Generic;

namespace PoiGenerator.Gen;

/// <summary>
/// Walking on the block grid, for validation and for enemies: plain A* over cells a 1.8 m body
/// can stand in. A node is the cell your feet are in. You stand in a clear cell with a clear
/// cell above and something solid below, or on a ramp. Moves are one cell sideways at the same
/// height, or up and down ramps along their slope; no jumping or dropping, so every path can
/// be walked back.
///
/// Below ground level, any cell no module occupies is rock.
/// </summary>
public sealed class NavGrid
{
    readonly IBlockStructure _grid;
    readonly CellMask _occupied;

    public NavGrid(IBlockStructure grid, CellMask occupied)
    {
        _grid = grid;
        _occupied = occupied;
    }

    public bool IsRock(Int3 p) => p.Y <= Assembler.GroundY && !_occupied[p];

    public bool Solid(Int3 p)
    {
        var b = _grid.Get(p);
        return b.IsAir ? IsRock(p) : BlockInfo.Collides(b.Type);
    }

    bool Clear(Int3 p) => !Solid(p);

    bool SupportsStanding(Int3 p)
    {
        var b = _grid.Get(p);
        return b.IsAir ? IsRock(p) : BlockInfo.Supports(b.Type);
    }

    public bool RampAt(Int3 p, out Dir rises)
    {
        var b = _grid.Get(p);
        rises = b.Facing;
        return b.Type == BlockType.Ramp;
    }

    public bool Standable(Int3 f)
    {
        // Outside the grid the plain goes on for ever; walking is limited to the place itself.
        if (!_grid.InBounds(f)) return false;
        if (RampAt(f, out _)) return Clear(f + Int3.Up) && Clear(f + Int3.Up * 2);
        return Clear(f) && Clear(f + Int3.Up) && SupportsStanding(f + Int3.Down);
    }

    public List<Int3> Neighbours(Int3 f)
    {
        var list = new List<Int3>(4);
        NeighboursInto(f, list);
        return list;
    }

    /// <summary>Fills a reused list, so searches don't allocate per step.</summary>
    public void NeighboursInto(Int3 f, List<Int3> into)
    {
        into.Clear();
        bool onRamp = RampAt(f, out Dir rampDir);
        foreach (Dir h in Dirs.Horizontal)
        {
            Int3 g = f + h.Vec();
            if (Standable(g))
            {
                bool toRamp = RampAt(g, out Dir gDir);
                bool ok = (onRamp, toRamp) switch
                {
                    (false, false) => true,
                    (false, true) => h == gDir,                         // onto a ramp from its foot
                    (true, false) => h == rampDir.Opposite(),           // off a ramp at its foot
                    (true, true) => gDir == rampDir && h != rampDir && h != rampDir.Opposite(), // across a wide ramp
                };
                if (ok) into.Add(g);
            }
            // Up a ramp and off its top.
            if (onRamp && h == rampDir)
            {
                Int3 up = g + Int3.Up;
                if (Standable(up) && (!RampAt(up, out Dir upDir) || upDir == rampDir)) into.Add(up);
            }
            // Down onto a ramp whose top is here.
            Int3 down = g + Int3.Down;
            if (RampAt(down, out Dir dDir) && dDir == h.Opposite() && Standable(down) && (!onRamp || rampDir == dDir))
                into.Add(down);
        }
    }

    /// <summary>Plain A* with a Manhattan heuristic. Returns null if there is no path.</summary>
    public List<Int3>? FindPath(Int3 start, Int3 goal, int maxNodes = 200_000)
    {
        if (!Standable(start) || !Standable(goal)) return null;
        var open = new PriorityQueue<Int3, int>();
        var cost = new Dictionary<Int3, int> { [start] = 0 };
        var from = new Dictionary<Int3, Int3>();
        open.Enqueue(start, start.Manhattan(goal));
        int expanded = 0;
        var next = new List<Int3>(8);
        while (open.TryDequeue(out Int3 cur, out _))
        {
            if (cur == goal) return Rebuild(from, cur);
            if (++expanded > maxNodes) return null;
            int c = cost[cur];
            NeighboursInto(cur, next);
            foreach (Int3 n in next)
            {
                int nc = c + 1;
                if (cost.TryGetValue(n, out int old) && old <= nc) continue;
                cost[n] = nc;
                from[n] = cur;
                open.Enqueue(n, nc + n.Manhattan(goal));
            }
        }
        return null;
    }

    static List<Int3> Rebuild(Dictionary<Int3, Int3> from, Int3 cur)
    {
        var path = new List<Int3> { cur };
        while (from.TryGetValue(cur, out Int3 prev))
        {
            path.Add(prev);
            cur = prev;
        }
        path.Reverse();
        return path;
    }

    /// <summary>Every cell reachable on foot from start (breadth first).</summary>
    public HashSet<Int3> Reachable(Int3 start) => new(Tree(start).Keys);

    /// <summary>
    /// A breadth-first tree from start: each reachable cell maps to the cell it was reached
    /// from. Every move costs the same, so walking the tree back gives a shortest path, the
    /// same length A* finds, for every target at once.
    /// </summary>
    public Dictionary<Int3, Int3> Tree(Int3 start)
    {
        var from = new Dictionary<Int3, Int3>();
        if (!Standable(start)) return from;
        var queue = new Queue<Int3>();
        queue.Enqueue(start);
        from[start] = start;
        var next = new List<Int3>(8);
        while (queue.Count > 0)
        {
            Int3 cur = queue.Dequeue();
            NeighboursInto(cur, next);
            foreach (Int3 n in next)
                if (from.TryAdd(n, cur)) queue.Enqueue(n);
        }
        return from;
    }

    public static List<Int3>? PathInTree(Dictionary<Int3, Int3> tree, Int3 goal)
    {
        if (!tree.ContainsKey(goal)) return null;
        var path = new List<Int3> { goal };
        while (tree[goal] != goal)
        {
            goal = tree[goal];
            path.Add(goal);
        }
        path.Reverse();
        return path;
    }

    /// <summary>A standable cell beside a block (a cache, terminal or core), to walk up to it.</summary>
    public Int3? AccessCell(Int3 block, HashSet<Int3>? reachable = null)
    {
        foreach (Dir h in Dirs.Horizontal)
        {
            Int3 c = block + h.Vec();
            if (Standable(c) && (reachable == null || reachable.Contains(c))) return c;
        }
        return null;
    }
}
