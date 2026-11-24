using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

namespace GalHack.Structures;

public enum NavKind { Floor, Door, Ladder }

/// <summary>A move between two nav cells. Door links need the door opened; ladder links are climbs.</summary>
public readonly record struct NavLink(Int3 From, Int3 To, NavKind Kind);

/// <summary>
/// The walkable cells of a structure and the links between them, for other systems (crew,
/// boarders, invaders) to run A* on. Floors are the blocks' real shapes (<see cref="Structure.SolidAt"/>):
/// a cell is walkable where there is something to stand on at its foot, or within a step above it (a
/// half block, a slope's middle, a stair), and the character fits from there up for its height.
/// Neighbours join where the surface between them rises no more than a step (a ramp of slopes, a
/// half block, a stair; never a full block). Doors and ladders become nodes of their own kind, joined
/// by door and ladder links.
/// </summary>
public sealed class NavGrid
{
    public readonly Dictionary<Int3, NavKind> Nodes = new();

    /// <summary>Where a character stands in each node's cell (structure frame, in cells, at the cell's middle).</summary>
    public readonly Dictionary<Int3, float> Floor = new();
    public readonly List<NavLink> Links = new();
    readonly Dictionary<Int3, List<NavLink>> _from = new();

    public IReadOnlyList<NavLink> LinksFrom(Int3 cell) =>
        _from.TryGetValue(cell, out var list) ? list : (IReadOnlyList<NavLink>)Array.Empty<NavLink>();

    /// <param name="stepHeight">the tallest rise walked up without a climb, m (the shared walker's step)</param>
    public static NavGrid Build(Structure s, float characterHeight = 1.8f, float stepHeight = 0.6f)
    {
        var nav = new NavGrid();
        if (s.Count == 0) return nav;
        float cell = s.Grid.CellSize, height = characterHeight / cell, step = stepHeight / cell;
        const float Fine = 0.05f;

        // Doors are walked through (a door node opens them); anything else solid is where its shape is.
        bool Solid(Vector3 p) => s.SolidAt(p) && !(s.TryGet(Int3.Round(p), out Block b) && s.TypeOf(b).IsDoor);
        // The surface under a point: the top of whatever is solid between from and to (cells), or null.
        float? Surface(float x, float z, float from, float to)
        {
            for (float y = from; y >= to; y -= Fine)
                if (Solid(new Vector3(x, y, z))) return y + Fine * 0.5f;
            return null;
        }
        bool Fits(float x, float z, float floor)
        {
            for (float y = floor + Fine; y < floor + height; y += 0.25f)
                if (Solid(new Vector3(x, y, z))) return false;
            return !Solid(new Vector3(x, floor + height - 0.01f, z));
        }

        var (min, max) = s.Bounds();
        for (int z = min.Z - 1; z <= max.Z + 1; z++)
            for (int y = min.Y; y <= max.Y + 1; y++)
                for (int x = min.X - 1; x <= max.X + 1; x++)
                {
                    var c = new Int3(x, y, z);
                    bool hasBlock = s.TryGet(c, out Block b);
                    if (hasBlock && s.TypeOf(b).IsLadder)
                    {
                        nav.Nodes[c] = NavKind.Ladder;
                        nav.Floor[c] = y - 0.5f;
                        continue;
                    }
                    // Standing at the cell's foot (on the block below), or on something no taller than a step inside it.
                    if (Surface(x, z, y - 0.5f + step, y - 0.5f - Fine) is not float floor || !Fits(x, z, floor)) continue;
                    nav.Nodes[c] = hasBlock && s.TypeOf(b).IsDoor ? NavKind.Door : NavKind.Floor;
                    nav.Floor[c] = floor;
                }

        foreach (var (c, kind) in nav.Nodes)
        {
            foreach (Dir d in new[] { Dir.PosX, Dir.NegX, Dir.PosZ, Dir.NegZ })
                for (int dy = -1; dy <= 1; dy++)
                {
                    Int3 n = c + d.Vec() + Int3.UnitY * dy;
                    if (!nav.Nodes.TryGetValue(n, out NavKind other)) continue;
                    // Across the shared edge the surface rises no more than a step: a ramp or a step, not a wall.
                    Vector3 toward = d.Vec().ToVector3() * 0.45f;
                    float fa = nav.Floor[c], fb = nav.Floor[n];
                    if (Surface(c.X + toward.X, c.Z + toward.Z, fa + step, fa - step) is not float ea
                        || Surface(n.X - toward.X, n.Z - toward.Z, fb + step, fb - step) is not float eb
                        || MathF.Abs(ea - eb) > step) continue;
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
