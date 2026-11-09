using System;
using System.Collections.Generic;
using Godot;

namespace Wildlife;

/// <summary>
/// The solid parts of a place that fill only some of their cell: a table's top and legs, a chair's seat,
/// a locker's body, a monitor on a wall, a crate, a heap of rubble, a dressed room's furniture and
/// clutter. Each is held as the box it really collides as (turned and tilted as it stands), never as
/// its whole cell, so the one rule here serves everything that moves through a place:
///
/// - A body is a capsule standing on the floor: a radius, and a height from the floor up. It fits
///   where it is clear of every part (<see cref="Gap"/>); it goes round a part it can't fit beside
///   and under one it is shorter than (a cat under a table, between a chair's legs), never through one.
/// - A cell can be stood in where some point of it fits the body (<see cref="Spot"/>): a walker's way
///   through a part-filled cell goes by that point, not the cell's middle; and a step from one cell to
///   the next is taken only where the body fits all along it (<see cref="Sweep"/>).
/// - What moves is kept off the parts by the same test (<see cref="PushOut"/>), sideways.
/// - A climber holds the parts' faces as it holds the walls (<see cref="Distance"/> joins the place's
///   density), so it climbs over a monitor on a wall rather than into it.
///
/// Positions are in the frame of the place's cells. Safe to read from several threads; changes
/// (<see cref="AddBox"/>, <see cref="Remove"/>) are made by one.
/// </summary>
public sealed class PartSet
{
    /// <summary>A part as it collides: an oriented box, and what it belongs to (a cell's block, a placed piece).</summary>
    public readonly struct Box
    {
        public readonly Vector3 Centre, X, Y, Z, Half, Lo, Hi;
        public readonly long Owner;

        public Box(Vector3 centre, Vector3 x, Vector3 y, Vector3 z, Vector3 half, long owner)
        {
            Centre = centre; X = x; Y = y; Z = z; Half = half; Owner = owner;
            Vector3 e = new(
                MathF.Abs(x.X) * half.X + MathF.Abs(y.X) * half.Y + MathF.Abs(z.X) * half.Z,
                MathF.Abs(x.Y) * half.X + MathF.Abs(y.Y) * half.Y + MathF.Abs(z.Y) * half.Z,
                MathF.Abs(x.Z) * half.X + MathF.Abs(y.Z) * half.Y + MathF.Abs(z.Z) * half.Z);
            Lo = centre - e;
            Hi = centre + e;
        }

        /// <summary>Signed distance from a point to the box: negative inside. <paramref name="thick"/>: no thinner than this either way.</summary>
        public float Distance(Vector3 p, float thick = 0f)
        {
            Vector3 l = p - Centre;
            float qx = MathF.Abs(l.Dot(X)) - MathF.Max(Half.X, thick), qy = MathF.Abs(l.Dot(Y)) - MathF.Max(Half.Y, thick), qz = MathF.Abs(l.Dot(Z)) - MathF.Max(Half.Z, thick);
            float ox = MathF.Max(qx, 0f), oy = MathF.Max(qy, 0f), oz = MathF.Max(qz, 0f);
            return MathF.Sqrt(ox * ox + oy * oy + oz * oz) + MathF.Min(MathF.Max(qx, MathF.Max(qy, qz)), 0f);
        }

        /// <summary>Signed distance from a segment to the box (it is convex along the segment, so a golden-section search finds it).</summary>
        public float Distance(Vector3 a, Vector3 b)
        {
            if ((b - a).LengthSquared() < 1e-8f) return Distance(a);
            const float G = 0.618034f;
            float lo = 0f, hi = 1f;
            float t1 = hi - G * (hi - lo), t2 = lo + G * (hi - lo);
            float f1 = Distance(a.Lerp(b, t1)), f2 = Distance(a.Lerp(b, t2));
            for (int i = 0; i < 18; i++)
            {
                if (f1 < f2) { hi = t2; t2 = t1; f2 = f1; t1 = hi - G * (hi - lo); f1 = Distance(a.Lerp(b, t1)); }
                else { lo = t1; t1 = t2; f1 = f2; t2 = lo + G * (hi - lo); f2 = Distance(a.Lerp(b, t2)); }
            }
            return MathF.Min(MathF.Min(f1, f2), MathF.Min(Distance(a), Distance(b)));
        }
    }

