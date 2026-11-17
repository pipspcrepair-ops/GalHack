using System;
using System.Collections.Generic;
using Godot;

namespace Wildlife;

/// <summary>A round thing creatures walk around: a rock, a tree trunk.</summary>
public sealed class Obstacle
{
    public int Id;
    public Vector3 Position;
    public float Radius;
    public bool Active = true;
}

/// <summary>
/// The grid creatures out on the ground go by (<see cref="Brain.Go"/>): coarse 3 m cells marked by the rocks and trunks
/// on them, searched with a budgeted A* and string-pulled to a few waypoints; felling or regrowing a tree updates the
/// cells it covers. Among buildings (a place standing on the ground, a house by the road) the cells are too coarse for
/// a doorway, so the way is found on a fine window of 1 m cells round the body (<see cref="Window"/>), blocked where the
/// built blocks stand as well as by the rocks and trunks. And a flight is planned on it too (<see cref="Away"/>).
/// Every move from a cell to the next is also one the body's legs can make (<see cref="Gait"/>, owner: "wall climbers
/// should be able to go up and down those, but not a person or regular animal"): up or down ground no steeper than its
/// limit (steep but passable ground costing more, so a way prefers the easy way round), never off an edge higher than
/// it can drop; climbers, fliers and leapers go where they like. One height a cell (<see cref="Ground"/>) serves every gait.
/// </summary>
public sealed class NavGrid
{
    /// <summary>
    /// What a body's legs manage between cells: the steepest ground it walks up or down (rise over run), and the highest
    /// edge it steps or hops off. The default is anything (a climber, a flier, a leaper).
    /// </summary>
    public readonly record struct Gait(float Slope, float Drop)
    {
        public bool Any => Slope <= 0f;
    }

    public readonly float CellSize;
    public readonly int Size;
    float _ox, _oz;                          // the grid's corner (a fine window's moves with the body it is for)
    readonly int[] _blocked;                 // how many obstacles cover each cell
    readonly List<Obstacle>[] _buckets;      // obstacles touching each cell, for local avoidance
    readonly List<Obstacle> _obstacles = new();
    // The ground's height at each cell's middle (a fine window's worked out as it is read), and the gait this search is for.
    readonly float[] _h;
    Terrain _ground;
    Gait _gait;

    // A* and flood scratch, reused between searches so pathfinding never allocates per call.
    readonly float[] _g, _t;
    readonly int[] _from;
    readonly int[] _visit, _closedAt, _tAt;
    int _search;
    readonly PriorityQueue<int, float> _open = new();
    readonly List<int> _cells = new();

    // A fine window: the coarse grid whose rocks and trunks block it too, what is built, and each cell's verdict this search.
    readonly NavGrid _coarse;
    Func<Vector3, bool> _built;
    float _body;
    readonly int[] _known;
    int _stamp;
    NavGrid _fine;

    /// <summary>A fine window's side, in 1 m cells.</summary>
    public const int WindowSize = 96;

    public int Searches { get; private set; }

    public NavGrid(float halfSize, float cellSize = 3f)
    {
        CellSize = cellSize;
        Size = (int)MathF.Ceiling(halfSize * 2f / cellSize);
        _ox = _oz = -halfSize;
        int n = Size * Size;
        _blocked = new int[n];
        _buckets = new List<Obstacle>[n];
        _h = new float[n];
        (_g, _t, _from, _visit, _closedAt, _tAt) = (new float[n], new float[n], new int[n], new int[n], new int[n], new int[n]);
    }

    NavGrid(NavGrid coarse)
    {
        CellSize = 1f;
        Size = WindowSize;
        _coarse = coarse;
        int n = Size * Size;
        _blocked = new int[n];
        _known = new int[n];
        _h = new float[n];
        (_g, _t, _from, _visit, _closedAt, _tAt) = (new float[n], new float[n], new int[n], new int[n], new int[n], new int[n]);
    }

    /// <summary>The ground the grid lies on: each cell's height at its middle, what a gait's moves are judged by.</summary>
    public void Ground(Terrain t)
    {
        _ground = t;
        for (int z = 0; z < Size; z++)
        for (int x = 0; x < Size; x++)
        {
            Vector3 c = Center(x, z);
            _h[z * Size + x] = t.Height(c.X, c.Z);
        }
    }

