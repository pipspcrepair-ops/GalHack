using System;
using System.Collections.Generic;
using System.Numerics;
using System.Threading;

namespace TerrainDigging.Terrain;

/// <summary>
/// Cuts a chunk's terrain surface off exactly at the faces of the structures standing in it
/// (rule 4, the cutting half; <see cref="ChunkNeighborhood"/> draws the cleared cells first).
/// Whatever lies inside a volume is dropped and every triangle crossing a face is split along it,
/// so the ground outside meets the face along their true intersection: no sag, no gap, at any
/// block size and angle. Collision is built from the same triangles, so walking matches the look.
///
/// For each volume in turn, a triangle T that touches it loses T ∩ box, a convex polygon C,
/// and T \ C is re-triangulated. The only new points on T's edges are where an edge enters or
/// leaves the box, and those are computed from the edge alone with its ends in a canonical order.
/// The triangle on the other side of that edge (in this chunk or the next) gets the same points,
/// to the bit, so the cut surface has no cracks or T-junctions across seams. A pure function of
/// the triangles and volumes, so a chunk always rebuilds the same.
///
/// One instance per mesher (one per thread): it reuses its buffers.
/// </summary>
public sealed class StructureClipper
{
    /// <summary>Triangles the cut couldn't re-triangulate cleanly (kept or dropped whole instead). Tests expect 0.</summary>
    public static long Fallbacks => Interlocked.Read(ref _fallbacks);

    static long _fallbacks;

    readonly List<Vector3> _pos = new(), _nrm = new();
    readonly List<CellMaterial> _mat = new();
    readonly List<Int3> _cube = new();
    List<int> _tri = new(), _next = new();
    List<byte> _blk = new(), _nextBlk = new();
    readonly Dictionary<(int, int, int), int> _added = new();

    // Per-triangle scratch.
    readonly List<int> _ring = new();
    readonly List<bool> _ringIn = new();
    readonly List<int> _pierce = new();
    readonly List<int> _poly = new();
    readonly List<(int a, int b, int c)> _ears = new();
    readonly List<int> _split = new(2);
    readonly List<(float angle, int vertex, int ring)> _c = new();
    readonly Dictionary<int, Vector2> _flat = new();

    public ChunkMesh Clip(ChunkMesh mesh, StructureVolume[] volumes)
    {
        _pos.Clear();
        _nrm.Clear();
        _mat.Clear();
        _cube.Clear();
        _added.Clear();
        _pos.AddRange(mesh.Positions);
        _nrm.AddRange(mesh.Normals);
        _mat.AddRange(mesh.Materials);
        bool cubes = mesh.Cubes != null;
        if (cubes) _cube.AddRange(mesh.Cubes);
        else for (int i = 0; i < mesh.Positions.Length; i++) _cube.Add(default);
        _tri.Clear();
        _tri.AddRange(mesh.Indices);
        _blk.Clear();
        _blk.AddRange(mesh.TriangleBlocks);

        bool changed = false;
        foreach (StructureVolume v in volumes)
        {
            float tol = Tolerance(v);
            _next.Clear();
            _nextBlk.Clear();
            for (int t = 0; t < _blk.Count; t++)
            {
                int a = _tri[t * 3], b = _tri[t * 3 + 1], c = _tri[t * 3 + 2];
                if (!Near(_pos[a], _pos[b], _pos[c], v, tol * 4f) || !ClipTriangle(a, b, c, _blk[t], v, tol))
                {
                    _next.Add(a);
                    _next.Add(b);
                    _next.Add(c);
                    _nextBlk.Add(_blk[t]);
                }
                else changed = true;
            }
            (_tri, _next) = (_next, _tri);
            (_blk, _nextBlk) = (_nextBlk, _blk);
        }
        if (!changed) return mesh;
        return Compact(mesh.Chunk, cubes);
    }

