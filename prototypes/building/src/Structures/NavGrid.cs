using System;
using System.Collections.Generic;
using System.Linq;

namespace GalHack.Structures;

public enum NavKind { Floor, Door, Ladder }

/// <summary>A move between two nav cells. Door links need the door opened; ladder links are climbs.</summary>
public readonly record struct NavLink(Int3 From, Int3 To, NavKind Kind);

/// <summary>
/// The walkable cells of a structure and the links between them, for other systems (crew,
/// boarders, invaders) to run A* on. A cell is walkable when a character fits in it (enough
/// clear cells above for its height) and the block under it has a full top face. Doors and
/// ladders become nodes of their own kind, joined by door and ladder links.
/// </summary>
public sealed class NavGrid
{
    public readonly Dictionary<Int3, NavKind> Nodes = new();
    public readonly List<NavLink> Links = new();
    readonly Dictionary<Int3, List<NavLink>> _from = new();

    public IReadOnlyList<NavLink> LinksFrom(Int3 cell) =>
        _from.TryGetValue(cell, out var list) ? list : (IReadOnlyList<NavLink>)Array.Empty<NavLink>();

    public static NavGrid Build(Structure s, float characterHeight = 1.8f)
    {
        var nav = new NavGrid();
        if (s.Count == 0) return nav;
        int headroom = Math.Max(1, (int)MathF.Ceiling(characterHeight / s.Grid.CellSize - 1e-3f));

        // A character can be in a cell with no block, an open door, or a non-solid component.
        bool Clear(Int3 c) => !s.TryGet(c, out Block b) || !s.IsSolid(b) || s.TypeOf(b).IsDoor;
        bool Fits(Int3 c)
        {
            for (int k = 0; k < headroom; k++)
                if (!Clear(c + new Int3(0, k, 0))) return false;
            return true;
        }
        bool FloorUnder(Int3 c) =>
            s.TryGet(c - Int3.UnitY, out Block below) && s.IsSolid(below) && s.AttachCoverage(below, Dir.PosY) == Coverage.Full;

        var (min, max) = s.Bounds();
        for (int z = min.Z - 1; z <= max.Z + 1; z++)
            for (int y = min.Y; y <= max.Y + 1; y++)
                for (int x = min.X - 1; x <= max.X + 1; x++)
                {
                    var c = new Int3(x, y, z);
                    bool hasBlock = s.TryGet(c, out Block b);
                    if (hasBlock && s.TypeOf(b).IsLadder) nav.Nodes[c] = NavKind.Ladder;
                    else if (Fits(c) && FloorUnder(c))
                        nav.Nodes[c] = hasBlock && s.TypeOf(b).IsDoor ? NavKind.Door : NavKind.Floor;
                }

        foreach (var (c, kind) in nav.Nodes)
        {
            foreach (Dir d in new[] { Dir.PosX, Dir.NegX, Dir.PosZ, Dir.NegZ })
            {
                Int3 n = c + d.Vec();
                if (!nav.Nodes.TryGetValue(n, out NavKind other)) continue;
                NavKind link = kind == NavKind.Door || other == NavKind.Door ? NavKind.Door
                    : kind == NavKind.Ladder || other == NavKind.Ladder ? NavKind.Ladder : NavKind.Floor;
                nav.Add(new NavLink(c, n, link));
            }
            if (kind != NavKind.Ladder) continue;
            // Ladders climb: up and down the ladder, and on to whatever floor is at either end.
            foreach (Dir d in new[] { Dir.PosY, Dir.NegY })
            {
                Int3 n = c + d.Vec();
                if (nav.Nodes.ContainsKey(n)) nav.Add(new NavLink(c, n, NavKind.Ladder));
            }
            // From the top rung, step up and over onto whatever the ladder leans against.
            Dir back = Orientations.ToWorld(s.Get(c).Rot, Dir.PosZ);
            Int3 over = c + Int3.UnitY + back.Vec();
            if (!nav.Nodes.ContainsKey(c + Int3.UnitY) && nav.Nodes.TryGetValue(over, out NavKind top) && top != NavKind.Ladder)
                nav.Add(new NavLink(c, over, NavKind.Ladder));
        }
        // Make every ladder link two-way (a floor above a ladder sees the ladder below it).
        foreach (NavLink l in nav.Links.ToList())
            if (l.Kind == NavKind.Ladder && !nav.LinksFrom(l.To).Any(r => r.To == l.From))
                nav.Add(new NavLink(l.To, l.From, NavKind.Ladder));
        return nav;
    }

    void Add(NavLink link)
    {
        Links.Add(link);
        if (!_from.TryGetValue(link.From, out var list)) _from[link.From] = list = new List<NavLink>();
        list.Add(link);
    }

    /// <summary>A plain A* over the links, to show the export is enough to path on. Doors cost a little extra.</summary>
    public List<Int3> FindPath(Int3 from, Int3 to)
    {
        if (!Nodes.ContainsKey(from) || !Nodes.ContainsKey(to)) return null;
        var open = new PriorityQueue<Int3, float>();
        var came = new Dictionary<Int3, Int3>();
        var cost = new Dictionary<Int3, float> { [from] = 0 };
        open.Enqueue(from, 0);
        while (open.TryDequeue(out Int3 c, out _))
        {
            if (c == to)
            {
                var path = new List<Int3> { c };
                while (came.TryGetValue(c, out c)) path.Add(c);
                path.Reverse();
                return path;
            }
            foreach (NavLink l in LinksFrom(c))
            {
                float g = cost[c] + (l.Kind == NavKind.Door ? 2f : 1f);
                if (cost.TryGetValue(l.To, out float old) && old <= g) continue;
                cost[l.To] = g;
                came[l.To] = c;
                Int3 h = l.To - to;
                open.Enqueue(l.To, g + Math.Abs(h.X) + Math.Abs(h.Y) + Math.Abs(h.Z));
            }
        }
        return null;
    }
}
