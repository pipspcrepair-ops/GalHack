#nullable enable
using System;
using System.Collections.Generic;

namespace PoiGenerator.Gen;

/// <summary>
/// Walking on the block grid, for validation and for enemies: plain A* over cells a 1.8 m body
/// can stand in. A node is the cell your feet are in. You stand in a clear cell with a clear
/// cell above and something solid below, on a ramp or stair, on a ladder, or on a lift car's stop.
/// Moves are one cell sideways at the same height, up and down ramps and stairs along their slope,
/// up and down ladders (stepping off sideways at the top), and between a lift's stops; no jumping
/// or dropping, so every path can be walked back.
///
/// Below ground level, any cell no module occupies is rock.
/// </summary>
public sealed class NavGrid
{
    readonly IBlockStructure _grid;
    readonly CellMask _occupied;
    readonly HashSet<Int3>? _liftNodes;
    readonly Dictionary<Int3, List<Int3>>? _liftLinks;
    readonly bool _space;

    /// <summary>
    /// Where some of a cell is taken by things that fill only part of it (furniture, clutter): whether a
    /// body fits standing in the cell somewhere (null: the blocks alone say).
    /// </summary>
    public Func<Int3, bool>? Fits;

    /// <summary>And whether it gets from one cell to the next, its body clear all the way (null: always).</summary>
    public Func<Int3, Int3, bool>? Passes;

    public NavGrid(IBlockStructure grid, CellMask occupied, IReadOnlyList<LiftShaft>? lifts = null, bool space = false)
    {
        _grid = grid;
        _occupied = occupied;
        _space = space;
        if (lifts == null || lifts.Count == 0) return;
        _liftNodes = new HashSet<Int3>();
        _liftLinks = new Dictionary<Int3, List<Int3>>();
        foreach (var lift in lifts)
            foreach (Int3 f in lift.Footprint)
            {
                var stops = new List<Int3>();
                foreach (int y in lift.StopWalkY) stops.Add(f with { Y = y });
                foreach (Int3 s in stops)
                {
                    _liftNodes.Add(s);
                    var others = new List<Int3>();
                    foreach (Int3 o in stops) if (o != s) others.Add(o);
                    _liftLinks[s] = others;
                }
            }
    }

    /// <summary>Below ground level, anything no module occupies is rock (on planets, not in space).</summary>
    public bool IsRock(Int3 p) => !_space && p.Y <= Assembler.GroundY && !_occupied[p];

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
        return BlockInfo.IsSlope(b.Type);
    }

    public bool LadderAt(Int3 p) => _grid.Get(p).Type == BlockType.Ladder;
    public bool LiftAt(Int3 p) => _liftNodes != null && _liftNodes.Contains(p);

    public bool Standable(Int3 f)
    {
        // Outside the grid the plain goes on for ever; walking is limited to the place itself.
        if (!_grid.InBounds(f)) return false;
        if (RampAt(f, out _)) return Clear(f + Int3.Up) && Clear(f + Int3.Up * 2);
        if (LadderAt(f)) return Clear(f + Int3.Up);
        if (LiftAt(f)) return Clear(f) && Clear(f + Int3.Up);
        return Clear(f) && Clear(f + Int3.Up) && SupportsStanding(f + Int3.Down) && (Fits == null || Fits(f));
    }

    void Add(List<Int3> into, Int3 from, Int3 to)
    {
        if (Passes == null || Passes(from, to)) into.Add(to);
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
        bool onLadder = LadderAt(f);
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
                if (ok) Add(into, f, g);
            }
            // Up a ramp and off its top.
            if (onRamp && h == rampDir)
            {
                Int3 up = g + Int3.Up;
                if (Standable(up) && (!RampAt(up, out Dir upDir) || upDir == rampDir)) Add(into, f, up);
            }
            // Down onto a ramp whose top is here.
            Int3 down = g + Int3.Down;
            if (RampAt(down, out Dir dDir) && dDir == h.Opposite() && Standable(down) && (!onRamp || rampDir == dDir))
                Add(into, f, down);

            // Off the top of a ladder: climb into the clear cell above it and step sideways.
            if (onLadder && !LadderAt(f + Int3.Up) && Clear(f + Int3.Up) && Clear(f + Int3.Up * 2))
            {
                Int3 off = f + Int3.Up + h.Vec();
                if (Standable(off) && !LadderAt(off) && !RampAt(off, out _)) into.Add(off);
            }
            // Onto the top of a ladder from the floor beside its shaft.
            if (!onLadder && !onRamp)
            {
                Int3 shaft = f + h.Vec();
                Int3 top = shaft + Int3.Down;
                if (LadderAt(top) && !LadderAt(shaft) && Clear(shaft) && Clear(shaft + Int3.Up) && Standable(top)) into.Add(top);
            }
        }
        if (onLadder)
        {
            Int3 up = f + Int3.Up, dn = f + Int3.Down;
            if (LadderAt(up) && Standable(up)) into.Add(up);
            if (LadderAt(dn) && Standable(dn)) into.Add(dn);
        }
        if (_liftLinks != null && _liftLinks.TryGetValue(f, out var stops))
            foreach (Int3 s in stops)
                if (Standable(s)) into.Add(s);
    }

    /// <summary>Plain A* with a Manhattan heuristic. Returns null if there is no path.</summary>
    public List<Int3>? FindPath(Int3 start, Int3 goal, int maxNodes = 400_000)
    {
        if (!Standable(start) || !Standable(goal)) return null;
        var open = new PriorityQueue<Int3, int>();
        var cost = new Dictionary<Int3, int> { [start] = 0 };
        var from = new Dictionary<Int3, Int3>();
        // Lifts jump many cells at once, so the heuristic must not overestimate across them.
        bool lifts = _liftLinks != null;
        open.Enqueue(start, lifts ? 0 : start.Manhattan(goal));
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
                open.Enqueue(n, nc + (lifts ? 0 : n.Manhattan(goal)));
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
            if (Standable(c) && !LadderAt(c) && (reachable == null || reachable.Contains(c))) return c;
        }
        return null;
    }
}
