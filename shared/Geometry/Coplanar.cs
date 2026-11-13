#nullable enable
using System;
using System.Collections.Generic;
using System.Numerics;
using System.Runtime.InteropServices;

namespace GalHack.Geometry;

/// <summary>
/// Faces that z-fight: two triangles of different looks (material or colour) lying in nearly the
/// same plane, facing the same way, and overlapping in area. Engine-free. A test hands
/// <see cref="Find"/> the triangles of whatever it built (block chunks, deco models, rigs, a whole
/// drawn scene) with a look and a source each and gets the pairs back, so every builder is held to
/// one rule; a mesher hands <see cref="Separate"/> its triangles grouped into parts and moves the
/// parts it is told to, so the rule is kept by construction: whatever lies on a face stands
/// <see cref="Standoff"/> off it.
/// </summary>
public static class Coplanar
{
    /// <summary>
    /// How far a part laid on another's face stands off it (m): past <see cref="Gap"/>, and with the
    /// depth buffer reversed far more than its precision anywhere a face is still seen.
    /// </summary>
    public const float Standoff = 0.003f;
    /// <summary>One drawn, opaque triangle. <see cref="Front"/> is the side it is seen from (any length; zero takes the winding as given).</summary>
    public readonly record struct Face(Vector3 A, Vector3 B, Vector3 C, Vector3 Front, int Look, int Source);

    public readonly record struct Pair(int P, int Q, float Gap, float Area);

    /// <summary>How far apart two planes may lie and still fight (m).</summary>
    public const float Gap = 0.002f;
    /// <summary>The least shared area that is a flicker anyone sees (m2): 1 cm2.</summary>
    public const float MinArea = 1e-4f;

    /// <summary>The look of a face from a material and an RGBA colour, quantised so equal shades are one look.</summary>
    public static int Look(int material, Vector4 colour) =>
        HashCode.Combine(material, (int)MathF.Round(colour.X * 255f), (int)MathF.Round(colour.Y * 255f), (int)MathF.Round(colour.Z * 255f), (int)MathF.Round(colour.W * 255f));

    /// <summary>What a pair's test needs of a face, side by side.</summary>
    struct Info
    {
        public Vector3 Normal, Lo, Hi;
        public float D;
        public int Look, Group, Cx, Cy, Cz;
        public sbyte Axis;
        public bool Apart, Valid;
    }

    /// <summary>Where a face goes in the buckets: its facing and plane slab, quantised, and the cells its box touches.</summary>
    struct Bucketing
    {
        public int Nx, Ny, Nz, Dq, Hx, Hy, Hz;
    }

    const float Cell = 2f, Slab = 0.004f;

    /// <summary>Faces past this many are sorted into buckets and tried on several threads (the result is the same).</summary>
    const int ParallelFrom = 20000;

