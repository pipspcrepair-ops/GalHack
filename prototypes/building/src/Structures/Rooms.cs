using System;
using System.Collections.Generic;
using System.Linq;

namespace GalHack.Structures;

/// <summary>The face of <see cref="Cell"/> that looks towards <see cref="Dir"/>.</summary>
public readonly record struct Face(Int3 Cell, Dir Dir);

public sealed class Room
{
    public readonly List<Int3> Cells = new();
    public bool Sealed;

    /// <summary>For a leaking room: the fewest faces that would seal it, nearest the outside.</summary>
    public readonly List<Face> Leaks = new();

    /// <summary>More holes than are worth listing: the room is open to the outside.</summary>
    public bool WideOpen;
}

public sealed class RoomReport
{
    public readonly List<Room> Rooms = new();
    public IEnumerable<Room> Sealed => Rooms.Where(r => r.Sealed);
    public IEnumerable<Room> Leaking => Rooms.Where(r => !r.Sealed);
    public IEnumerable<Face> LeakFaces => Rooms.SelectMany(r => r.Leaks);
}

/// <summary>
/// Finds rooms by flood-filling air through the block grid. Air crosses a face unless a block on
/// either side seals it (a full, airtight face: armour, glass, a closed door). Only the boundary
/// matters, so ladders, furniture and components inside a room never break its seal.
///
/// 1. Flood from outside the structure's bounds. Air it cannot reach is a sealed room.
/// 2. Leaking rooms are the reachable air that the hull still encloses: cells with a block
///    somewhere along all six axis rays. For each, the leaks are a minimum cut between the room
///    and the outside (max-flow over unit faces), taken nearest the outside. That is exactly
///    the fewest faces to fill to seal the room: a missing hull block reports its own face, an
///    open door reports the doorway.
/// </summary>
public static class Rooms
{
    public const int MaxLeaks = 48;
    const int MaxCells = 4_000_000;

    public static RoomReport Detect(Structure s)
    {
        var report = new RoomReport();
        if (s.Count == 0) return report;
        var (bmin, bmax) = s.Bounds();
        var grid = new AirGrid(s, bmin - new Int3(1, 1, 1), bmax + new Int3(1, 1, 1));
        if (grid.N > MaxCells) return report;

        const int Outside = 1;
        int[] label = new int[grid.N];
        var queue = new Queue<int>();

        // 1. Everything air can reach from the box around the structure is outside.
        for (int i = 0; i < grid.N; i++)
            if (grid.IsEdge(i) && grid.IsAir(i)) { label[i] = Outside; queue.Enqueue(i); }
        Flood(grid, label, queue, Outside, _ => true);

        // Whatever is left is sealed.
        int next = Outside + 1;
        for (int i = 0; i < grid.N; i++)
        {
            if (label[i] != 0 || !grid.IsAir(i)) continue;
            int id = next++;
            label[i] = id;
            queue.Enqueue(i);
            var room = new Room { Sealed = true };
            List<int> cells = Flood(grid, label, queue, id, _ => true);
            foreach (int c in cells) room.Cells.Add(grid.CellOf(c));
            // Air trapped only inside block shapes (a cylinder boxed in by cubes) is not a room.
            if (cells.Any(c => !grid.IsOccupied(c))) report.Rooms.Add(room);
        }

        // 2. Outside air the hull still encloses on every axis is a room with a leak.
        byte[] enclosed = grid.EnclosedDirections();
        bool Candidate(int c) => label[c] == Outside && enclosed[c] == 0x3f;
        var grouped = new bool[grid.N];
        for (int i = 0; i < grid.N; i++)
        {
            if (grouped[i] || !Candidate(i)) continue;
            grouped[i] = true;
            queue.Enqueue(i);
            var cells = new List<int>();
            while (queue.Count > 0)
            {
                int c = queue.Dequeue();
                cells.Add(c);
                for (int d = 0; d < 6; d++)
                {
                    int n = grid.Neighbour(c, d);
                    if (n < 0 || grouped[n] || !Candidate(n) || !grid.Passable(c, d)) continue;
                    grouped[n] = true;
                    queue.Enqueue(n);
                }
            }
            if (cells.All(grid.IsOccupied)) continue;
            var room = new Room { Sealed = false };
            foreach (int c in cells) room.Cells.Add(grid.CellOf(c));
            FindLeaks(grid, label, Outside, cells, room);
            report.Rooms.Add(room);
        }
        return report;
    }