    /// <summary>
    /// The fine window round a point (made once, then moved): 1 m cells blocked where <paramref name="built"/> says a
    /// point of the ground is built on, or a rock or trunk of this grid comes within <paramref name="body"/> of the cell's middle.
    /// </summary>
    public NavGrid Window(Vector3 centre, Func<Vector3, bool> built, float body)
    {
        _fine ??= new NavGrid(this);
        _fine._ox = MathF.Floor(centre.X) - WindowSize / 2;
        _fine._oz = MathF.Floor(centre.Z) - WindowSize / 2;
        _fine._built = built;
        _fine._body = body;
        if (++_fine._stamp >= int.MaxValue / 4) { Array.Clear(_fine._known); _fine._stamp = 1; }
        return _fine;
    }

    public IReadOnlyList<Obstacle> Obstacles => _obstacles;

    public Obstacle AddObstacle(Vector3 position, float radius)
    {
        var o = new Obstacle { Id = _obstacles.Count, Position = position, Radius = radius };
        _obstacles.Add(o);
        Mark(o, +1);
        return o;
    }

    public void SetActive(Obstacle o, bool active)
    {
        if (o.Active == active) return;
        o.Active = active;
        Mark(o, active ? +1 : -1);
    }

    void Mark(Obstacle o, int delta)
    {
        // A cell counts as blocked when its centre is inside the obstacle plus a little margin;
        // the bucket lists every obstacle that touches the cell at all.
        float reach = o.Radius + CellSize;
        (int ox, int oz) = Cell(o.Position);
        (int x0, int z0) = Cell(o.Position - new Vector3(reach, 0, reach));
        (int x1, int z1) = Cell(o.Position + new Vector3(reach, 0, reach));
        for (int z = Math.Max(0, z0); z <= Math.Min(Size - 1, z1); z++)
        for (int x = Math.Max(0, x0); x <= Math.Min(Size - 1, x1); x++)
        {
            Vector3 c = Center(x, z);
            float d = new Vector2(c.X - o.Position.X, c.Z - o.Position.Z).Length();
            int i = z * Size + x;
            // A thin trunk still blocks the cell it stands in.
            if (d < o.Radius + 0.6f || (x == ox && z == oz)) _blocked[i] = Math.Max(0, _blocked[i] + delta);
            if (d < o.Radius + CellSize)
            {
                _buckets[i] ??= new List<Obstacle>();
                if (delta > 0 && !_buckets[i].Contains(o)) _buckets[i].Add(o);
            }
        }
    }

    public (int x, int z) Cell(Vector3 p) =>
        ((int)MathF.Floor((p.X - _ox) / CellSize), (int)MathF.Floor((p.Z - _oz) / CellSize));

    public Vector3 Center(int x, int z) => new(_ox + (x + 0.5f) * CellSize, 0f, _oz + (z + 0.5f) * CellSize);

    bool InGrid(int x, int z) => x >= 0 && z >= 0 && x < Size && z < Size;

    public bool Blocked(int x, int z) => InGrid(x, z) ? (_coarse == null ? _blocked[z * Size + x] > 0 : (Known(x, z) & 1) != 0) : true;

    /// <summary>In a fine window, whether a cell is built on (out past the window, nothing is known to be).</summary>
    bool BuiltAt(int x, int z) => _coarse != null && InGrid(x, z) && (Known(x, z) & 2) != 0;

    /// <summary>A fine window's cell, worked out once a search: bit 1 blocked (built on, or a rock or trunk near), bit 2 built on.</summary>
    int Known(int x, int z)
    {
        int i = z * Size + x, k = _known[i];
        if (k >> 2 == _stamp) return k & 3;
        Vector3 p = Center(x, z);
        bool built = _built(p);
        k = built ? 3 : _coarse.Rock(p, _body) ? 1 : 0;
        _known[i] = _stamp << 2 | k;
        _h[i] = _coarse._ground?.Height(p.X, p.Z) ?? 0f;
        return k;
    }

    /// <summary>
    /// Whether this search's gait takes a body from one cell to a neighbour this far off (m): up no steeper than its limit,
    /// down no steeper either unless the drop is one it steps or hops off; and how much more than its length the move costs
    /// (steep ground is slower going, so a way bends round it where it can).
    /// </summary>
    bool Legs(int from, int to, float run, out float cost)
    {
        cost = 1f;
        if (_gait.Any || _ground == null && _coarse?._ground == null) return true;
        if (_coarse != null) { Known(from % Size, from / Size); Known(to % Size, to / Size); }
        float rise = _h[to] - _h[from], slope = MathF.Abs(rise) / run;
        if (slope > _gait.Slope && (rise > 0f || -rise > _gait.Drop)) return false;
        float k = MathF.Min(slope / _gait.Slope, 1f);
        cost = 1f + 2f * k * k;
        return true;
    }