    /// <summary>How far round a part its cells know of it, m: the farthest a density or a body's radius reaches.</summary>
    public const float Reach = 1f;

    /// <summary>How far up a body's capsule starts over its feet: low enough that the litter at a body's edge is met, not stepped over.</summary>
    const float Lift = 0.04f;

    readonly List<Box> _boxes = new();
    readonly List<bool> _gone = new();
    readonly Dictionary<long, List<int>> _cells = new();
    readonly Dictionary<long, List<int>> _owned = new();
    readonly Dictionary<(long Cell, int Body, int Walls), Vector3?> _spots = new();
    readonly Dictionary<(long A, long B, int Body), bool> _sweeps = new();
    readonly object _lock = new();

    /// <summary>Parts held (not removed).</summary>
    public int Count { get; private set; }

    /// <summary>Every part held, as it collides.</summary>
    public IEnumerable<Box> All
    {
        get
        {
            for (int i = 0; i < _boxes.Count; i++) if (!_gone[i]) yield return _boxes[i];
        }
    }

    static long Key(int x, int y, int z) => ((long)(x + 0x100000) << 42) | ((long)(y + 0x100000) << 21) | (long)(z + 0x100000);
    static int Fl(float v) => (int)MathF.Floor(v);

    /// <summary>A part: an oriented box (unit axes, half sizes).</summary>
    public void Add(Vector3 centre, Vector3 x, Vector3 y, Vector3 z, Vector3 half, long owner)
    {
        if (half.X <= 0f || half.Y <= 0f || half.Z <= 0f) return;
        var box = new Box(centre, x, y, z, half, owner);
        lock (_lock)
        {
            int i = _boxes.Count;
            _boxes.Add(box);
            _gone.Add(false);
            Count++;
            for (int cz = Fl(box.Lo.Z - Reach); cz <= Fl(box.Hi.Z + Reach); cz++)
            for (int cy = Fl(box.Lo.Y - Reach); cy <= Fl(box.Hi.Y + Reach); cy++)
            for (int cx = Fl(box.Lo.X - Reach); cx <= Fl(box.Hi.X + Reach); cx++)
            {
                long k = Key(cx, cy, cz);
                if (!_cells.TryGetValue(k, out var l)) _cells[k] = l = new List<int>(4);
                l.Add(i);
            }
            if (!_owned.TryGetValue(owner, out var o)) _owned[owner] = o = new List<int>(2);
            o.Add(i);
            _spots.Clear();
            _sweeps.Clear();
            _vias.Clear();
        }
    }

    /// <summary>A part that stands square: a box from one corner to the other.</summary>
    public void AddBox(Vector3 lo, Vector3 hi, long owner) => Add((lo + hi) * 0.5f, Vector3.Right, Vector3.Up, Vector3.Back, (hi - lo) * 0.5f, owner);

    /// <summary>Everything one thing was made of goes (a block broken, a piece taken).</summary>
    public void Remove(long owner)
    {
        lock (_lock)
        {
            if (!_owned.Remove(owner, out var l)) return;
            foreach (int i in l)
                if (!_gone[i]) { _gone[i] = true; Count--; }
            _spots.Clear();
            _sweeps.Clear();
            _vias.Clear();
        }
    }

    /// <summary>The parts whose reach takes in a point's cell.</summary>
    List<int> Near(Vector3 p) => _cells.TryGetValue(Key(Fl(p.X), Fl(p.Y), Fl(p.Z)), out var l) ? l : null;

    /// <summary>Whether any part lies within reach of a point.</summary>
    public bool Any(Vector3 p) => Near(p) != null;

    /// <summary>How thick a part is at least, as a climber feels it (<see cref="BlockSpace.Density"/>), m.</summary>
    public const float Thick = 0.05f;

    /// <summary>Signed distance from a point to the nearest part (negative inside one); <see cref="Reach"/> where none is near. <paramref name="thick"/>: each felt at least this thick.</summary>
    public float Distance(Vector3 p, float thick = 0f)
    {
        List<int> l = Near(p);
        if (l == null) return Reach;
        float best = Reach;
        foreach (int i in l)
        {
            if (_gone[i]) continue;
            float d = _boxes[i].Distance(p, thick);
            if (d < best) best = d;
        }
        return best;
    }