    /// <summary>
    /// Every fighting pair among the faces (indices into the list), in order of their indices. Pairs a caller knows
    /// can't fight are never tried: two faces of one <paramref name="group"/> (a part is never moved off itself), and
    /// two faces both marked <paramref name="apart"/> (a block grid's cell faces: each is its own cell's, none lies
    /// over another).
    /// </summary>
    public static List<Pair> Find(IReadOnlyList<Face> faces, float gap = Gap, float minArea = MinArea, IReadOnlyList<int>? group = null, IReadOnlyList<bool>? apart = null)
    {
        int n = faces.Count;
        Face[]? copy = faces is List<Face> || faces is Face[] ? null : new List<Face>(faces).ToArray();
        IReadOnlyList<Face> src = copy ?? faces;
        int[]? groups = group == null ? null : new List<int>(group).ToArray();
        bool[]? aparts = apart == null ? null : new List<bool>(apart).ToArray();
        var info = new Info[n];
        var place = new Bucketing[n];
        // Each face's facing, plane, box and cells: on their own, so on several threads for a big mesh.
        void Fill(int from, int to)
        {
            ReadOnlySpan<Face> all = src is List<Face> l ? CollectionsMarshal.AsSpan(l) : (Face[])src;
            for (int i = from; i < to; i++)
            {
                ref readonly Face f = ref all[i];
                Vector3 c = Vector3.Cross(f.B - f.A, f.C - f.A);
                float len = c.Length();
                if (len < 2e-7f) continue; // no area
                c /= len;
                if (f.Front != Vector3.Zero && Vector3.Dot(c, f.Front) < 0f) c = -c;
                Vector3 lo = Vector3.Min(f.A, Vector3.Min(f.B, f.C)), hi = Vector3.Max(f.A, Vector3.Max(f.B, f.C));
                float d = Vector3.Dot(c, (f.A + f.B + f.C) / 3f);
                info[i] = new Info
                {
                    Normal = c, Lo = lo, Hi = hi, D = d, Look = f.Look, Group = groups?[i] ?? 0, Apart = aparts?[i] ?? false, Valid = true,
                    // The axis it faces along, when it faces along one (-1 if not): two such faces that only touch at an
                    // edge share nothing, and most neighbours in a bucket do just that.
                    Axis = (sbyte)(MathF.Abs(c.X) > 0.9999f ? 0 : MathF.Abs(c.Y) > 0.9999f ? 1 : MathF.Abs(c.Z) > 0.9999f ? 2 : -1),
                    Cx = (int)MathF.Floor(lo.X / Cell), Cy = (int)MathF.Floor(lo.Y / Cell), Cz = (int)MathF.Floor(lo.Z / Cell),
                };
                place[i] = new Bucketing
                {
                    Nx = (int)MathF.Round(c.X * 40f), Ny = (int)MathF.Round(c.Y * 40f), Nz = (int)MathF.Round(c.Z * 40f), Dq = (int)MathF.Floor(d / Slab),
                    Hx = (int)MathF.Floor(hi.X / Cell), Hy = (int)MathF.Floor(hi.Y / Cell), Hz = (int)MathF.Floor(hi.Z / Cell),
                };
            }
        }
        if (n < ParallelFrom) Fill(0, n);
        else System.Threading.Tasks.Parallel.ForEach(System.Collections.Concurrent.Partitioner.Create(0, n, 8192), r => Fill(r.Item1, r.Item2));

        // Buckets by facing, plane (4 mm slabs) and 2 m cell: a face in every cell its box touches; a pair is tried only
        // in the first cell both share, so never twice. Faces marked apart are bucketed only where an unmarked face
        // could meet them (in its own plane slab or the next either side, in one of its cells): two marked faces are
        // never tried, so anywhere else a marked face has nothing to be tried against. The unmarked go first, so that
        // is known when the marked come.
        var map = new BucketMap(n);
        var bucketCell = new List<(int X, int Y, int Z)>();
        var entryBucket = new List<int>(n + n / 4);
        var entryFace = new List<int>(n + n / 4);
        for (int pass = aparts == null ? 1 : 0; pass < 2; pass++)
            for (int i = 0; i < n; i++)
            {
                ref Info fi = ref info[i];
                if (!fi.Valid || aparts != null && fi.Apart != (pass == 1)) continue;
                Bucketing pl = place[i];
                for (int x = fi.Cx; x <= pl.Hx; x++)
                    for (int y = fi.Cy; y <= pl.Hy; y++)
                        for (int z = fi.Cz; z <= pl.Hz; z++)
                        {
                            ulong key = Hash(pl.Nx, pl.Ny, pl.Nz, pl.Dq, x, y, z);
                            int b = map.Get(key);
                            if (b < 0)
                            {
                                if (pass == 1 && aparts != null && map.Get(Hash(pl.Nx, pl.Ny, pl.Nz, pl.Dq - 1, x, y, z)) < 0 && map.Get(Hash(pl.Nx, pl.Ny, pl.Nz, pl.Dq + 1, x, y, z)) < 0) continue;
                                b = bucketCell.Count;
                                map.Add(key, b);
                                bucketCell.Add((x, y, z));
                            }
                            entryBucket.Add(b);
                            entryFace.Add(i);
                        }
            }
        // The faces bucket by bucket, and where each bucket starts.
        int buckets = bucketCell.Count, m = entryFace.Count;
        var bucketStart = new int[buckets + 1];
        foreach (int b in entryBucket) bucketStart[b + 1]++;
        for (int b = 0; b < buckets; b++) bucketStart[b + 1] += bucketStart[b];
        var fill = (int[])bucketStart.Clone();
        var at = new int[m];
        for (int e = 0; e < m; e++) at[fill[entryBucket[e]]++] = entryFace[e];

        List<Pair> Try(int fromBucket, int toBucket)
        {
            var found = new List<Pair>();
            for (int b = fromBucket; b < toBucket; b++)
            {
                int start = bucketStart[b], end = bucketStart[b + 1];
                var (x, y, z) = bucketCell[b];
                Bucketing pl = place[at[start]];
                // And the bucket a slab up, the same way and cell: a pair can straddle a slab's edge.
                int u = map.Get(Hash(pl.Nx, pl.Ny, pl.Nz, pl.Dq + 1, x, y, z));
                int a0 = u < 0 ? 0 : bucketStart[u], a1 = u < 0 ? 0 : bucketStart[u + 1];
                for (int a = start; a < end; a++)
                {
                    for (int c = a + 1; c < end; c++) Test(at[a], at[c], x, y, z, found);
                    for (int c = a0; c < a1; c++) Test(at[a], at[c], x, y, z, found);
                }
            }
            return found;
        }

        void Test(int p, int q, int x, int y, int z, List<Pair> found)
        {
            if (p == q) return;
            ref Info a = ref info[p], b = ref info[q];
            if (a.Look == b.Look) return;
            if (groups != null && a.Group == b.Group) return;
            if (a.Apart && b.Apart) return;
            // Only in the first cell the two share.
            if (Math.Max(a.Cx, b.Cx) != x || Math.Max(a.Cy, b.Cy) != y || Math.Max(a.Cz, b.Cz) != z) return;
            if (Vector3.Dot(a.Normal, b.Normal) < 0.9995f) return;
            float gapPq = MathF.Abs(a.D - b.D);
            if (gapPq > gap) return;
            // Both facing along one axis: their boxes overlap across the plane both ways, or they share no area (with a
            // micrometre's overlap or less, the most they could share is far under any least area).
            int ax = a.Axis;
            if (ax >= 0 && ax == b.Axis)
            {
                Vector3 e = Vector3.Min(a.Hi, b.Hi) - Vector3.Max(a.Lo, b.Lo);
                const float Touch = 1e-6f;
                if (ax != 0 && e.X <= Touch || ax != 1 && e.Y <= Touch || ax != 2 && e.Z <= Touch) return;
            }
            float area = Shared(src[p], src[q], a.Normal);
            if (area >= minArea) found.Add(new Pair(Math.Min(p, q), Math.Max(p, q), gapPq, area));
        }

        List<Pair> pairs;
        if (n < ParallelFrom) pairs = Try(0, buckets);
        else
        {
            var parts = new System.Collections.Concurrent.ConcurrentBag<List<Pair>>();
            System.Threading.Tasks.Parallel.ForEach(System.Collections.Concurrent.Partitioner.Create(0, buckets, 4096), r => parts.Add(Try(r.Item1, r.Item2)));
            pairs = new List<Pair>();
            foreach (List<Pair> part in parts) pairs.AddRange(part);
        }
        // In index order, however the threads fell: a mesher that acts on them in turn does the same every time.
        pairs.Sort((s, t) => s.P != t.P ? s.P.CompareTo(t.P) : s.Q.CompareTo(t.Q));
        return pairs;
    }

