using System;
using System.Collections.Generic;
using System.Diagnostics;
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

    /// <summary>The longest path search so far, ms, and what it was (measurements; they clear it).</summary>
    public static double WorstMs;
    public static string Worst = "";

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
    static bool Steps(ISolidSpace s, Vector3I a, Vector3I b, Body body, out Vector3[] vias)
    {
        vias = null;
        PartSet parts = s.Parts;
        if (parts == null) return true;
        if (body.Climbs) return true;
        Vector3 pa = Point(s, a, body) + Vector3.Down * 0.5f, pb = Point(s, b, body) + Vector3.Down * 0.5f;
        // Up or down a flight the feet are between the two levels: the flat test is made at the lower.
        float y = MathF.Min(pa.Y, pb.Y);
        if (!parts.Touches(pa, body.Radius, body.Height) && !parts.Touches(pb, body.Radius, body.Height)) return true;
        return parts.Link(a, pa with { Y = y }, b, pb with { Y = y }, body.Radius, body.Height, s.SolidCell, body.WallRadius, out vias);
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
    public static bool FindPath(ISolidSpace s, Vector3 from, Vector3 to, Body body, List<Vector3> path, int budget = 2500, WildSim timed = null)
    {
        path.Clear();
        Searches++;
        long began = Stopwatch.GetTimestamp(), until = Until(began, timed);
        Vector3I? a = Nearest(s, from, body, 2) ?? Nearest(s, from, body with { Shoulders = 0 }), b = Nearest(s, to, body, 3);
        if (a == null || b == null) return false;
        Vector3I start = a.Value, goal = b.Value;
        if (start == goal)
        {
            path.Add(to);
            return true;
        }
        Scratch k = Take();
        k.Cost[start] = 0f;
        k.Open.Enqueue(start, H(start, goal));
        Vector3I closest = start;
        float closestH = H(start, goal);
        bool found = false;
        int expanded = 0;
        while (k.Open.Count > 0 && expanded < budget && !Late(until))
        {
            Vector3I cur = k.Open.Dequeue();
            if (!k.Closed.Add(cur)) continue;
            expanded++;
            if (cur == goal)
            {
                found = true;
                closest = goal;
                break;
            }
            float h = H(cur, goal);
            if (h < closestH) { closestH = h; closest = cur; }
            float g = k.Cost[cur];
            Moves(s, cur, body, k);
            foreach (var (n, step) in k.Next)
            {
                float ng = g + step;
                if (k.Cost.TryGetValue(n, out float old) && old <= ng) continue;
                k.Cost[n] = ng;
                k.Came[n] = cur;
                k.Open.Enqueue(n, ng + H(n, goal));
            }
        }
        Expanded += expanded;
        Points(s, start, closest, body, k, path);
        if (found) path[^1] = to;
        timed?.PathTook(began);
        double ms = Stopwatch.GetElapsedTime(began).TotalMilliseconds;
        if (ms > WorstMs) (WorstMs, Worst) = (ms, $"{expanded} cells, {k.Cost.Count} costed, {(found ? "found" : "not found")}, {(body.Climbs ? "climber" : "walker")}, {(to - from).Length():F0} m ({to.Y - from.Y:F0} up)");
        return found;
    }

    /// <summary>
    /// A flight through a cave or a place: from near <paramref name="from"/> to the cell within <paramref name="reach"/> m
    /// of the way round that is farthest from <paramref name="threat"/> by the way round (the threat's own flood over the
    /// same cells), so out of a room by its door and down the corridor beyond rather than into the wall behind; never
    /// past the threat (a step nearer it costs four times over). The way there into <paramref name="path"/>; false
    /// where nowhere it can reach is farther (cornered).
    /// </summary>
    public static bool Away(ISolidSpace s, Vector3 from, Vector3 threat, Body body, float reach, List<Vector3> path, int budget = 700, WildSim timed = null)
    {
        path.Clear();
        Searches++;
        long began = Stopwatch.GetTimestamp(), until = Until(began, timed);
        Vector3I? a = Nearest(s, from, body, 2) ?? Nearest(s, from, body with { Shoulders = 0 });
        if (a is not { } start) return false;
        Scratch k = Take();
        // The threat's flood: how far each cell near is from it by the way round; past it, at least as far as it went.
        float far = 0f;
        if (Nearest(s, threat, body, 2) is { } t)
        {
            k.Threat[t] = 0f;
            k.Open.Enqueue(t, 0f);
            for (int done = 0; k.Open.Count > 0 && done < budget && !Late(until);)
            {
                Vector3I cur = k.Open.Dequeue();
                if (!k.Closed.Add(cur)) continue;
                done++;
                float g = k.Threat[cur];
                far = MathF.Max(far, g);
                Moves(s, cur, body, k);
                foreach (var (n, step) in k.Next)
                {
                    if (k.Threat.TryGetValue(n, out float old) && old <= g + step) continue;
                    k.Threat[n] = g + step;
                    k.Open.Enqueue(n, g + step);
                }
            }
            k.Open.Clear();
            k.Closed.Clear();
        }
        float T(Vector3I c) => k.Threat.TryGetValue(c, out float d) ? d : MathF.Max(far, (SpaceMath.Center(c) - threat).Length());
        float t0 = T(start), bestScore = float.MinValue;
        Vector3I? best = null;
        k.Cost[start] = 0f;
        k.Open.Enqueue(start, 0f);
        for (int expanded = 0; k.Open.Count > 0 && expanded < budget && !Late(until);)
        {
            Vector3I cur = k.Open.Dequeue();
            if (!k.Closed.Add(cur)) continue;
            expanded++;
            float g = k.Cost[cur], tc = T(cur), score = tc - 0.35f * g;
            if (cur != start && tc > t0 + 1f && score > bestScore) { bestScore = score; best = cur; }
            if (g >= reach) continue;
            Moves(s, cur, body, k);
            foreach (var (n, step) in k.Next)
            {
                float ng = g + step * (T(n) < tc ? 4f : 1f);
                if (k.Cost.TryGetValue(n, out float old) && old <= ng) continue;
                k.Cost[n] = ng;
                k.Came[n] = cur;
                k.Open.Enqueue(n, ng);
            }
        }
        timed?.PathTook(began);
        if (best == null) return false;
        Points(s, start, best.Value, body, k, path);
        return true;
    }

    /// <summary>When a search timed by a sim stops: when the step's path time runs out (never, untimed).</summary>
    static long Until(long began, WildSim timed) => timed == null ? long.MaxValue : began + (long)(Math.Max(0.0, timed.PathMsLeft) * Stopwatch.Frequency / 1000.0);

    static bool Late(long until) => until != long.MaxValue && Stopwatch.GetTimestamp() > until;

    /// <summary>What a search reuses (one set a thread), so a search allocates nothing once warm.</summary>
    sealed class Scratch
    {
        public readonly PriorityQueue<Vector3I, float> Open = new();
        public readonly Dictionary<Vector3I, Vector3I> Came = new();
        public readonly Dictionary<Vector3I, float> Cost = new(), Threat = new();
        public readonly Dictionary<Vector3I, bool> Walk = new();
        public readonly HashSet<Vector3I> Closed = new();
        public readonly List<Vector3I> Cells = new();
        public readonly List<(Vector3I Cell, float Step)> Next = new();
    }

    [ThreadStatic] static Scratch _scratch;

    static Scratch Take()
    {
        Scratch k = _scratch ??= new Scratch();
        k.Open.Clear();
        k.Came.Clear();
        k.Cost.Clear();
        k.Threat.Clear();
        k.Walk.Clear();
        k.Closed.Clear();
        return k;
    }

    static bool Walk(ISolidSpace s, Vector3I c, Body body, Scratch k)
    {
        if (!k.Walk.TryGetValue(c, out bool ok)) k.Walk[c] = ok = Walkable(s, c, body);
        return ok;
    }

    /// <summary>
    /// The moves a body makes from a cell, into <see cref="Scratch.Next"/>: to each walkable neighbour round no solid
    /// corner (a walker on a diagonal) with its body clear of the parts on the way; and a walker's up or down a flight
    /// (a ramp or stair rising a cell a cell), as people go: onto the cell over a ramp rising ahead, down onto one over a ramp falling ahead.
    /// </summary>
    static void Moves(ISolidSpace s, Vector3I cur, Body body, Scratch k)
    {
        k.Next.Clear();
        bool climbs = body.Climbs;
        foreach (Vector3I d in climbs ? Around26 : Around8)
        {
            var n = new Vector3I(cur.X + d.X, cur.Y + d.Y, cur.Z + d.Z);
            if (k.Closed.Contains(n) || !Walk(s, n, body, k)) continue;
            if (!climbs && d.X != 0 && d.Z != 0 && (s.SolidCell(cur.X + d.X, cur.Y, cur.Z) || s.SolidCell(cur.X, cur.Y, cur.Z + d.Z))) continue;
            if (!Steps(s, cur, n, body)) continue;
            k.Next.Add((n, MathF.Sqrt(d.X * d.X + d.Y * d.Y + d.Z * d.Z)));
        }
        if (climbs) return;
        foreach (Vector3I r in Slopes.Rise)
        {
            Vector3I n;
            if (s.SlopeCell(cur.X + r.X, cur.Y, cur.Z + r.Z) == Slopes.Of(r.X, r.Z)) n = new Vector3I(cur.X + r.X, cur.Y + 1, cur.Z + r.Z);
            else if (s.SlopeCell(cur.X + r.X, cur.Y - 2, cur.Z + r.Z) == Slopes.Of(-r.X, -r.Z)) n = new Vector3I(cur.X + r.X, cur.Y - 1, cur.Z + r.Z);
            else continue;
            if (k.Closed.Contains(n) || !Walk(s, n, body, k) || !Steps(s, cur, n, body)) continue;
            k.Next.Add((n, 1.4142f));
        }
    }

    /// <summary>The way from the start to a cell a search reached, as the points a body goes by (and the gaps among the parts between them).</summary>
    static void Points(ISolidSpace s, Vector3I start, Vector3I end, Body body, Scratch k, List<Vector3> path)
    {
        k.Cells.Clear();
        for (Vector3I c = end; ; c = k.Came[c])
        {
            k.Cells.Add(c);
            if (c == start || !k.Came.ContainsKey(c)) break;
        }
        k.Cells.Reverse();
        for (int i = 0; i < k.Cells.Count; i++)
        {
            if (i > 0 && Steps(s, k.Cells[i - 1], k.Cells[i], body, out Vector3[] vias) && vias != null)
                foreach (Vector3 v in vias) path.Add(v + Vector3.Up * 0.5f);
            path.Add(Point(s, k.Cells[i], body));
        }
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