    /// <summary>
    /// How close two points must be to count as one (m): a tenth of a millimetre, or a couple of
    /// float steps at the volume's distance from the origin (a streamed planet is kilometres wide).
    /// One number per volume, so both sides of a seam decide alike.
    /// </summary>
    static float Tolerance(StructureVolume v)
    {
        float mag = MathF.Max(MathF.Abs(v.Center.X), MathF.Max(MathF.Abs(v.Center.Y), MathF.Abs(v.Center.Z))) + v.Half.Length();
        return MathF.Max(1e-4f, mag * 2.5e-7f);
    }

    static bool Near(Vector3 a, Vector3 b, Vector3 c, StructureVolume v, float margin) =>
        v.BoundsOverlap(Vector3.Min(a, Vector3.Min(b, c)), Vector3.Max(a, Vector3.Max(b, c)), margin);

    static bool InsideTol(Vector3 l, Vector3 h, float tol) =>
        MathF.Abs(l.X) <= h.X + tol && MathF.Abs(l.Y) <= h.Y + tol && MathF.Abs(l.Z) <= h.Z + tol;

    /// <summary>
    /// Replaces one triangle with what of it lies outside the volume. Returns false when the
    /// volume doesn't touch it (the caller keeps it as it is).
    /// </summary>
    bool ClipTriangle(int ia, int ib, int ic, byte block, StructureVolume v, float tol)
    {
        Vector3 la = v.Local(_pos[ia]), lb = v.Local(_pos[ib]), lc = v.Local(_pos[ic]);
        bool inA = InsideTol(la, v.Half, tol), inB = InsideTol(lb, v.Half, tol), inC = InsideTol(lc, v.Half, tol);
        if (inA && inB && inC) return true; // all of it is inside: dropped

        // The ring: T's corners with the points where its edges enter or leave the box.
        _ring.Clear();
        _ringIn.Clear();
        bool splits = false;
        AddCorner(ia, inA);
        splits |= AddSplits(ia, ib, v, tol);
        AddCorner(ib, inB);
        splits |= AddSplits(ib, ic, v, tol);
        AddCorner(ic, inC);
        splits |= AddSplits(ic, ia, v, tol);

        // Where the box's own edges pass through T's interior.
        _pierce.Clear();
        AddPierces(ia, ib, ic, v, tol);

        if (!splits && !inA && !inB && !inC && _pierce.Count == 0) return false;

        // Flatten onto T's plane, turned so T runs anticlockwise.
        Vector3 pa = _pos[ia], n = Vector3.Cross(_pos[ib] - pa, _pos[ic] - pa);
        Vector3 an = Vector3.Abs(n);
        int drop = an.X >= an.Y && an.X >= an.Z ? 0 : an.Y >= an.Z ? 1 : 2;
        float sign = (drop == 0 ? n.X : drop == 1 ? n.Y : n.Z) >= 0f ? 1f : -1f;
        _flat.Clear();
        foreach (int r in _ring) Flatten(r, pa, drop, sign);
        foreach (int p in _pierce) Flatten(p, pa, drop, sign);
        float scale = MathF.Max(Vector3.DistanceSquared(_pos[ia], _pos[ib]), MathF.Max(Vector3.DistanceSquared(_pos[ib], _pos[ic]), Vector3.DistanceSquared(_pos[ic], _pos[ia])));
        float areaEps = 1e-9f * MathF.Max(scale, 1e-6f);

        // C = T ∩ box: every ring point in the box and every pierce point, all on C's convex
        // boundary, so sorting them by angle round their centroid walks that boundary anticlockwise.
        _c.Clear();
        Vector2 g = Vector2.Zero;
        int count = 0;
        for (int i = 0; i < _ring.Count; i++)
            if (_ringIn[i]) { g += _flat[_ring[i]]; count++; }
        foreach (int p in _pierce) { g += _flat[p]; count++; }
        float area = 0f;
        if (count >= 3)
        {
            g /= count;
            for (int i = 0; i < _ring.Count; i++)
                if (_ringIn[i]) _c.Add((Angle(_flat[_ring[i]] - g), _ring[i], i));
            foreach (int p in _pierce) _c.Add((Angle(_flat[p] - g), p, -1));
            _c.Sort((x, y) => x.angle.CompareTo(y.angle));
            for (int i = 0; i < _c.Count; i++)
            {
                Vector2 p = _flat[_c[i].vertex], q = _flat[_c[(i + 1) % _c.Count].vertex];
                area += p.X * q.Y - q.X * p.Y;
            }
            area *= 0.5f;
        }

        _ears.Clear();
        if (area <= areaEps * 4f)
        {
            // The box only grazes T (a point or along an edge): keep all of it, split where its
            // edges were split so it still meets its neighbours point for point.
            if (!splits) return false;
            _poly.Clear();
            _poly.AddRange(_ring);
            if (!EarClip(_poly, areaEps)) Fallback(_poly, keep: true);
        }
        else if (AllIn())
        {
            return true; // T lies in the box to within the tolerance: dropped
        }
        else if (!AnyIn())
        {
            // The box pokes through T's middle without touching its edges: T with a hole.
            if (!Holed(areaEps)) Fallback(_ring, keep: true);
        }
        else if (!Loops(areaEps))
        {
            // Couldn't walk C's boundary consistently: keep the ring whole if T is mostly outside.
            _ears.Clear();
            Vector3 mid = (la + lb + lc) / 3f;
            _poly.Clear();
            _poly.AddRange(_ring);
            Fallback(_poly, keep: !InsideTol(mid, v.Half, 0f));
        }

        foreach ((int a, int b, int c) in _ears)
        {
            _next.Add(a);
            _next.Add(b);
            _next.Add(c);
            _nextBlk.Add(block);
        }
        return true;
    }