    /// <summary>
    /// The buckets by their hashed keys, open addressed (-1 for a key not there): a mesh's million keys, with none of a
    /// general dictionary's weight. Keys are 64-bit hashes: two buckets' keys alike is as good as never.
    /// </summary>
    sealed class BucketMap
    {
        ulong[] _keys;
        int[] _values;
        int _count;

        public BucketMap(int capacity)
        {
            int size = 64;
            while (size < capacity) size <<= 1;
            _keys = new ulong[size];
            _values = new int[size];
        }

        // 0 marks an empty slot, so a key of 0 is kept as 1.
        public void Add(ulong k, int value)
        {
            if (k == 0) k = 1;
            if (_count * 2 >= _keys.Length) Grow();
            int mask = _keys.Length - 1;
            int i = (int)(k ^ (k >> 32)) & mask;
            while (_keys[i] != 0 && _keys[i] != k) i = (i + 1) & mask;
            if (_keys[i] == 0) _count++;
            _keys[i] = k;
            _values[i] = value;
        }

        public int Get(ulong k)
        {
            if (k == 0) k = 1;
            int mask = _keys.Length - 1;
            for (int i = (int)(k ^ (k >> 32)) & mask; ; i = (i + 1) & mask)
            {
                if (_keys[i] == k) return _values[i];
                if (_keys[i] == 0) return -1;
            }
        }