    public bool Blocked(Vector3 p)
    {
        (int x, int z) = Cell(p);
        return Blocked(x, z);
    }

    /// <summary>Whether an active rock or trunk comes within a body's radius of a point.</summary>
    public bool Rock(Vector3 p, float radius)
    {
        foreach (Obstacle o in Around(p, radius))
            if (o.Active && new Vector2(p.X - o.Position.X, p.Z - o.Position.Z).Length() < o.Radius + radius) return true;
        return false;
    }

    /// <summary>Active obstacles near a point, for pushing creatures out of trunks and rocks.</summary>
    public List<Obstacle> Near(Vector3 p)
    {
        (int x, int z) = Cell(p);
        return InGrid(x, z) ? _buckets[z * Size + x] : null;
    }

    readonly List<Obstacle> _around = new();

    /// <summary>
    /// Obstacles that may come within a reach of a point, for pushing a body of that reach out of trunks and rocks:
    /// the buckets of the cells round it (one cell for anything up to a person's size). A list reused by every call.
    /// </summary>
    public List<Obstacle> Around(Vector3 p, float reach)
    {
        _around.Clear();
        float r = MathF.Max(0f, reach - CellSize * 0.3f);
        (int x0, int z0) = Cell(p - new Vector3(r, 0f, r));
        (int x1, int z1) = Cell(p + new Vector3(r, 0f, r));
        for (int z = Math.Max(0, z0); z <= Math.Min(Size - 1, z1); z++)
        for (int x = Math.Max(0, x0); x <= Math.Min(Size - 1, x1); x++)
            if (_buckets[z * Size + x] is { } b) _around.AddRange(b);
        return _around;
    }

    /// <summary>
    /// True when no blocked cell lies on the straight line (a supercover walk) and a body of this gait gets from each cell
    /// on it to the next; <paramref name="built"/>: only that no built-on cell of a fine window does.
    /// </summary>
    public bool LineClear(Vector3 a, Vector3 b, bool built = false, Gait gait = default)
    {
        _gait = built ? default : gait;
        return Line(a, b, built);
    }

    bool Line(Vector3 a, Vector3 b, bool built)
    {
        (int x, int z) = Cell(a);
        (int x1, int z1) = Cell(b);
        float ax = (a.X - _ox) / CellSize, az = (a.Z - _oz) / CellSize;
        float dx = (b.X - _ox) / CellSize - ax, dz = (b.Z - _oz) / CellSize - az;
        int sx = Math.Sign(dx), sz = Math.Sign(dz);
        float tx = dx != 0f ? ((sx > 0 ? x + 1 - ax : ax - x) / MathF.Abs(dx)) : float.MaxValue;
        float tz = dz != 0f ? ((sz > 0 ? z + 1 - az : az - z) / MathF.Abs(dz)) : float.MaxValue;
        float stepX = dx != 0f ? 1f / MathF.Abs(dx) : float.MaxValue, stepZ = dz != 0f ? 1f / MathF.Abs(dz) : float.MaxValue;
        bool first = true;
        int was = -1;
        for (int guard = 0; guard < Size * 2; guard++)
        {
            if (!first && (built ? BuiltAt(x, z) : Blocked(x, z))) return false; // a creature may start inside a blocked cell
            // (Too steep from the cell before, or off too high an edge: not a straight way for these legs.)
            if (!first && !_gait.Any && InGrid(x, z) && was >= 0 && !Legs(was, z * Size + x, CellSize, out _)) return false;
            first = false;
            was = InGrid(x, z) ? z * Size + x : -1;
            if (x == x1 && z == z1 || MathF.Min(tx, tz) > 1f) return true; // (the line ends in this cell: a walk that rounding carried past its last cell would run on to the grid's edge)
            if (tx < tz) { tx += stepX; x += sx; }
            else { tz += stepZ; z += sz; }
        }
        return true;
    }

    /// <summary>A point held inside the grid, a margin of cells in from its edge (a fine window's far goal is aimed at its nearest edge).</summary>
    public Vector3 Inside(Vector3 p, int margin = 1) =>
        new(Math.Clamp(p.X, _ox + margin * CellSize, _ox + (Size - margin) * CellSize), p.Y, Math.Clamp(p.Z, _oz + margin * CellSize, _oz + (Size - margin) * CellSize));