    bool AllIn()
    {
        foreach (bool b in _ringIn) if (!b) return false;
        return true;
    }

    bool AnyIn()
    {
        foreach (bool b in _ringIn) if (b) return true;
        return false;
    }

    void AddCorner(int vertex, bool inside)
    {
        _ring.Add(vertex);
        _ringIn.Add(inside);
    }

    /// <summary>
    /// Adds the points where edge (from → to) enters and leaves the box, in order from "from".
    /// Computed from the edge alone, its ends in a canonical order, so both triangles that share
    /// the edge get the very same points.
    /// </summary>
    bool AddSplits(int from, int to, StructureVolume v, float tol)
    {
        bool forward = Less(_pos[from], _pos[to]);
        int p = forward ? from : to, q = forward ? to : from;
        Vector3 pp = _pos[p], pq = _pos[q];
        Vector3 lp = v.Local(pp), lq = v.Local(pq);
        float t0 = 0f, t1 = 1f;
        for (int k = 0; k < 3; k++)
        {
            float a = k == 0 ? lp.X : k == 1 ? lp.Y : lp.Z;
            float b = k == 0 ? lq.X : k == 1 ? lq.Y : lq.Z;
            float h = k == 0 ? v.Half.X : k == 1 ? v.Half.Y : v.Half.Z;
            float d = b - a;
            if (d == 0f)
            {
                if (a < -h || a > h) return false;
                continue;
            }
            float e0 = (-h - a) / d, e1 = (h - a) / d;
            if (e0 > e1) (e0, e1) = (e1, e0);
            if (e0 > t0) t0 = e0;
            if (e1 < t1) t1 = e1;
            if (t0 > t1) return false;
        }
        float length = Vector3.Distance(pp, pq);
        _split.Clear();
        if ((t1 - t0) * length <= tol)
        {
            // The edge touches the box at one point: one of the box's own edges runs through it.
            // That point is a corner of T ∩ box (or of nothing, if T only grazes the box there).
            float tm = (t0 + t1) * 0.5f;
            if (tm * length > tol && (1f - tm) * length > tol) _split.Add(EdgePoint(p, q, tm));
        }
        else
        {
            if (t0 * length > tol) _split.Add(EdgePoint(p, q, t0));
            if ((1f - t1) * length > tol) _split.Add(EdgePoint(p, q, t1));
        }
        if (_split.Count == 0) return false;
        if (!forward) _split.Reverse();
        foreach (int s in _split)
        {
            _ring.Add(s);
            _ringIn.Add(true);
        }
        return true;
    }