    static List<int> Flood(AirGrid grid, int[] label, Queue<int> queue, int id, Func<int, bool> allow)
    {
        var visited = new List<int>();
        while (queue.Count > 0)
        {
            int c = queue.Dequeue();
            visited.Add(c);
            for (int d = 0; d < 6; d++)
            {
                int n = grid.Neighbour(c, d);
                if (n < 0 || label[n] != 0 || !grid.IsAir(n) || !grid.Passable(c, d) || !allow(n)) continue;
                label[n] = id;
                queue.Enqueue(n);
            }
        }
        return visited;
    }

    /// <summary>
    /// Edmonds–Karp from the room to the bounding box over faces of capacity one. Each augmenting
    /// path is one more independent hole; the final residual graph gives the cut.
    /// </summary>
    static void FindLeaks(AirGrid grid, int[] label, int outside, List<int> roomCells, Room room)
    {
        var flow = new Dictionary<long, int>(); // (cell * 6 + dir) → flow along that face
        int Residual(int c, int d) => 1 - (flow.TryGetValue((long)c * 6 + d, out int f) ? f : 0);
        bool Usable(int c, int d, out int n)
        {
            n = grid.Neighbour(c, d);
            return n >= 0 && label[n] == outside && grid.Passable(c, d);
        }

        var source = new HashSet<int>(roomCells);
        var parent = new Dictionary<int, int>(); // cell → (previous cell * 6 + dir)
        var queue = new Queue<int>();
        int paths = 0;
        while (true)
        {
            parent.Clear();
            queue.Clear();
            foreach (int c in roomCells) { parent[c] = -1; queue.Enqueue(c); }
            int sink = -1;
            while (queue.Count > 0 && sink < 0)
            {
                int c = queue.Dequeue();
                for (int d = 0; d < 6; d++)
                {
                    if (!Usable(c, d, out int n) || parent.ContainsKey(n) || Residual(c, d) <= 0) continue;
                    parent[n] = c * 6 + d;
                    if (grid.IsEdge(n)) { sink = n; break; }
                    queue.Enqueue(n);
                }
            }
            if (sink < 0) break;
            if (++paths > MaxLeaks)
            {
                room.WideOpen = true;
                return;
            }
            for (int v = sink; parent[v] >= 0;)
            {
                int u = parent[v] / 6, d = parent[v] % 6;
                Add(flow, (long)u * 6 + d, 1);
                Add(flow, (long)v * 6 + (d ^ 1), -1);
                v = u;
            }
        }

        // Cells that can still reach the outside edge. The cut sits where the rest meets them.
        var reachesOut = new HashSet<int>();
        queue.Clear();
        for (int i = 0; i < grid.N; i++)
            if (grid.IsEdge(i) && label[i] == outside) { reachesOut.Add(i); queue.Enqueue(i); }
        while (queue.Count > 0)
        {
            int v = queue.Dequeue();
            for (int d = 0; d < 6; d++)
            {
                int u = grid.Neighbour(v, d);
                // Edge u → v crosses the face of u that looks back at v.
                if (u < 0 || reachesOut.Contains(u) || label[u] != outside || !grid.Passable(u, d ^ 1) || Residual(u, d ^ 1) <= 0) continue;
                reachesOut.Add(u);
                queue.Enqueue(u);
            }
        }

        var reachable = new HashSet<int>(source);
        queue.Clear();
        foreach (int c in roomCells) queue.Enqueue(c);
        while (queue.Count > 0)
        {
            int c = queue.Dequeue();
            for (int d = 0; d < 6; d++)
            {
                if (!Usable(c, d, out int n)) continue;
                if (reachesOut.Contains(n))
                {
                    room.Leaks.Add(new Face(grid.CellOf(c), (Dir)d));
                    continue;
                }
                if (reachable.Add(n)) queue.Enqueue(n);
            }
        }
    }