    /// <summary>
    /// A* over the grid with 8-way moves (no cutting corners), then string-pulled to a few
    /// waypoints. Bounded: a search that runs out of budget returns the path to the closest
    /// cell it reached, which is still progress.
    /// </summary>
    public bool FindPath(Vector3 from, Vector3 to, List<Vector3> path, int budget = 2500, Gait gait = default)
    {
        Searches++;
        _gait = gait;
        path.Clear();
        (int sx, int sz) = Cell(from);
        (int gx, int gz) = Cell(to);
        if (!InGrid(sx, sz)) return false;
        if (Blocked(gx, gz) && !NearestFree(ref gx, ref gz)) return false;
        int start = sz * Size + sx, goal = gz * Size + gx;

        Begin(start);
        _open.Enqueue(start, Heuristic(sx, sz, gx, gz));
        int best = start;
        float bestH = float.MaxValue;
        int expanded = 0;

        while (_open.Count > 0 && expanded < budget)
        {
            int cur = _open.Dequeue();
            if (_closedAt[cur] == _search) continue;
            _closedAt[cur] = _search;
            expanded++;
            int cx = cur % Size, cz = cur / Size;
            float h = Heuristic(cx, cz, gx, gz);
            if (h < bestH) { bestH = h; best = cur; }
            if (cur == goal) break;
            for (int dz = -1; dz <= 1; dz++)
            for (int dx = -1; dx <= 1; dx++)
            {
                if (!Step(cx, cz, dx, dz, out int next, out float step)) continue;
                float g = _g[cur] + step;
                if (_visit[next] == _search && g >= _g[next]) continue;
                Reach(next, cur, g);
                _open.Enqueue(next, g + Heuristic(next % Size, next / Size, gx, gz));
            }
        }
        int end = _closedAt[goal] == _search ? goal : best;
        Pull(from, end, end == goal ? to : Center(end % Size, end / Size), path);
        return end == goal;
    }

    /// <summary>
    /// A flight on the grid: from <paramref name="from"/> to the cell within <paramref name="reach"/> m of the way
    /// round that is farthest from <paramref name="threat"/> by the way round (its own flood: out of a pocket by its
    /// opening, never past the threat to get there), held within <paramref name="keepRadius"/> of
    /// <paramref name="keep"/> if given (a pen). The way there into <paramref name="path"/>; false where nowhere it can
    /// reach is farther (cornered).
    /// </summary>
    public bool Away(Vector3 from, Vector3 threat, float reach, List<Vector3> path, int budget = 500, Vector3 keep = default, float keepRadius = float.MaxValue, Gait gait = default)
    {
        Searches++;
        _gait = gait;
        path.Clear();
        (int sx, int sz) = Cell(from);
        if (!InGrid(sx, sz)) return false;
        int start = sz * Size + sx;
        // How far every cell near is from the threat by the way round; past what that flood reached, at least as far as it went.
        float tFar = Flood(threat, budget);
        float T(int i) => _tAt[i] == _tStamp ? _t[i] : MathF.Max(tFar, Straight(i, threat));
        float t0 = T(start);
        // Then out from the body: stepping nearer the threat costs four times over, and the best is the cell farthest
        // from it by the way round, less a little for how far the body has to run to get there.
        Begin(start);
        _open.Enqueue(start, 0f);
        int best = -1, expanded = 0;
        float bestScore = float.MinValue, cells = reach / CellSize;
        while (_open.Count > 0 && expanded < budget)
        {
            int cur = _open.Dequeue();
            if (_closedAt[cur] == _search) continue;
            _closedAt[cur] = _search;
            expanded++;
            float tc = T(cur), score = tc - 0.35f * _g[cur];
            if (cur != start && tc > t0 + 1f && score > bestScore) { bestScore = score; best = cur; }
            if (_g[cur] >= cells) continue;
            int cx = cur % Size, cz = cur / Size;
            for (int dz = -1; dz <= 1; dz++)
            for (int dx = -1; dx <= 1; dx++)
            {
                if (!Step(cx, cz, dx, dz, out int next, out float step)) continue;
                if (keepRadius < float.MaxValue && Straight(next, keep) * CellSize > keepRadius) continue;
                float g = _g[cur] + step * (T(next) < tc ? 4f : 1f);
                if (_visit[next] == _search && g >= _g[next]) continue;
                Reach(next, cur, g);
                _open.Enqueue(next, g);
            }
        }
        if (best < 0) return false;
        Pull(from, best, Center(best % Size, best / Size), path);
        return true;
    }