    /// <summary>
    /// How far a standing body is clear of every part, m: its capsule (radius <paramref name="radius"/>,
    /// from the floor at <paramref name="feet"/> up to <paramref name="height"/>) against each box; negative
    /// where it is in one, by that much. <paramref name="ignore"/>: a thing the body is meant to be in
    /// (the seat it sits on).
    /// </summary>
    public float Gap(Vector3 feet, float radius, float height, long ignore = 0)
    {
        radius = MathF.Min(radius, Reach);
        Vector3 a = feet + Vector3.Up * MathF.Min(Lift, radius), b = feet + Vector3.Up * MathF.Max(MathF.Min(Lift, radius), height - radius);
        float best = Reach;
        float x0 = feet.X - radius, x1 = feet.X + radius, z0 = feet.Z - radius, z1 = feet.Z + radius, y1 = b.Y + radius;
        // The cells along its middle know every part that comes within a reach of it.
        int seen = 0;
        Span<int> done = stackalloc int[64];
        for (float y = a.Y; ; y += 0.9f)
        {
            float yy = MathF.Min(y, b.Y);
            List<int> l = Near(new Vector3(feet.X, yy, feet.Z));
            if (l != null)
                foreach (int i in l)
                {
                    if (_gone[i]) continue;
                    Box box = _boxes[i];
                    if (box.Owner == ignore && ignore != 0) continue;
                    if (box.Hi.X < x0 || box.Lo.X > x1 || box.Hi.Z < z0 || box.Lo.Z > z1 || box.Lo.Y > y1 || box.Hi.Y < feet.Y) continue;
                    bool again = false;
                    for (int k = 0; k < seen; k++) if (done[k] == i) { again = true; break; }
                    if (again) continue;
                    if (seen < done.Length) done[seen++] = i;
                    float d = box.Distance(a, b) - radius;
                    if (d < best) best = d;
                }
            if (yy >= b.Y) break;
        }
        return best;
    }

    /// <summary>Whether a standing body fits at a point (a hair of give for rounding).</summary>
    public bool Fits(Vector3 feet, float radius, float height, long ignore = 0) => Gap(feet, radius, height, ignore) >= -0.002f;

    static int BodyKey(float radius, float height) => (int)MathF.Round(radius * 100f) | (int)MathF.Round(height * 20f) << 12;

    static readonly Vector2[] Candidates = BuildCandidates();

    static Vector2[] BuildCandidates()
    {
        var l = new List<Vector2>();
        // Over the whole cell, its edges too: a big body among furniture may fit only astride two cells.
        for (int i = 0; i < 16; i++)
            for (int j = 0; j < 16; j++)
                l.Add(new Vector2(0.03125f + i * 0.0625f, 0.03125f + j * 0.0625f));
        l.Sort((p, q) => (p - new Vector2(0.5f, 0.5f)).LengthSquared().CompareTo((q - new Vector2(0.5f, 0.5f)).LengthSquared()));
        return l.ToArray();
    }

    /// <summary>
    /// Where in a cell a body standing on its floor (at <paramref name="floorY"/>) fits nearest its middle,
    /// or null where it fits nowhere in it. A cell no part comes near is its middle. <paramref name="walls"/>:
    /// the solid cells round it, kept <paramref name="wallRadius"/> off (a spot is never against a wall closer
    /// than the body is held off walls).
    /// </summary>
    public Vector3? Spot(int x, int y, int z, float floorY, float radius, float height, Func<int, int, int, bool> walls = null, float wallRadius = 0f)
    {
        var mid = new Vector3(x + 0.5f, floorY, z + 0.5f);
        if (!Touches(mid, radius, height)) return mid;
        var key = (Key(x, y, z), BodyKey(radius, height), WallsKey(walls, wallRadius));
        lock (_lock) if (_spots.TryGetValue(key, out Vector3? known)) return known;
        Vector3? found = null;
        foreach (Vector2 c in Candidates)
        {
            var p = new Vector3(x + c.X, floorY, z + c.Y);
            if (WallClear(p, height, walls, wallRadius) && Fits(p, radius, height)) { found = p; break; }
        }
        lock (_lock) _spots[key] = found;
        return found;
    }

    static int WallsKey(Func<int, int, int, bool> walls, float radius) =>
        walls == null ? 0 : HashCode.Combine(System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(walls.Target ?? walls.Method), walls.Method, (int)MathF.Round(radius * 100f));