    static void Add(Dictionary<long, int> flow, long key, int delta) =>
        flow[key] = (flow.TryGetValue(key, out int f) ? f : 0) + delta;

    /// <summary>The structure's bounding box as flat arrays: which faces of each cell seal, and which cells hold a block.</summary>
    sealed class AirGrid
    {
        public readonly int N;
        readonly Int3 _min;
        readonly int _nx, _ny, _nz;
        readonly byte[] _seal;     // bit per Dir: this cell's block seals that face
        readonly bool[] _occupied;

        public AirGrid(Structure s, Int3 min, Int3 max)
        {
            _min = min;
            _nx = max.X - min.X + 1;
            _ny = max.Y - min.Y + 1;
            _nz = max.Z - min.Z + 1;
            long n = (long)_nx * _ny * _nz;
            N = n > MaxCells ? int.MaxValue : (int)n;
            if (n > MaxCells) return;
            _seal = new byte[N];
            _occupied = new bool[N];
            foreach (var (p, b) in s.Blocks())
            {
                int i = Index(p - min);
                _occupied[i] = true;
                _seal[i] = s.Catalogue.SealMask(b);
            }
        }

        int Index(Int3 local) => local.X + _nx * (local.Y + _ny * local.Z);

        public Int3 CellOf(int i) => _min + new Int3(i % _nx, i / _nx % _ny, i / (_nx * _ny));

        public bool IsEdge(int i)
        {
            int x = i % _nx, y = i / _nx % _ny, z = i / (_nx * _ny);
            return x == 0 || y == 0 || z == 0 || x == _nx - 1 || y == _ny - 1 || z == _nz - 1;
        }

        /// <summary>Air can be in this cell unless its block seals all six faces (a solid block).</summary>
        public bool IsAir(int i) => _seal[i] != 0x3f;

        public bool IsOccupied(int i) => _occupied[i];

        public int Neighbour(int i, int d)
        {
            int x = i % _nx, y = i / _nx % _ny, z = i / (_nx * _ny);
            switch (d)
            {
                case 0: return x + 1 < _nx ? i + 1 : -1;
                case 1: return x > 0 ? i - 1 : -1;
                case 2: return y + 1 < _ny ? i + _nx : -1;
                case 3: return y > 0 ? i - _nx : -1;
                case 4: return z + 1 < _nz ? i + _nx * _ny : -1;
                default: return z > 0 ? i - _nx * _ny : -1;
            }
        }

        /// <summary>Air crosses the face between a cell and its neighbour unless either side seals it.</summary>
        public bool Passable(int i, int d)
        {
            int n = Neighbour(i, d);
            return n >= 0 && (_seal[i] & (1 << d)) == 0 && (_seal[n] & (1 << (d ^ 1))) == 0;
        }

        /// <summary>For each cell, a bit per direction: is there a block somewhere along that ray?</summary>
        public byte[] EnclosedDirections()
        {
            var bits = new byte[N];
            for (int axis = 0; axis < 3; axis++)
            {
                int len = axis == 0 ? _nx : axis == 1 ? _ny : _nz;
                int stride = axis == 0 ? 1 : axis == 1 ? _nx : _nx * _ny;
                for (int start = 0; start < N; start++)
                {
                    int pos = axis == 0 ? start % _nx : axis == 1 ? start / _nx % _ny : start / (_nx * _ny);
                    if (pos != 0) continue; // walk each line once, from its first cell
                    bool seen = false;
                    for (int k = 0, i = start; k < len; k++, i += stride)
                    {
                        if (seen) bits[i] |= (byte)(1 << (axis * 2 + 1)); // a block in the negative direction
                        if (_occupied[i]) seen = true;
                    }
                    seen = false;
                    for (int k = len - 1, i = start + (len - 1) * stride; k >= 0; k--, i -= stride)
                    {
                        if (seen) bits[i] |= (byte)(1 << (axis * 2));
                        if (_occupied[i]) seen = true;
                    }
                }
            }
            return bits;
        }
    }
}
