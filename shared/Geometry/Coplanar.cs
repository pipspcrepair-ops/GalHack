#nullable enable
using System;
using System.Collections.Generic;
using System.Numerics;

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

    /// <summary>Every fighting pair among the faces (indices into the list).</summary>
    public static List<Pair> Find(IReadOnlyList<Face> faces, float gap = Gap, float minArea = MinArea)
    {
        int n = faces.Count;
        var nrm = new Vector3[n];
        var d = new float[n];
        var lo = new (int X, int Y, int Z)[n];
        const float Cell = 2f, Slab = 0.004f;
        // Buckets by facing, plane (4 mm slabs) and 2 m cell, as one sorted array of hashed keys: a face in every
        // cell its box touches; a pair is tried only in the first cell both share, so never twice.
        var entries = new List<(ulong Key, int Face, int X, int Y, int Z)>(n + n / 2);
        for (int i = 0; i < n; i++)
        {
            Face f = faces[i];
            Vector3 c = Vector3.Cross(f.B - f.A, f.C - f.A);
            float len = c.Length();
            if (len < 2e-7f) continue; // no area
            c /= len;
            if (f.Front != Vector3.Zero && Vector3.Dot(c, f.Front) < 0f) c = -c;
            nrm[i] = c;
            d[i] = Vector3.Dot(c, (f.A + f.B + f.C) / 3f);
            int nx = (int)MathF.Round(c.X * 40f), ny = (int)MathF.Round(c.Y * 40f), nz = (int)MathF.Round(c.Z * 40f);
            int dq = (int)MathF.Floor(d[i] / Slab);
            Vector3 l = Vector3.Min(f.A, Vector3.Min(f.B, f.C)), h = Vector3.Max(f.A, Vector3.Max(f.B, f.C));
            lo[i] = ((int)MathF.Floor(l.X / Cell), (int)MathF.Floor(l.Y / Cell), (int)MathF.Floor(l.Z / Cell));
            for (int x = lo[i].X; x <= (int)MathF.Floor(h.X / Cell); x++)
                for (int y = lo[i].Y; y <= (int)MathF.Floor(h.Y / Cell); y++)
                    for (int z = lo[i].Z; z <= (int)MathF.Floor(h.Z / Cell); z++)
                        entries.Add((Hash(nx, ny, nz, dq, x, y, z), i, x, y, z));
        }
        var keys = new ulong[entries.Count];
        var order = new int[entries.Count];
        for (int i = 0; i < keys.Length; i++) { keys[i] = entries[i].Key; order[i] = i; }
        Array.Sort(keys, order);
        var pairs = new List<Pair>();
        for (int start = 0; start < keys.Length;)
        {
            int end = start + 1;
            while (end < keys.Length && keys[end] == keys[start]) end++;
            var (_, f0, x, y, z) = entries[order[start]];
            Vector3 c = nrm[f0];
            int dq = (int)MathF.Floor(d[f0] / Slab);
            ulong up = Hash((int)MathF.Round(c.X * 40f), (int)MathF.Round(c.Y * 40f), (int)MathF.Round(c.Z * 40f), dq + 1, x, y, z);
            int a0 = Array.BinarySearch(keys, up), a1 = a0;
            if (a0 >= 0)
            {
                while (a0 > 0 && keys[a0 - 1] == up) a0--;
                while (a1 < keys.Length && keys[a1] == up) a1++;
            }
            for (int a = start; a < end; a++)
            {
                for (int b = a + 1; b < end; b++) Test(entries[order[a]].Face, entries[order[b]].Face, x, y, z);
                if (a0 >= 0) for (int b = a0; b < a1; b++) Test(entries[order[a]].Face, entries[order[b]].Face, x, y, z);
            }
            start = end;
        }
        return pairs;

        void Test(int p, int q, int x, int y, int z)
        {
            if (p == q || faces[p].Look == faces[q].Look) return;
            // Only in the first cell the two share.
            if (Math.Max(lo[p].X, lo[q].X) != x || Math.Max(lo[p].Y, lo[q].Y) != y || Math.Max(lo[p].Z, lo[q].Z) != z) return;
            if (Vector3.Dot(nrm[p], nrm[q]) < 0.9995f) return;
            float gapPq = MathF.Abs(d[p] - d[q]);
            if (gapPq > gap) return;
            float area = Shared(faces[p], faces[q], nrm[p]);
            if (area >= minArea) pairs.Add(new Pair(Math.Min(p, q), Math.Max(p, q), gapPq, area));
        }
    }

    static ulong Hash(int nx, int ny, int nz, int dq, int x, int y, int z)
    {
        ulong h = 1469598103934665603UL;
        foreach (int v in stackalloc int[] { nx, ny, nz, dq, x, y, z }) h = (h ^ (uint)v) * 1099511628211UL;
        return h ^ (h >> 29);
    }

    /// <summary>
    /// Sets fighting parts apart: face <c>i</c> belongs to part <c>group[i]</c>, and of two parts whose faces
    /// fight the smaller (<paramref name="size"/>) is what lies on the other, so it moves <paramref name="standoff"/>
    /// out along its fighting face's normal (ties: the later part). Rounds repeat for parts stacked three deep.
    /// <paramref name="may"/> can refuse a move (a part must not sink through the floor): the other part moves
    /// back instead. Returns each part's offset; the caller moves its own vertices.
    /// </summary>
    public static Vector3[] Separate(IReadOnlyList<Face> faces, IReadOnlyList<int> group, IReadOnlyList<float> size, Func<int, Vector3, bool>? may = null, float standoff = Standoff, int rounds = 8)
    {
        var offset = new Vector3[size.Count];
        var work = new List<Face>(faces);
        var ids = new List<int>(faces.Count);
        for (int i = 0; i < faces.Count; i++) ids.Add(i);
        for (int r = 0; r < rounds; r++)
        {
            List<Pair> pairs = Find(work);
            if (pairs.Count == 0) break;
            if (r == 0)
            {
                // Later rounds look only at the planes where something fought: the rest of the mesh is done.
                var hot = new HashSet<(int, int, int, int)>();
                foreach (Pair p in pairs)
                    foreach (int i in new[] { p.P, p.Q })
                    {
                        var (nx, ny, nz, s) = PlaneOf(work[i]);
                        for (int k = -2; k <= 2; k++) hot.Add((nx, ny, nz, s + k));
                    }
                var keep = new List<Face>();
                var keepIds = new List<int>();
                for (int i = 0; i < work.Count; i++)
                    if (hot.Contains(PlaneOf(work[i]))) { keep.Add(work[i]); keepIds.Add(ids[i]); }
                // The pairs found point into the full list; map them onto the kept one.
                var at = new Dictionary<int, int>();
                for (int i = 0; i < keepIds.Count; i++) at[keepIds[i]] = i;
                for (int i = 0; i < pairs.Count; i++) pairs[i] = pairs[i] with { P = at[ids[pairs[i].P]], Q = at[ids[pairs[i].Q]] };
                work = keep;
                ids = keepIds;
            }
            var step = new Dictionary<int, Vector3>();
            foreach (Pair p in pairs)
            {
                int gp = group[ids[p.P]], gq = group[ids[p.Q]];
                if (gp == gq) continue;
                bool pMoves = size[gp] < size[gq] || size[gp] == size[gq] && gp > gq;
                int mover = pMoves ? gp : gq, other = pMoves ? gq : gp;
                if (step.ContainsKey(mover) || step.ContainsKey(other)) continue;
                Vector3 s = Front(work[pMoves ? p.P : p.Q]) * standoff;
                if (may != null && !may(mover, offset[mover] + s))
                {
                    if (!may(other, offset[other] - s)) continue;
                    (mover, s) = (other, -s);
                }
                step[mover] = s;
            }
            if (step.Count == 0) break;
            foreach (var kv in step) offset[kv.Key] += kv.Value;
            for (int i = 0; i < work.Count; i++)
                if (step.TryGetValue(group[ids[i]], out Vector3 s)) work[i] = work[i] with { A = work[i].A + s, B = work[i].B + s, C = work[i].C + s };
        }
        return offset;
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
        Vector2 P(Vector3 w) => new(Vector3.Dot(w - o, u), Vector3.Dot(w - o, v));
        var poly = new List<Vector2> { P(q.A), P(q.B), P(q.C) };
        Vector2[] clip = { P(p.A), P(p.B), P(p.C) };
        // Bounding boxes first: most neighbours in a bucket don't touch.
        if (MathF.Max(clip[0].X, MathF.Max(clip[1].X, clip[2].X)) <= MathF.Min(poly[0].X, MathF.Min(poly[1].X, poly[2].X))
            || MathF.Min(clip[0].X, MathF.Min(clip[1].X, clip[2].X)) >= MathF.Max(poly[0].X, MathF.Max(poly[1].X, poly[2].X))
            || MathF.Max(clip[0].Y, MathF.Max(clip[1].Y, clip[2].Y)) <= MathF.Min(poly[0].Y, MathF.Min(poly[1].Y, poly[2].Y))
            || MathF.Min(clip[0].Y, MathF.Min(clip[1].Y, clip[2].Y)) >= MathF.Max(poly[0].Y, MathF.Max(poly[1].Y, poly[2].Y))) return 0f;
        if (Cross(clip[1] - clip[0], clip[2] - clip[0]) < 0f) (clip[1], clip[2]) = (clip[2], clip[1]);
        for (int e = 0; e < 3 && poly.Count > 0; e++)
        {
            Vector2 a = clip[e], b = clip[(e + 1) % 3];
            var next = new List<Vector2>(poly.Count + 2);
            for (int i = 0; i < poly.Count; i++)
            {
                Vector2 s = poly[i], t = poly[(i + 1) % poly.Count];
                float ds = Cross(b - a, s - a), dt = Cross(b - a, t - a);
                if (ds >= 0f) next.Add(s);
                if (ds >= 0f != dt >= 0f) next.Add(s + (t - s) * (ds / (ds - dt)));
            }
            poly = next;
        }
        float area = 0f;
        for (int i = 0; i < poly.Count; i++) area += Cross(poly[i], poly[(i + 1) % poly.Count]);
        return MathF.Abs(area) * 0.5f;
    }

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