        void Grow()
        {
            ulong[] keys = _keys;
            int[] values = _values;
            _keys = new ulong[keys.Length * 2];
            _values = new int[keys.Length * 2];
            _count = 0;
            for (int i = 0; i < keys.Length; i++)
                if (keys[i] != 0) Add(keys[i], values[i]);
        }
    }

    static ulong Hash(int nx, int ny, int nz, int dq, int x, int y, int z)
    {
        ulong h = 1469598103934665603UL;
        h = (h ^ (uint)nx) * 1099511628211UL;
        h = (h ^ (uint)ny) * 1099511628211UL;
        h = (h ^ (uint)nz) * 1099511628211UL;
        h = (h ^ (uint)dq) * 1099511628211UL;
        h = (h ^ (uint)x) * 1099511628211UL;
        h = (h ^ (uint)y) * 1099511628211UL;
        h = (h ^ (uint)z) * 1099511628211UL;
        return h ^ (h >> 29);
    }

    /// <summary>
    /// Sets fighting parts apart: face <c>i</c> belongs to part <c>group[i]</c>, and of two parts whose faces
    /// fight the smaller (<paramref name="size"/>) is what lies on the other, so it moves <paramref name="standoff"/>
    /// out along its fighting face's normal (ties: the later part). Rounds repeat for parts stacked three deep.
    /// <paramref name="may"/> can refuse a move (a part must not sink through the floor): the other part moves
    /// back instead. Faces marked <paramref name="apart"/> are known never to fight one another (see <see cref="Find"/>).
    /// Returns each part's offset; the caller moves its own vertices.
    /// </summary>
    public static Vector3[] Separate(IReadOnlyList<Face> faces, IReadOnlyList<int> group, IReadOnlyList<float> size, Func<int, Vector3, bool>? may = null, float standoff = Standoff, int rounds = 8, IReadOnlyList<bool>? apart = null)
    {
        int n = faces.Count;
        var offset = new Vector3[size.Count];
        List<Pair> pairs = Find(faces, group: group, apart: apart);
        if (pairs.Count == 0) return offset;
        // Each part's faces, to find what a part moved now lies on.
        var partStart = new int[size.Count + 1];
        for (int i = 0; i < n; i++) partStart[group[i] + 1]++;
        for (int g = 0; g < size.Count; g++) partStart[g + 1] += partStart[g];
        var partFaces = new int[n];
        var fill = (int[])partStart.Clone();
        for (int i = 0; i < n; i++) partFaces[fill[group[i]]++] = i;
        Face At(int i) => offset[group[i]] is var o && o == Vector3.Zero ? faces[i] : faces[i] with { A = faces[i].A + o, B = faces[i].B + o, C = faces[i].C + o };
        // The faces a round looks at, by their index in the whole list (all of them in the first round).
        int[]? ids = null;
        for (int r = 0; r < rounds && pairs.Count > 0; r++)
        {
            int Id(int k) => ids == null ? k : ids[k];
            var step = new Dictionary<int, Vector3>();
            foreach (Pair p in pairs)
            {
                int fp = Id(p.P), fq = Id(p.Q), gp = group[fp], gq = group[fq];
                if (gp == gq) continue;
                bool pMoves = size[gp] < size[gq] || size[gp] == size[gq] && gp > gq;
                int mover = pMoves ? gp : gq, other = pMoves ? gq : gp;
                if (step.ContainsKey(mover) || step.ContainsKey(other)) continue;
                Vector3 s = Front(At(pMoves ? fp : fq)) * standoff;
                if (may != null && !may(mover, offset[mover] + s))
                {
                    if (!may(other, offset[other] - s)) continue;
                    (mover, s) = (other, -s);
                }
                step[mover] = s;
            }
            if (step.Count == 0 || r + 1 == rounds) { foreach (var kv in step) offset[kv.Key] += kv.Value; break; }
            foreach (var kv in step) offset[kv.Key] += kv.Value;
            // What can fight next round: a face of a part just moved, or of a pair still fighting, and whatever shares a
            // plane and a cell with one (the rest of the mesh is as it was, and was found clear).
            var hotFaces = new HashSet<int>();
            foreach (Pair p in pairs) { hotFaces.Add(Id(p.P)); hotFaces.Add(Id(p.Q)); }
            foreach (int g in step.Keys)
                for (int k = partStart[g]; k < partStart[g + 1]; k++) hotFaces.Add(partFaces[k]);
            var hot = new BucketMap(hotFaces.Count * 8);
            foreach (int i in hotFaces)
            {
                Face f = At(i);
                var (nx, ny, nz, s) = PlaneOf(f);
                var (lo, hi) = CellsOf(f);
                for (int x = lo.X; x <= hi.X; x++)
                    for (int y = lo.Y; y <= hi.Y; y++)
                        for (int z = lo.Z; z <= hi.Z; z++)
                            for (int k = -1; k <= 1; k++) hot.Add(Hash(nx, ny, nz, s + k, x, y, z), 0);
            }
            var keep = new bool[n];
            void Mark(int from, int to)
            {
                for (int i = from; i < to; i++)
                {
                    if (hotFaces.Contains(i)) { keep[i] = true; continue; }
                    Face f = At(i);
                    var (nx, ny, nz, s) = PlaneOf(f);
                    var (lo, hi) = CellsOf(f);
                    for (int x = lo.X; x <= hi.X && !keep[i]; x++)
                        for (int y = lo.Y; y <= hi.Y && !keep[i]; y++)
                            for (int z = lo.Z; z <= hi.Z && !keep[i]; z++)
                                if (hot.Get(Hash(nx, ny, nz, s, x, y, z)) >= 0) keep[i] = true;
                }
            }
            if (n < ParallelFrom) Mark(0, n);
            else System.Threading.Tasks.Parallel.ForEach(System.Collections.Concurrent.Partitioner.Create(0, n, 8192), part => Mark(part.Item1, part.Item2));
            var kept = new List<int>();
            for (int i = 0; i < n; i++) if (keep[i]) kept.Add(i);
            ids = kept.ToArray();
            var work = new Face[ids.Length];
            var groups = new int[ids.Length];
            bool[]? aparts = apart == null ? null : new bool[ids.Length];
            for (int k = 0; k < ids.Length; k++)
            {
                work[k] = At(ids[k]);
                groups[k] = group[ids[k]];
                if (aparts != null) aparts[k] = apart![ids[k]];
            }
            pairs = Find(work, group: groups, apart: aparts);
        }
        return offset;
    }