    /// <summary>Whether a standing body is <paramref name="radius"/> clear (across) of every solid cell round it, from its feet to its head.</summary>
    public static bool WallClear(Vector3 feet, float height, Func<int, int, int, bool> walls, float radius)
    {
        if (walls == null || radius <= 0f) return true;
        int y0 = Fl(feet.Y + 0.01f), y1 = Fl(feet.Y + MathF.Max(0.02f, MathF.Min(height, 1.9f) - 0.01f));
        int x0 = Fl(feet.X - radius), x1 = Fl(feet.X + radius), z0 = Fl(feet.Z - radius), z1 = Fl(feet.Z + radius);
        float r2 = (radius - 0.002f) * (radius - 0.002f);
        for (int cy = y0; cy <= y1; cy++)
        for (int cz = z0; cz <= z1; cz++)
        for (int cx = x0; cx <= x1; cx++)
        {
            if (!walls(cx, cy, cz)) continue;
            float dx = MathF.Max(MathF.Max(cx - feet.X, feet.X - (cx + 1)), 0f), dz = MathF.Max(MathF.Max(cz - feet.Z, feet.Z - (cz + 1)), 0f);
            if (dx * dx + dz * dz < r2) return false;
        }
        return true;
    }
    static readonly Vector3[] Holds = BuildHolds();

    static Vector3[] BuildHolds()
    {
        var l = new List<Vector3>();
        for (int i = 0; i < 5; i++)
            for (int j = 0; j < 5; j++)
                for (int k = 0; k < 5; k++)
                    l.Add(new Vector3(0.1f + i * 0.2f, 0.1f + j * 0.2f, 0.1f + k * 0.2f));
        l.Sort((p, q) => (p - new Vector3(0.5f, 0.5f, 0.5f)).LengthSquared().CompareTo((q - new Vector3(0.5f, 0.5f, 0.5f)).LengthSquared()));
        return l.ToArray();
    }

    /// <summary>Where in a cell a climber's body (a ball) is clear of the parts, nearest its middle; null where nowhere is.</summary>
    public Vector3? Hold(int x, int y, int z, float radius)
    {
        var mid = new Vector3(x + 0.5f, y + 0.5f, z + 0.5f);
        if (!Any(mid)) return mid;
        var key = (Key(x, y, z), BodyKey(radius, 0f) | 1 << 30, 0);
        lock (_lock) if (_spots.TryGetValue(key, out Vector3? known)) return known;
        Vector3? found = null;
        foreach (Vector3 c in Holds)
        {
            var p = new Vector3(x, y, z) + c;
            if (Distance(p, Thick) >= radius) { found = p; break; }
        }
        lock (_lock) _spots[key] = found;
        return found;
    }

    /// <summary>Whether any part comes within reach of a standing body's capsule (else nothing here to check).</summary>
    public bool Touches(Vector3 feet, float radius, float height)
    {
        for (float y = 0f; ; y += 0.9f)
        {
            float yy = MathF.Min(y, height);
            if (Near(feet + Vector3.Up * yy) != null) return true;
            if (yy >= height) return false;
        }
    }

    /// <summary>Whether a body walking straight from one point to another fits all the way (sampled every 8 cm), and keeps off the walls as it goes.</summary>
    public bool Sweep(Vector3 from, Vector3 to, float radius, float height, long ignore = 0, Func<int, int, int, bool> walls = null, float wallRadius = 0f)
    {
        bool parts = Touches(from, radius, height) || Touches(to, radius, height) || Touches((from + to) * 0.5f, radius, height);
        if (!parts && walls == null) return true;
        float len = new Vector2(to.X - from.X, to.Z - from.Z).Length();
        int n = Math.Max(1, (int)MathF.Ceiling(len / 0.08f));
        for (int i = 0; i <= n; i++)
        {
            Vector3 p = from.Lerp(to, i / (float)n);
            if (parts && !Fits(p, radius, height, ignore) || !WallClear(p, height, walls, wallRadius)) return false;
        }
        return true;
    }

    /// <summary>Whether a body gets from one cell's spot to the next's (<see cref="Link"/>).</summary>
    public bool Step(Vector3I a, Vector3 from, Vector3I b, Vector3 to, float radius, float height, Func<int, int, int, bool> walls = null, float wallRadius = 0f) =>
        Link(a, from, b, to, radius, height, walls, wallRadius, out _);

    static readonly Vector3[] NoVias = Array.Empty<Vector3>();

