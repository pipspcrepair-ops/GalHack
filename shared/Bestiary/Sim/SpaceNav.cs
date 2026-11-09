using System;
using System.Collections.Generic;
using Godot;

namespace Wildlife;

/// <summary>
/// Paths through a cave or a place on its 1 m cells, with a bounded A*. A climber may use any
/// open cell that touches solid (floors, walls, ceilings, round any corner); a walker needs an
/// open cell with solid under it and headroom over it, and moves sideways only. Paths are
/// guidance: the crawl locomotion follows the real surface between the waypoints.
/// </summary>
public static class SpaceNav
{
    static readonly Vector3I[] Around26 = Build26();
    static readonly Vector3I[] Around8 =
    {
        new(1, 0, 0), new(-1, 0, 0), new(0, 0, 1), new(0, 0, -1),
        new(1, 0, 1), new(1, 0, -1), new(-1, 0, 1), new(-1, 0, -1),
    };

    static Vector3I[] Build26()
    {
        var list = new List<Vector3I>();
        for (int z = -1; z <= 1; z++)
        for (int y = -1; y <= 1; y++)
        for (int x = -1; x <= 1; x++)
            if (x != 0 || y != 0 || z != 0) list.Add(new Vector3I(x, y, z));
        return list.ToArray();
    }

    /// <summary>Searches made, and nodes expanded in all of them (for the cost checks).</summary>
    public static int Searches, Expanded;

    /// <summary><paramref name="shoulders"/>: whole cells of clear space it needs either side (a wide body doesn't go through a door it can't fit).</summary>
    public static bool Walkable(ISolidSpace s, Vector3I c, bool climbs, int headroom, int shoulders = 0)
    {
        if (s.SolidCell(c.X, c.Y, c.Z)) return false;
        if (climbs) return SpaceMath.SurfaceCell(s, c.X, c.Y, c.Z);
        if (!s.SolidCell(c.X, c.Y - 1, c.Z)) return false;
        for (int h = 1; h < headroom; h++)
            if (s.SolidCell(c.X, c.Y + h, c.Z)) return false;
        int y = c.Y + 1;
        for (int r = 1; r <= shoulders; r++)
            if ((s.SolidCell(c.X - r, y, c.Z) || s.SolidCell(c.X + r, y, c.Z)) && (s.SolidCell(c.X, y, c.Z - r) || s.SolidCell(c.X, y, c.Z + r))) return false;
        return true;
    }

    /// <summary>The headroom a walker needs, in cells.</summary>
    public static int Headroom(Species s) => Math.Max(1, (int)MathF.Ceiling(s.BodyHeight));

    /// <summary>The clear cells a body needs either side: none until it is two metres across.</summary>
    public static int Shoulders(Species s) => s.Climbs ? 0 : (int)(s.BodyWidth * 0.5f);

    /// <summary>The walkable cell nearest a point (searching a few cells round it), if any.</summary>
    public static Vector3I? Nearest(ISolidSpace s, Vector3 p, bool climbs, int headroom, int reach = 2, int shoulders = 0)
    {
        Vector3I c0 = SpaceMath.Cell(p);
        Vector3I? best = null;
        float bestD = float.MaxValue;
        for (int dz = -reach; dz <= reach; dz++)
        for (int dy = -reach; dy <= reach; dy++)
        for (int dx = -reach; dx <= reach; dx++)
        {
            var c = new Vector3I(c0.X + dx, c0.Y + dy, c0.Z + dz);
            if (!Walkable(s, c, climbs, headroom, shoulders)) continue;
            float d = (SpaceMath.Center(c) - p).LengthSquared();
            if (d < bestD) { bestD = d; best = c; }
        }
        return best;
    }

