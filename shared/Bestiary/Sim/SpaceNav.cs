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

    /// <summary>
    /// What a path is found for: whether it climbs, the whole cells of headroom it needs, the clear cells
    /// either side a wide body needs (it doesn't go through a door it can't fit), and the body the
    /// place's parts are tested against (<see cref="PartSet"/>): a walker's capsule (radius, height), a
    /// climber's ball (radius).
    /// </summary>
    public readonly record struct Body(bool Climbs, int Headroom, int Shoulders, float Radius, float Height, float WallRadius = 0f)
    {
        public static Body Of(bool climbs, int headroom, int shoulders = 0) => new(climbs, headroom, shoulders, climbs ? 0.1f : 0.25f, climbs ? 0f : MathF.Min(headroom, 1.2f), climbs ? 0f : 0.3f);
    }

    /// <summary>A species' body as its paths are found for.</summary>
    public static Body BodyOf(Species s) => new(s.Climbs, Headroom(s), Shoulders(s), PartRadius(s), s.Climbs ? 0f : PartHeight(s), s.Climbs ? 0f : Crawl.ClearanceFor(s));

    /// <summary>The radius a body keeps off the place's parts: a walker its own, a climber most of the clearance it rides at.</summary>
    public static float PartRadius(Species s) => s.Climbs ? Crawl.ClearanceFor(s) * 0.8f : Math.Clamp(s.Stats.Radius, 0.05f, PartSet.Reach);

    /// <summary>How tall a walker stands, for going under a part (a table, a chair's seat) or not.</summary>
    public static float PartHeight(Species s) => MathF.Max(0.1f, s.BodyHeight);

    public static bool Walkable(ISolidSpace s, Vector3I c, bool climbs, int headroom, int shoulders = 0) => Walkable(s, c, Body.Of(climbs, headroom, shoulders));

    public static bool Walkable(ISolidSpace s, Vector3I c, Body body)
    {
        if (s.SolidCell(c.X, c.Y, c.Z)) return false;
        PartSet parts = s.Parts;
        if (body.Climbs)
        {
            bool surface = SpaceMath.SurfaceCell(s, c.X, c.Y, c.Z);
            if (parts == null || !parts.Any(SpaceMath.Center(c))) return surface;
            // Among parts: something to hold in reach, and room for its body somewhere in the cell.
            if (!surface && parts.Distance(SpaceMath.Center(c)) > 0.9f) return false;
            return parts.Hold(c.X, c.Y, c.Z, body.Radius) != null;
        }
        if (!s.SolidCell(c.X, c.Y - 1, c.Z)) return false;
        for (int h = 1; h < body.Headroom; h++)
            if (s.SolidCell(c.X, c.Y + h, c.Z)) return false;
        int y = c.Y + 1;
        for (int r = 1; r <= body.Shoulders; r++)
            if ((s.SolidCell(c.X - r, y, c.Z) || s.SolidCell(c.X + r, y, c.Z)) && (s.SolidCell(c.X, y, c.Z - r) || s.SolidCell(c.X, y, c.Z + r))) return false;
        // Somewhere in the cell its body stands clear of the parts in and round it.
        return parts == null || parts.Spot(c.X, c.Y, c.Z, c.Y, body.Radius, body.Height, s.SolidCell, body.WallRadius) != null;
    }

    /// <summary>The point a path goes through in a cell: its middle, or among parts where the body fits.</summary>
    public static Vector3 Point(ISolidSpace s, Vector3I c, Body body)
    {
        PartSet parts = s.Parts;
        if (parts == null) return SpaceMath.Center(c);
        if (body.Climbs) return parts.Hold(c.X, c.Y, c.Z, body.Radius) ?? SpaceMath.Center(c);
        return parts.Spot(c.X, c.Y, c.Z, c.Y, body.Radius, body.Height, s.SolidCell, body.WallRadius) is { } p ? p + Vector3.Up * 0.5f : SpaceMath.Center(c);
    }

    /// <summary>Whether a walker gets straight from one cell to the next, its body clear of the parts all the way.</summary>
    public static bool Steps(ISolidSpace s, Vector3I a, Vector3I b, Body body) => Steps(s, a, b, body, out _);

    /// <summary>And the point between them it goes by, where the straight way between the two cells' points is barred but a gap on their shared face isn't.</summary>
    static bool Steps(ISolidSpace s, Vector3I a, Vector3I b, Body body, out Vector3? via)
    {
        via = null;
        PartSet parts = s.Parts;
        if (parts == null || body.Climbs) return true;
        Vector3 pa = Point(s, a, body) + Vector3.Down * 0.5f, pb = Point(s, b, body) + Vector3.Down * 0.5f;
        // Up or down a flight the feet are between the two levels: the flat test is made at the lower.
        float y = MathF.Min(pa.Y, pb.Y);
        if (!parts.Touches(pa, body.Radius, body.Height) && !parts.Touches(pb, body.Radius, body.Height)) return true;
        bool ok = parts.Link(a, pa with { Y = y }, b, pb with { Y = y }, body.Radius, body.Height, s.SolidCell, body.WallRadius, out via);
        if (via is { } v) via = v + Vector3.Up * 0.5f;
        return ok;
    }

    /// <summary>The headroom a walker needs, in cells.</summary>
    public static int Headroom(Species s) => Math.Max(1, (int)MathF.Ceiling(s.BodyHeight));

    /// <summary>The clear cells a body needs either side: none until it is two metres across.</summary>
    public static int Shoulders(Species s) => s.Climbs ? 0 : (int)(s.BodyWidth * 0.5f);

    /// <summary>The walkable cell nearest a point (searching a few cells round it), if any.</summary>
    public static Vector3I? Nearest(ISolidSpace s, Vector3 p, bool climbs, int headroom, int reach = 2, int shoulders = 0) => Nearest(s, p, Body.Of(climbs, headroom, shoulders), reach);

    public static Vector3I? Nearest(ISolidSpace s, Vector3 p, Body body, int reach = 2)
    {
        Vector3I c0 = SpaceMath.Cell(p);
        Vector3I? best = null;
        float bestD = float.MaxValue;
        for (int dz = -reach; dz <= reach; dz++)
        for (int dy = -reach; dy <= reach; dy++)
        for (int dx = -reach; dx <= reach; dx++)
        {
            var c = new Vector3I(c0.X + dx, c0.Y + dy, c0.Z + dz);
            float d = (SpaceMath.Center(c) - p).LengthSquared();
            if (d >= bestD || !Walkable(s, c, body)) continue;
            bestD = d;
            best = c;
        }
        return best;
    }

    public static bool FindPath(ISolidSpace s, Vector3 from, Vector3 to, bool climbs, int headroom, List<Vector3> path, int budget = 2500, int shoulders = 0) =>
        FindPath(s, from, to, Body.Of(climbs, headroom, shoulders), path, budget);

    /// <summary>
    /// A path of cell points (their middles, or among parts where the body fits) from near
    /// <paramref name="from"/> to near <paramref name="to"/>. Returns false if the goal wasn't reached
    /// within the budget; the path then leads to the closest cell found, so a creature still heads the right way.
    /// </summary>
    public static bool FindPath(ISolidSpace s, Vector3 from, Vector3 to, Body body, List<Vector3> path, int budget = 2500)
    {
        path.Clear();
        Searches++;
        bool climbs = body.Climbs;
        Vector3I? a = Nearest(s, from, body, 2) ?? Nearest(s, from, body with { Shoulders = 0 }), b = Nearest(s, to, body, 3);
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
                    ok = Walkable(s, n, body);
                    walk[n] = ok;
                }
                if (!ok) continue;
                // No cutting a corner through solid on a diagonal, nor a step that takes the body through a part.
                if (!climbs && d.X != 0 && d.Z != 0 && (s.SolidCell(cur.X + d.X, cur.Y, cur.Z) || s.SolidCell(cur.X, cur.Y, cur.Z + d.Z))) continue;
                if (!Steps(s, cur, n, body)) continue;
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
                    ok = Walkable(s, n, body);
                    walk[n] = ok;
                }
                if (!ok || !Steps(s, cur, n, body)) continue;
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
        for (int i = 0; i < cells.Count; i++)
        {
            if (i > 0 && Steps(s, cells[i - 1], cells[i], body, out Vector3? via) && via is { } v) path.Add(v);
            path.Add(Point(s, cells[i], body));
        }
        if (found) path[^1] = to;
        return found;
    }

    static float H(Vector3I a, Vector3I b)
    {
        float dx = a.X - b.X, dy = a.Y - b.Y, dz = a.Z - b.Z;
        return MathF.Sqrt(dx * dx + dy * dy + dz * dz);
    }

    /// <summary>Open cells reachable by a walker or a climber from a point (flood fill, bounded): what a place's creatures can reach.</summary>
    public static HashSet<Vector3I> Reach(ISolidSpace s, Vector3 from, bool climbs, int headroom, int max = 20000) => Reach(s, from, Body.Of(climbs, headroom), max);

    public static HashSet<Vector3I> Reach(ISolidSpace s, Vector3 from, Body body, int max = 20000)
    {
        bool climbs = body.Climbs;
        var seen = new HashSet<Vector3I>();
        Vector3I? a = Nearest(s, from, body);
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
                if (seen.Contains(n) || !Walkable(s, n, body) || !Steps(s, cur, n, body)) continue;
                seen.Add(n);
                queue.Enqueue(n);
            }
        }
        return seen;
    }
}