    /// <summary>
    /// How a body gets from one cell's spot to the next's: straight (<paramref name="vias"/> empty), or, where a
    /// piece bars the straight way, round it by the points of a way found over the two cells (a 6 cm lattice of
    /// where the body fits, pulled straight between them); false where there is none. Remembered.
    /// </summary>
    public bool Link(Vector3I a, Vector3 from, Vector3I b, Vector3 to, float radius, float height, Func<int, int, int, bool> walls, float wallRadius, out Vector3[] vias)
    {
        var key = (Key(a.X, a.Y, a.Z), Key(b.X, b.Y, b.Z), BodyKey(radius, height) ^ WallsKey(walls, wallRadius) * 31);
        lock (_lock)
            if (_sweeps.TryGetValue(key, out bool known))
            {
                vias = _vias.TryGetValue(key, out Vector3[] v) ? v : NoVias;
                return known;
            }
        vias = NoVias;
        bool ok = Sweep(from, to, radius, height, 0, walls, wallRadius);
        if (!ok && a.Y == b.Y && (a.X == b.X) != (a.Z == b.Z))
        {
            List<Vector3> way = Round(a, from, b, to, radius, height, walls, wallRadius);
            if (way != null) { ok = true; vias = way.ToArray(); }
        }
        lock (_lock)
        {
            _sweeps[key] = ok;
            if (vias.Length > 0) _vias[key] = vias;
        }
        return ok;
    }

    readonly Dictionary<(long A, long B, int Body), Vector3[]> _vias = new();

    /// <summary>A way from where a body stands to a point a cell or two off, round whatever bars the straight way (its corners), or null. Not remembered.</summary>
    public List<Vector3> WayRound(Vector3 from, Vector3 to, float radius, float height, Func<int, int, int, bool> walls, float wallRadius)
    {
        var a = new Vector3I(Fl(from.X), Fl(from.Y + 0.05f), Fl(from.Z));
        var b = new Vector3I(Fl(to.X), Fl(to.Y + 0.05f), Fl(to.Z));
        if (Math.Abs(a.X - b.X) > 2 || Math.Abs(a.Z - b.Z) > 2) return null;
        return Round(a, from, b, to, radius, height, walls, wallRadius);
    }