    /// <summary>The 2 m cells a face's box touches, as <see cref="Find"/> buckets it.</summary>
    static ((int X, int Y, int Z) Lo, (int X, int Y, int Z) Hi) CellsOf(Face f)
    {
        Vector3 l = Vector3.Min(f.A, Vector3.Min(f.B, f.C)), h = Vector3.Max(f.A, Vector3.Max(f.B, f.C));
        return (((int)MathF.Floor(l.X / Cell), (int)MathF.Floor(l.Y / Cell), (int)MathF.Floor(l.Z / Cell)),
                ((int)MathF.Floor(h.X / Cell), (int)MathF.Floor(h.Y / Cell), (int)MathF.Floor(h.Z / Cell)));
    }

    /// <summary>A face's plane, coarsely: its facing and a 5 cm slab along it.</summary>
    static (int, int, int, int) PlaneOf(Face f)
    {
        Vector3 n = Front(f);
        return ((int)MathF.Round(n.X * 40f), (int)MathF.Round(n.Y * 40f), (int)MathF.Round(n.Z * 40f), (int)MathF.Floor(Vector3.Dot(n, (f.A + f.B + f.C) / 3f) / 0.05f));
    }

    /// <summary>A face's unit normal on the side it is seen from.</summary>
    public static Vector3 Front(Face f)
    {
        Vector3 c = Vector3.Cross(f.B - f.A, f.C - f.A);
        float len = c.Length();
        if (len < 1e-12f) return Vector3.Zero;
        c /= len;
        return f.Front != Vector3.Zero && Vector3.Dot(c, f.Front) < 0f ? -c : c;
    }