    /// <summary>A point on an edge, its ends in canonical order. New points are shared by exact position.</summary>
    int EdgePoint(int p, int q, float t)
    {
        Vector3 pos = _pos[p] + (_pos[q] - _pos[p]) * t;
        Vector3 nrm = Vector3.Lerp(_nrm[p], _nrm[q], t);
        nrm = nrm.LengthSquared() > 1e-12f ? Vector3.Normalize(nrm) : _nrm[p];
        int near = t <= 0.5f ? p : q;
        return AddVertex(pos, nrm, _mat[near], _cube[near]);
    }

    int AddVertex(Vector3 pos, Vector3 nrm, CellMaterial mat, Int3 cube)
    {
        var key = (BitConverter.SingleToInt32Bits(pos.X), BitConverter.SingleToInt32Bits(pos.Y), BitConverter.SingleToInt32Bits(pos.Z));
        if (_added.TryGetValue(key, out int existing)) return existing;
        int index = _pos.Count;
        _pos.Add(pos);
        _nrm.Add(nrm);
        _mat.Add(mat);
        _cube.Add(cube);
        _added[key] = index;
        return index;
    }

    /// <summary>The points where the box's twelve edges pass through T's interior (not near its edges).</summary>
    void AddPierces(int ia, int ib, int ic, StructureVolume v, float tol)
    {
        Vector3 a = _pos[ia], b = _pos[ib], c = _pos[ic];
        Vector3 e1 = b - a, e2 = c - a;
        Vector3 hx = v.AxisX * v.Half.X, hy = v.AxisY * v.Half.Y, hz = v.AxisZ * v.Half.Z;
        for (int axis = 0; axis < 3; axis++)
        {
            Vector3 along = axis == 0 ? hx : axis == 1 ? hy : hz;
            Vector3 u = axis == 0 ? hy : axis == 1 ? hz : hx, w = axis == 0 ? hz : axis == 1 ? hx : hy;
            for (int s = 0; s < 4; s++)
            {
                Vector3 mid = v.Center + ((s & 1) == 0 ? -u : u) + ((s & 2) == 0 ? -w : w);
                Vector3 from = mid - along, dir = along * 2f;
                // Möller–Trumbore, with the segment's own parameter kept to [0, 1].
                Vector3 pv = Vector3.Cross(dir, e2);
                float det = Vector3.Dot(e1, pv);
                if (MathF.Abs(det) < 1e-12f) continue; // the box edge runs along T's plane
                float inv = 1f / det;
                Vector3 tv = from - a;
                float bu = Vector3.Dot(tv, pv) * inv;
                if (bu < 0f || bu > 1f) continue;
                Vector3 qv = Vector3.Cross(tv, e1);
                float bv = Vector3.Dot(dir, qv) * inv;
                if (bv < 0f || bu + bv > 1f) continue;
                float st = Vector3.Dot(e2, qv) * inv;
                if (st < 0f || st > 1f) continue;
                Vector3 x = from + dir * st;
                if (DistanceToSegment(x, a, b) <= tol * 2f || DistanceToSegment(x, b, c) <= tol * 2f || DistanceToSegment(x, c, a) <= tol * 2f)
                    continue; // on T's edge: the edge's own split point stands for it
                float bw = 1f - bu - bv;
                Vector3 nrm = _nrm[ia] * bw + _nrm[ib] * bu + _nrm[ic] * bv;
                nrm = nrm.LengthSquared() > 1e-12f ? Vector3.Normalize(nrm) : _nrm[ia];
                int near = bw >= bu && bw >= bv ? ia : bu >= bv ? ib : ic;
                int index = AddVertex(x, nrm, _mat[near], _cube[near]);
                if (!_pierce.Contains(index)) _pierce.Add(index);
            }
        }
    }