    /// <summary>The way round what bars two neighbouring cells' points: a search over both cells, then pulled straight (the corners between them), or null.</summary>
    List<Vector3> Round(Vector3I a, Vector3 from, Vector3I b, Vector3 to, float radius, float height, Func<int, int, int, bool> walls, float wallRadius)
    {
        const float S = 0.0625f;
        float y = MathF.Min(from.Y, to.Y);
        int x0 = Math.Min(a.X, b.X), z0 = Math.Min(a.Z, b.Z);
        int nx = (Math.Max(a.X, b.X) - x0 + 1) * 16, nz = (Math.Max(a.Z, b.Z) - z0 + 1) * 16;
        Vector3 At(int i, int j) => new(x0 + (i + 0.5f) * S, y, z0 + (j + 0.5f) * S);
        var fits = new sbyte[nx * nz]; // 0 not asked, 1 fits, -1 doesn't
        bool Ok(int i, int j)
        {
            if (i < 0 || j < 0 || i >= nx || j >= nz) return false;
            ref sbyte f = ref fits[j * nx + i];
            if (f == 0) { Vector3 p = At(i, j); f = WallClear(p, height, walls, wallRadius) && Fits(p, radius, height) ? (sbyte)1 : (sbyte)-1; }
            return f > 0;
        }
        (int, int) Nearest(Vector3 p)
        {
            int bi = -1, bj = -1;
            float best = float.MaxValue;
            int ci = (int)((p.X - x0) / S), cj = (int)((p.Z - z0) / S);
            for (int dj = -3; dj <= 3; dj++)
                for (int di = -3; di <= 3; di++)
                {
                    int i = ci + di, j = cj + dj;
                    if (!Ok(i, j)) continue;
                    float d = (At(i, j) - p).LengthSquared();
                    if (d < best && Sweep(p, At(i, j), radius, height, 0, walls, wallRadius)) { best = d; bi = i; bj = j; }
                }
            return (bi, bj);
        }
        var (si, sj) = Nearest(from);
        var (gi, gj) = Nearest(to);
        if (si < 0 || gi < 0) return null;
        var came = new int[nx * nz];
        Array.Fill(came, -1);
        var q = new Queue<int>();
        int start = sj * nx + si, goal = gj * nx + gi;
        came[start] = start;
        q.Enqueue(start);
        while (q.Count > 0 && came[goal] < 0)
        {
            int k = q.Dequeue(), i = k % nx, j = k / nx;
            for (int dj = -1; dj <= 1; dj++)
                for (int di = -1; di <= 1; di++)
                {
                    if (di == 0 && dj == 0) continue;
                    int ni = i + di, nj = j + dj;
                    if (!Ok(ni, nj) || came[nj * nx + ni] >= 0) continue;
                    if (di != 0 && dj != 0 && (!Ok(i + di, j) || !Ok(i, j + dj))) continue;
                    came[nj * nx + ni] = k;
                    q.Enqueue(nj * nx + ni);
                }
        }
        if (came[goal] < 0) return null;
        var cells = new List<Vector3> { to };
        for (int k = goal; ; k = came[k])
        {
            cells.Add(At(k % nx, k / nx));
            if (k == start) break;
        }
        cells.Add(from);
        cells.Reverse();
        // Pulled straight: from each corner, on to the farthest point it sees.
        var corners = new List<Vector3>();
        int at = 0;
        while (at < cells.Count - 1)
        {
            int next = at + 1;
            for (int k = cells.Count - 1; k > at + 1; k--)
                if (Sweep(cells[at], cells[k], radius, height, 0, walls, wallRadius)) { next = k; break; }
            if (next < cells.Count - 1) corners.Add(cells[next]);
            at = next;
        }
        return corners;
    }
    /// <summary>
    /// A standing body moved sideways out of any part it is in, the least way out; it stays where it
    /// is if it is clear. Returns where it ends (still in one only if there was no way out near).
    /// </summary>
    public Vector3 PushOut(Vector3 feet, float radius, float height, long ignore = 0)
    {
        for (int k = 0; k < 6; k++)
        {
            float gap = Gap(feet, radius, height, ignore);
            if (gap >= 0f) return feet;
            const float E = 0.02f;
            float gx = Gap(feet + new Vector3(E, 0, 0), radius, height, ignore) - Gap(feet - new Vector3(E, 0, 0), radius, height, ignore);
            float gz = Gap(feet + new Vector3(0, 0, E), radius, height, ignore) - Gap(feet - new Vector3(0, 0, E), radius, height, ignore);
            var dir = new Vector3(gx, 0f, gz);
            if (dir.LengthSquared() < 1e-8f)
            {
                // Deep in one: out the way from its middle.
                Vector3 from = feet;
                float best = float.MaxValue;
                foreach (Box b in All)
                {
                    float d = b.Distance(feet + Vector3.Up * MathF.Min(height * 0.5f, 0.5f));
                    if (d < best) { best = d; from = feet - b.Centre; }
                }
                dir = new Vector3(from.X, 0f, from.Z);
                if (dir.LengthSquared() < 1e-8f) dir = Vector3.Right;
            }
            feet += dir.Normalized() * MathF.Min(0.25f, -gap + 0.006f);
        }
        return feet;
    }

    /// <summary>
    /// What sort of thing a part-built piece is, for walkers, from its boxes over its floor (cell-local
    /// heights): against a wall up high (the floor before it stays open), low (stepped round, gone under
    /// or over by the small), part-deep (only what it fills is taken) or a whole cell.
    /// </summary>
    public static string KindOf(IEnumerable<(Vector3 Lo, Vector3 Hi)> boxes)
    {
        float lo = float.MaxValue, top = 0f, x0 = float.MaxValue, x1 = float.MinValue, z0 = float.MaxValue, z1 = float.MinValue;
        foreach (var (l, h) in boxes)
        {
            lo = MathF.Min(lo, l.Y);
            top = MathF.Max(top, h.Y);
            x0 = MathF.Min(x0, l.X); z0 = MathF.Min(z0, l.Z);
            x1 = MathF.Max(x1, h.X); z1 = MathF.Max(z1, h.Z);
        }
        if (lo == float.MaxValue) return "none";
        float foot = (x1 - x0) * (z1 - z0);
        if (lo > 0.6f) return "wall-mounted";
        if (top < 0.95f && foot < 0.95f) return "low";
        if (foot < 0.8f) return "part-deep";
        return "full";
    }
}