    /// <summary>The area two nearly coplanar triangles share, seen along the first one's normal.</summary>
    public static float Shared(Face p, Face q, Vector3 normal)
    {
        Vector3 u = Vector3.Normalize(MathF.Abs(normal.X) < 0.9f ? Vector3.Cross(normal, Vector3.UnitX) : Vector3.Cross(normal, Vector3.UnitY));
        Vector3 v = Vector3.Cross(normal, u);
        Vector3 o = p.A;
        Span<Vector2> clip = stackalloc Vector2[3];
        Span<Vector2> poly = stackalloc Vector2[12];
        Span<Vector2> next = stackalloc Vector2[12];
        clip[0] = On(p.A, o, u, v); clip[1] = On(p.B, o, u, v); clip[2] = On(p.C, o, u, v);
        poly[0] = On(q.A, o, u, v); poly[1] = On(q.B, o, u, v); poly[2] = On(q.C, o, u, v);
        int count = 3;
        // Bounding boxes first: most neighbours in a bucket don't touch.
        if (MathF.Max(clip[0].X, MathF.Max(clip[1].X, clip[2].X)) <= MathF.Min(poly[0].X, MathF.Min(poly[1].X, poly[2].X))
            || MathF.Min(clip[0].X, MathF.Min(clip[1].X, clip[2].X)) >= MathF.Max(poly[0].X, MathF.Max(poly[1].X, poly[2].X))
            || MathF.Max(clip[0].Y, MathF.Max(clip[1].Y, clip[2].Y)) <= MathF.Min(poly[0].Y, MathF.Min(poly[1].Y, poly[2].Y))
            || MathF.Min(clip[0].Y, MathF.Min(clip[1].Y, clip[2].Y)) >= MathF.Max(poly[0].Y, MathF.Max(poly[1].Y, poly[2].Y))) return 0f;
        if (Cross(clip[1] - clip[0], clip[2] - clip[0]) < 0f) (clip[1], clip[2]) = (clip[2], clip[1]);
        for (int e = 0; e < 3 && count > 0; e++)
        {
            Vector2 a = clip[e], b = clip[(e + 1) % 3];
            int m = 0;
            for (int i = 0; i < count; i++)
            {
                Vector2 s = poly[i], t = poly[(i + 1) % count];
                float ds = Cross(b - a, s - a), dt = Cross(b - a, t - a);
                if (ds >= 0f) next[m++] = s;
                if (ds >= 0f != dt >= 0f) next[m++] = s + (t - s) * (ds / (ds - dt));
            }
            Span<Vector2> swap = poly;
            poly = next;
            next = swap;
            count = m;
        }
        float area = 0f;
        for (int i = 0; i < count; i++) area += Cross(poly[i], poly[(i + 1) % count]);
        return MathF.Abs(area) * 0.5f;
    }

    static Vector2 On(Vector3 w, Vector3 o, Vector3 u, Vector3 v) => new(Vector3.Dot(w - o, u), Vector3.Dot(w - o, v));

    static float Cross(Vector2 a, Vector2 b) => a.X * b.Y - a.Y * b.X;

    /// <summary>Counts of pairs by the pair of sources' names, worst first.</summary>
    public static List<(string Sources, int Count)> BySource(IReadOnlyList<Face> faces, IReadOnlyList<Pair> pairs, Func<int, string> name)
    {
        var counts = new Dictionary<string, int>();
        foreach (Pair p in pairs)
        {
            string a = name(faces[p.P].Source), b = name(faces[p.Q].Source);
            string k = string.CompareOrdinal(a, b) <= 0 ? (a == b ? a : $"{a} / {b}") : $"{b} / {a}";
            counts[k] = counts.GetValueOrDefault(k) + 1;
        }
        var list = new List<(string, int)>();
        foreach (var kv in counts) list.Add((kv.Key, kv.Value));
        list.Sort((x, y) => y.Item2.CompareTo(x.Item2));
        return list;
    }
}