    static float DistanceToSegment(Vector3 p, Vector3 a, Vector3 b)
    {
        Vector3 ab = b - a;
        float t = Math.Clamp(Vector3.Dot(p - a, ab) / MathF.Max(ab.LengthSquared(), 1e-20f), 0f, 1f);
        return Vector3.Distance(p, a + ab * t);
    }

    void Flatten(int vertex, Vector3 origin, int drop, float sign)
    {
        if (_flat.ContainsKey(vertex)) return;
        Vector3 d = _pos[vertex] - origin;
        _flat[vertex] = drop == 0 ? new Vector2(d.Y * sign, d.Z) : drop == 1 ? new Vector2(d.Z * sign, d.X) : new Vector2(d.X * sign, d.Y);
    }

    static float Angle(Vector2 d) => MathF.Atan2(d.Y, d.X);

    /// <summary>
    /// T \ C where C touches T's edges: for every run of T's boundary outside C, from where it
    /// leaves C to where it meets it again, back along C's boundary to the start. Each such loop
    /// is a simple polygon, ear-clipped.
    /// </summary>
    bool Loops(float areaEps)
    {
        int n = _ring.Count;
        for (int i = 0; i < n; i++)
        {
            if (!_ringIn[i] || _ringIn[(i + 1) % n]) continue;
            _poly.Clear();
            _poly.Add(_ring[i]);
            int j = (i + 1) % n;
            while (!_ringIn[j])
            {
                _poly.Add(_ring[j]);
                j = (j + 1) % n;
            }
            _poly.Add(_ring[j]);
            // Back along C (clockwise) from where the ring came in to where it left.
            int k = IndexInC(j);
            if (k < 0) return false;
            for (int steps = 0; ; steps++)
            {
                k = (k - 1 + _c.Count) % _c.Count;
                (float _, int vertex, int ring) = _c[k];
                if (ring == i) break;
                if (ring >= 0 || steps > _c.Count) return false; // met another run of T's boundary first
                _poly.Add(vertex);
            }
            if (!EarClip(_poly, areaEps)) return false;
        }
        return true;
    }

    int IndexInC(int ringIndex)
    {
        for (int k = 0; k < _c.Count; k++)
            if (_c[k].ring == ringIndex) return k;
        return -1;
    }

    /// <summary>T with C as a hole: a bridge from a corner of T to a corner of C it can see, then ear clipping.</summary>
    bool Holed(float areaEps)
    {
        int m = _c.Count;
        Vector2 corner = _flat[_ring[0]];
        int best = -1;
        float bestD = float.MaxValue;
        for (int k = 0; k < m; k++)
        {
            Vector2 p = _flat[_c[k].vertex], next = _flat[_c[(k + 1) % m].vertex], prev = _flat[_c[(k - 1 + m) % m].vertex];
            // Visible from the corner if the corner is outside one of the two C edges at this point.
            bool visible = Cross(next - p, corner - p) < 0f || Cross(p - prev, corner - prev) < 0f;
            float d = Vector2.DistanceSquared(p, corner);
            if (visible && d < bestD)
            {
                bestD = d;
                best = k;
            }
        }
        if (best < 0) return false;
        _poly.Clear();
        _poly.AddRange(_ring);
        _poly.Add(_ring[0]);
        for (int s = 0; s <= m; s++) _poly.Add(_c[((best - s) % m + m) % m].vertex);
        return EarClip(_poly, areaEps);
    }

    static float Cross(Vector2 a, Vector2 b) => a.X * b.Y - a.Y * b.X;