    /// <summary>The threat's own flood (Dijkstra, bounded): each cell's distance from it by the way round, in cells; how far it went.</summary>
    float Flood(Vector3 threat, int budget)
    {
        _search++;
        _open.Clear();
        (int x, int z) = Cell(threat);
        if (!InGrid(x, z)) return 0f;
        if (Blocked(x, z) && !NearestFree(ref x, ref z)) return 0f;
        int start = z * Size + x, done = 0;
        float far = 0f;
        _tStamp = _search;
        _t[start] = 0f;
        _tAt[start] = _search;
        _open.Enqueue(start, 0f);
        while (_open.Count > 0 && done < budget)
        {
            int cur = _open.Dequeue();
            if (_closedAt[cur] == _search) continue;
            _closedAt[cur] = _search;
            done++;
            far = MathF.Max(far, _t[cur]);
            int cx = cur % Size, cz = cur / Size;
            for (int dz = -1; dz <= 1; dz++)
            for (int dx = -1; dx <= 1; dx++)
            {
                if (!Step(cx, cz, dx, dz, out int next, out float step)) continue;
                float g = _t[cur] + step;
                if (_tAt[next] == _search && g >= _t[next]) continue;
                _t[next] = g;
                _tAt[next] = _search;
                _open.Enqueue(next, g);
            }
        }
        return far; // (its distances stay stamped with its own search; the next search's stamp clears its settled marks)
    }

    int _tStamp;

    float Straight(int i, Vector3 p) => new Vector2(Center(i % Size, i / Size).X - p.X, Center(i % Size, i / Size).Z - p.Z).Length() / CellSize;

    /// <summary>A move from a cell to a neighbour: in the grid, not blocked, not cutting a blocked corner, one this search's gait makes; its cost in cells (its length, more on steep ground).</summary>
    bool Step(int cx, int cz, int dx, int dz, out int next, out float step)
    {
        next = -1;
        step = 0f;
        if (dx == 0 && dz == 0) return false;
        int nx = cx + dx, nz = cz + dz;
        if (Blocked(nx, nz)) return false;
        if (dx != 0 && dz != 0 && (Blocked(cx + dx, cz) || Blocked(cx, cz + dz))) return false;
        next = nz * Size + nx;
        if (_closedAt[next] == _search) return false;
        step = dx != 0 && dz != 0 ? 1.4142f : 1f;
        if (!Legs(cz * Size + cx, next, step * CellSize, out float steep)) return false;
        step *= steep;
        return true;
    }

    void Begin(int start)
    {
        _search++;
        _open.Clear();
        _g[start] = 0f;
        _from[start] = -1;
        _visit[start] = _search;
    }

    void Reach(int next, int from, float g)
    {
        _visit[next] = _search;
        _g[next] = g;
        _from[next] = from;
    }

    /// <summary>The cells back from one to the start, string-pulled: a waypoint only where the straight line from the last one breaks; the last point given.</summary>
    void Pull(Vector3 from, int end, Vector3 last, List<Vector3> path)
    {
        _cells.Clear();
        for (int c = end; c != -1; c = _from[c]) _cells.Add(c);
        _cells.Reverse();
        Vector3 anchor = from;
        for (int i = 1; i < _cells.Count; i++)
        {
            Vector3 next = Center(_cells[i] % Size, _cells[i] / Size);
            Vector3 after = i + 1 < _cells.Count ? Center(_cells[i + 1] % Size, _cells[i + 1] / Size) : last;
            if (!Line(anchor, after, false))
            {
                path.Add(next);
                anchor = next;
            }
        }
        path.Add(last);
    }

    bool NearestFree(ref int x, ref int z)
    {
        for (int r = 1; r < 6; r++)
        for (int dz = -r; dz <= r; dz++)
        for (int dx = -r; dx <= r; dx++)
        {
            if (Math.Abs(dx) != r && Math.Abs(dz) != r) continue;
            if (!Blocked(x + dx, z + dz))
            {
                x += dx;
                z += dz;
                return true;
            }
        }
        return false;
    }

    static float Heuristic(int x, int z, int gx, int gz)
    {
        int dx = Math.Abs(x - gx), dz = Math.Abs(z - gz);
        return Math.Max(dx, dz) + 0.4142f * Math.Min(dx, dz); // octile
    }
}