    /// <summary>
    /// A path of cell centres from near <paramref name="from"/> to near <paramref name="to"/>.
    /// Returns false if the goal wasn't reached within the budget; the path then leads to the
    /// closest cell found, so a creature still heads the right way.
    /// </summary>
    public static bool FindPath(ISolidSpace s, Vector3 from, Vector3 to, bool climbs, int headroom, List<Vector3> path, int budget = 2500, int shoulders = 0)
    {
        path.Clear();
        Searches++;
        Vector3I? a = Nearest(s, from, climbs, headroom, 2, shoulders) ?? Nearest(s, from, climbs, headroom), b = Nearest(s, to, climbs, headroom, 3, shoulders);
        if (a == null || b == null) return false;
        Vector3I start = a.Value, goal = b.Value;
        if (start == goal)
        {
            path.Add(to);
            return true;
        }
        var open = new PriorityQueue<Vector3I, float>();
        var came = new Dictionary<Vector3I, Vector3I>();
        var cost = new Dictionary<Vector3I, float> { [start] = 0f };
        var walk = new Dictionary<Vector3I, bool>();
        open.Enqueue(start, H(start, goal));
        Vector3I closest = start;
        float closestH = H(start, goal);
        Vector3I[] around = climbs ? Around26 : Around8;
        bool found = false;
        int expanded = 0;
        while (open.Count > 0 && expanded < budget)
        {
            Vector3I cur = open.Dequeue();
            expanded++;
            if (cur == goal)
            {
                found = true;
                closest = goal;
                break;
            }
            float h = H(cur, goal);
            if (h < closestH) { closestH = h; closest = cur; }
            float g = cost[cur];
            foreach (Vector3I d in around)
            {
                var n = new Vector3I(cur.X + d.X, cur.Y + d.Y, cur.Z + d.Z);
                if (!walk.TryGetValue(n, out bool ok))
                {
                    ok = Walkable(s, n, climbs, headroom, shoulders);
                    walk[n] = ok;
                }
                if (!ok) continue;
                // No cutting a corner through solid on a diagonal.
                if (!climbs && d.X != 0 && d.Z != 0 && (s.SolidCell(cur.X + d.X, cur.Y, cur.Z) || s.SolidCell(cur.X, cur.Y, cur.Z + d.Z))) continue;
                float step = MathF.Sqrt(d.X * d.X + d.Y * d.Y + d.Z * d.Z);
                float ng = g + step;
                if (cost.TryGetValue(n, out float old) && old <= ng) continue;
                cost[n] = ng;
                came[n] = cur;
                open.Enqueue(n, ng + H(n, goal));
            }
            // A walker goes up and down a flight (a ramp or stair rising a cell a cell) as people do:
            // up onto the cell over a ramp rising ahead, down onto one over a ramp falling ahead.
            if (climbs) continue;
            foreach (Vector3I r in Slopes.Rise)
            {
                Vector3I n;
                if (s.SlopeCell(cur.X + r.X, cur.Y, cur.Z + r.Z) == Slopes.Of(r.X, r.Z)) n = new Vector3I(cur.X + r.X, cur.Y + 1, cur.Z + r.Z);
                else if (s.SlopeCell(cur.X + r.X, cur.Y - 2, cur.Z + r.Z) == Slopes.Of(-r.X, -r.Z)) n = new Vector3I(cur.X + r.X, cur.Y - 1, cur.Z + r.Z);
                else continue;
                if (!walk.TryGetValue(n, out bool ok))
                {
                    ok = Walkable(s, n, false, headroom, shoulders);
                    walk[n] = ok;
                }
                if (!ok) continue;
                float ng = g + 1.4142f;
                if (cost.TryGetValue(n, out float old) && old <= ng) continue;
                cost[n] = ng;
                came[n] = cur;
                open.Enqueue(n, ng + H(n, goal));
            }
        }
        Expanded += expanded;
        var cells = new List<Vector3I>();
        for (Vector3I c = closest; ; c = came[c])
        {
            cells.Add(c);
            if (c == start || !came.ContainsKey(c)) break;
        }
        cells.Reverse();
        foreach (Vector3I c in cells) path.Add(SpaceMath.Center(c));
        if (found) path[^1] = to;
        return found;
    }

    static float H(Vector3I a, Vector3I b)
    {
        float dx = a.X - b.X, dy = a.Y - b.Y, dz = a.Z - b.Z;
        return MathF.Sqrt(dx * dx + dy * dy + dz * dz);
    }

    /// <summary>Open cells reachable by a walker or a climber from a point (flood fill, bounded): what a place's creatures can reach.</summary>
    public static HashSet<Vector3I> Reach(ISolidSpace s, Vector3 from, bool climbs, int headroom, int max = 20000)
    {
        var seen = new HashSet<Vector3I>();
        Vector3I? a = Nearest(s, from, climbs, headroom);
        if (a == null) return seen;
        var queue = new Queue<Vector3I>();
        queue.Enqueue(a.Value);
        seen.Add(a.Value);
        Vector3I[] around = climbs ? Around26 : Around8;
        while (queue.Count > 0 && seen.Count < max)
        {
            Vector3I cur = queue.Dequeue();
            foreach (Vector3I d in around)
            {
                var n = new Vector3I(cur.X + d.X, cur.Y + d.Y, cur.Z + d.Z);
                if (seen.Contains(n) || !Walkable(s, n, climbs, headroom)) continue;
                seen.Add(n);
                queue.Enqueue(n);
            }
        }
        return seen;
    }
}