    /// <summary>Ear clipping of a small anticlockwise polygon (it may touch itself at a bridge). Appends to _ears.</summary>
    bool EarClip(List<int> polygon, float areaEps)
    {
        var idx = new List<int>(polygon);
        int start = _ears.Count;
        while (idx.Count > 3)
        {
            int count = idx.Count;
            bool clipped = false;
            for (int i = 0; i < count && !clipped; i++)
            {
                int ip = idx[(i - 1 + count) % count], ic = idx[i], inx = idx[(i + 1) % count];
                Vector2 a = _flat[ip], b = _flat[ic], c = _flat[inx];
                if (Cross(b - a, c - b) <= areaEps) continue;
                bool blocked = false;
                for (int j = 0; j < count && !blocked; j++)
                {
                    int k = idx[j];
                    if (k == ip || k == ic || k == inx) continue;
                    Vector2 p = _flat[k];
                    if (p == a || p == b || p == c) continue;
                    blocked = Cross(b - a, p - a) >= -areaEps && Cross(c - b, p - b) >= -areaEps && Cross(a - c, p - c) >= -areaEps;
                }
                if (blocked) continue;
                _ears.Add((ip, ic, inx));
                idx.RemoveAt(i);
                clipped = true;
            }
            if (!clipped)
            {
                // Only slivers left (every corner flat, or nothing else will clip): done if it has no area.
                if (PolygonArea(idx) <= areaEps * idx.Count) return true;
                _ears.RemoveRange(start, _ears.Count - start);
                return false;
            }
        }
        if (idx.Count == 3 && Cross(_flat[idx[1]] - _flat[idx[0]], _flat[idx[2]] - _flat[idx[1]]) > areaEps)
            _ears.Add((idx[0], idx[1], idx[2]));
        return true;
    }

    float PolygonArea(List<int> idx)
    {
        float area = 0f;
        for (int i = 0; i < idx.Count; i++)
        {
            Vector2 p = _flat[idx[i]], q = _flat[idx[(i + 1) % idx.Count]];
            area += p.X * q.Y - q.X * p.Y;
        }
        return MathF.Abs(area) * 0.5f;
    }

    /// <summary>When a cut can't be made cleanly: keep the polygon as a fan, or drop it. Counted for the tests.</summary>
    void Fallback(List<int> polygon, bool keep)
    {
        Interlocked.Increment(ref _fallbacks);
        if (!keep) return;
        for (int i = 1; i + 1 < polygon.Count; i++)
            if (polygon[0] != polygon[i] && polygon[i] != polygon[i + 1] && polygon[0] != polygon[i + 1])
                _ears.Add((polygon[0], polygon[i], polygon[i + 1]));
    }

    static bool Less(Vector3 a, Vector3 b) => a.X != b.X ? a.X < b.X : a.Y != b.Y ? a.Y < b.Y : a.Z < b.Z;

    /// <summary>The cut mesh, keeping only vertices a triangle still uses.</summary>
    ChunkMesh Compact(Int3 chunk, bool cubes)
    {
        var remap = new int[_pos.Count];
        Array.Fill(remap, -1);
        int used = 0;
        foreach (int i in _tri)
            if (remap[i] < 0) remap[i] = used++;
        var positions = new Vector3[used];
        var normals = new Vector3[used];
        var materials = new CellMaterial[used];
        var cubeArray = cubes ? new Int3[used] : null;
        for (int i = 0; i < _pos.Count; i++)
        {
            int r = remap[i];
            if (r < 0) continue;
            positions[r] = _pos[i];
            normals[r] = _nrm[i];
            materials[r] = _mat[i];
            if (cubes) cubeArray[r] = _cube[i];
        }
        var indices = new int[_tri.Count];
        for (int i = 0; i < indices.Length; i++) indices[i] = remap[_tri[i]];
        return new ChunkMesh(chunk, positions, normals, materials, indices, _blk.ToArray(), cubeArray);
    }
}
