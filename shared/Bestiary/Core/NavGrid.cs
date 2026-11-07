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
/// The coarse grid creatures navigate by. Most of the time they steer straight at their goal;
/// only when a rock or a grove is in the way (a line test on this grid) do they ask A* for a
/// path, and the path is smoothed down to a few waypoints. Felling or regrowing a tree updates
/// the cells it covers, so the grid always matches the forest.
/// </summary>
public sealed class NavGrid
{
    public readonly float CellSize;
    public readonly int Size;
    readonly float _origin;
    readonly int[] _blocked;                 // how many obstacles cover each cell
    readonly List<Obstacle>[] _buckets;      // obstacles touching each cell, for local avoidance
    readonly List<Obstacle> _obstacles = new();

    // A* scratch, reused between searches so pathfinding never allocates per call.
    readonly float[] _g;
    readonly int[] _from;
    readonly int[] _visit;
    readonly int[] _closedAt;
    int _search;
    readonly PriorityQueue<int, float> _open = new();
    readonly List<int> _cells = new();

    public int Searches { get; private set; }

    public NavGrid(float halfSize, float cellSize = 3f)
    {
        CellSize = cellSize;
        Size = (int)MathF.Ceiling(halfSize * 2f / cellSize);
        _origin = -halfSize;
        int n = Size * Size;
        _blocked = new int[n];
        _buckets = new List<Obstacle>[n];
        _g = new float[n];
        _from = new int[n];
        _visit = new int[n];
        _closedAt = new int[n];
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
        ((int)MathF.Floor((p.X - _origin) / CellSize), (int)MathF.Floor((p.Z - _origin) / CellSize));

    public Vector3 Center(int x, int z) => new(_origin + (x + 0.5f) * CellSize, 0f, _origin + (z + 0.5f) * CellSize);

    bool InGrid(int x, int z) => x >= 0 && z >= 0 && x < Size && z < Size;

    public bool Blocked(int x, int z) => !InGrid(x, z) || _blocked[z * Size + x] > 0;

    public bool Blocked(Vector3 p)
    {
        (int x, int z) = Cell(p);
        return Blocked(x, z);
    }

    /// <summary>Active obstacles near a point, for pushing creatures out of trunks and rocks.</summary>
    public List<Obstacle> Near(Vector3 p)
    {
        (int x, int z) = Cell(p);
        return InGrid(x, z) ? _buckets[z * Size + x] : null;
    }

    /// <summary>True when no blocked cell lies on the straight line (a supercover walk).</summary>
    public bool LineClear(Vector3 a, Vector3 b)
    {
        (int x, int z) = Cell(a);
        (int x1, int z1) = Cell(b);
        float ax = (a.X - _origin) / CellSize, az = (a.Z - _origin) / CellSize;
        float dx = (b.X - _origin) / CellSize - ax, dz = (b.Z - _origin) / CellSize - az;
        int sx = Math.Sign(dx), sz = Math.Sign(dz);
        float tx = dx != 0f ? ((sx > 0 ? x + 1 - ax : ax - x) / MathF.Abs(dx)) : float.MaxValue;
        float tz = dz != 0f ? ((sz > 0 ? z + 1 - az : az - z) / MathF.Abs(dz)) : float.MaxValue;
        float stepX = dx != 0f ? 1f / MathF.Abs(dx) : float.MaxValue, stepZ = dz != 0f ? 1f / MathF.Abs(dz) : float.MaxValue;
        bool first = true;
        for (int guard = 0; guard < Size * 2; guard++)
        {
            if (!first && Blocked(x, z)) return false; // a creature may start inside a blocked cell
            first = false;
            if (x == x1 && z == z1) return true;
            if (tx < tz) { tx += stepX; x += sx; }
            else { tz += stepZ; z += sz; }
        }
        return true;
    }

    /// <summary>
    /// A* over the grid with 8-way moves (no cutting corners), then string-pulled to a few
    /// waypoints. Bounded: a search that runs out of budget returns the path to the closest
    /// cell it reached, which is still progress.
    /// </summary>
    public bool FindPath(Vector3 from, Vector3 to, List<Vector3> path, int budget = 2500)
    {
        Searches++;
        path.Clear();
        (int sx, int sz) = Cell(from);
        (int gx, int gz) = Cell(to);
        if (!InGrid(sx, sz)) return false;
        if (Blocked(gx, gz) && !NearestFree(ref gx, ref gz)) return false;
        int start = sz * Size + sx, goal = gz * Size + gx;

        _search++;
        _open.Clear();
        _g[start] = 0f;
        _from[start] = -1;
        _visit[start] = _search;
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
                if (dx == 0 && dz == 0) continue;
                int nx = cx + dx, nz = cz + dz;
                if (Blocked(nx, nz)) continue;
                if (dx != 0 && dz != 0 && (Blocked(cx + dx, cz) || Blocked(cx, cz + dz))) continue;
                int next = nz * Size + nx;
                if (_closedAt[next] == _search) continue;
                float g = _g[cur] + (dx != 0 && dz != 0 ? 1.4142f : 1f);
                if (_visit[next] == _search && g >= _g[next]) continue;
                _visit[next] = _search;
                _g[next] = g;
                _from[next] = cur;
                _open.Enqueue(next, g + Heuristic(nx, nz, gx, gz));
            }
        }

        int end = _closedAt[goal] == _search ? goal : best;
        _cells.Clear();
        for (int c = end; c != -1; c = _from[c]) _cells.Add(c);
        _cells.Reverse();

        // String pulling: keep a waypoint only where the straight line from the last one breaks.
        Vector3 anchor = from;
        for (int i = 1; i < _cells.Count; i++)
        {
            Vector3 next = Center(_cells[i] % Size, _cells[i] / Size);
            Vector3 after = i + 1 < _cells.Count ? Center(_cells[i + 1] % Size, _cells[i + 1] / Size) : (end == goal ? to : next);
            if (!LineClear(anchor, after))
            {
                path.Add(next);
                anchor = next;
            }
        }
        path.Add(end == goal ? to : Center(end % Size, end / Size));
        return end == goal;
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
