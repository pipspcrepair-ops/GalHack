using System;
using System.Collections.Generic;
using System.Numerics;
using GalHack.Geometry;

namespace GalHack.Deco;

/// <summary>
/// The deco models' rules against z-fighting, kept by construction rather than piece by piece, on the finished mesh:
/// <list type="bullet">
/// <item>nothing lies flat in the floor plane: a face there either rests on the floor (never seen) or was squashed
/// into it (a buried or crushed part), and the room's own floor is drawn there, so it is dropped;</item>
/// <item>two parts whose faces of different looks lie in one plane over each other are set apart: the smaller
/// (a trim, a panel, a label, a leaf) stands <see cref="Coplanar.Standoff"/> off the bigger, out along its face,
/// or the bigger steps back where the smaller may not move (into the floor, out of its footprint).</item>
/// </list>
/// A part is the span of triangles one model part emitted (<see cref="Parts.Next"/>); what is laid on after the
/// model (<see cref="Parts.Close"/>: marks, growth, drifts) is a part per quad.
/// </summary>
public static class DecoStandoff
{
    /// <summary>Where each part's triangles start on each surface, and where the model's own parts end.</summary>
    public sealed class Parts
    {
        readonly DecoMeshData _mesh;
        public readonly List<int[]> Starts = new();
        public int[] Closed;

        public Parts(DecoMeshData mesh) => _mesh = mesh;

        int[] Now()
        {
            var at = new int[_mesh.Surfaces.Length];
            for (int s = 0; s < at.Length; s++) at[s] = _mesh.Surfaces[s].Triangles;
            return at;
        }

        /// <summary>What is added from here on belongs to the next part.</summary>
        public void Next() => Starts.Add(Now());

        /// <summary>The model's parts are done: what comes after is laid on them.</summary>
        public void Close() => Closed = Now();
    }

    public static void Apply(DecoMeshData mesh, Parts parts, (Vector3 Min, Vector3 Max)? footprint, bool floor = true)
    {
        const float Flat = 1e-4f;
        var faces = new List<Coplanar.Face>();
        var group = new List<int>();
        var size = new List<float>();
        var lo = new List<Vector3>();
        var hi = new List<Vector3>();
        var where = new List<(int Surface, int Tri)>();
        var drop = new HashSet<(int, int)>();
        int marked = parts.Starts.Count;
        int Grow(int g)
        {
            while (size.Count <= g) { size.Add(0f); lo.Add(new Vector3(float.MaxValue)); hi.Add(new Vector3(float.MinValue)); }
            return g;
        }
        Grow(marked - 1);
        for (int s = 0; s < mesh.Surfaces.Length; s++)
        {
            DecoSurfaceData d = mesh.Surfaces[s];
            int part = -1, loose = -1;
            for (int t = 0; t < d.Triangles; t++)
            {
                Vector3 a = d.Positions[t * 3], b = d.Positions[t * 3 + 1], c = d.Positions[t * 3 + 2];
                if (floor && MathF.Abs(a.Y) < Flat && MathF.Abs(b.Y) < Flat && MathF.Abs(c.Y) < Flat) { drop.Add((s, t)); loose = -1; continue; }
                while (part + 1 < marked && parts.Starts[part + 1][s] <= t) part++;
                int g;
                if (part >= 0 && (parts.Closed == null || t < parts.Closed[s])) g = part;
                else
                {
                    // Laid on: a quad is two triangles in a row sharing an edge.
                    if (loose < 0 || Shared(d, t - 1, t) < 2) loose = Grow(size.Count);
                    g = loose;
                }
                faces.Add(new Coplanar.Face(a, b, c, d.Normals[t * 3], Coplanar.Look(s, d.Colors[t * 3]), s));
                group.Add(g);
                where.Add((s, t));
                size[g] += Vector3.Cross(b - a, c - a).Length() * 0.5f;
                lo[g] = Vector3.Min(lo[g], Vector3.Min(a, Vector3.Min(b, c)));
                hi[g] = Vector3.Max(hi[g], Vector3.Max(a, Vector3.Max(b, c)));
            }
        }
        // A part with body swells by a standoff on every side (about its middle, its foot kept on the floor), so
        // whichever of its faces lay flush with a bigger part's now stands proud of it, at both ends at once; a flat
        // part (a card, a leaf, a mark) steps out along its face. Each part moves once; then the bigger one may.
        var moved = new bool[size.Count];
        var work = new List<Coplanar.Face>(faces);
        Vector3 Move(int g, Vector3 v, Vector3 step)
        {
            if (step != Vector3.Zero) return v + step;
            Vector3 mid = (lo[g] + hi[g]) * 0.5f, half = (hi[g] - lo[g]) * 0.5f;
            bool grounded = lo[g].Y < Coplanar.Standoff + 1e-4f;
            float Grow(float x, float m, float h) => h < 1e-4f ? x : m + (x - m) * (h + Coplanar.Standoff) / h;
            return new Vector3(Grow(v.X, mid.X, half.X),
                grounded ? (half.Y < 1e-4f ? v.Y : lo[g].Y + (v.Y - lo[g].Y) * (2f * half.Y + Coplanar.Standoff) / (2f * half.Y)) : Grow(v.Y, mid.Y, half.Y),
                Grow(v.Z, mid.Z, half.Z));
        }
        for (int round = 0; round < 6 && faces.Count > 1; round++)
        {
            List<Coplanar.Pair> pairs = Coplanar.Find(work);
            if (pairs.Count == 0) break;
            var plan = new Dictionary<int, Vector3>();
            foreach (Coplanar.Pair pr in pairs)
            {
                int gp = group[pr.P], gq = group[pr.Q];
                if (gp == gq || plan.ContainsKey(gp) || plan.ContainsKey(gq)) continue;
                bool pFirst = size[gp] < size[gq] || size[gp] == size[gq] && gp > gq;
                int g = pFirst ? gp : gq, f = pFirst ? pr.P : pr.Q;
                if (moved[g]) { (g, f) = pFirst ? (gq, pr.Q) : (gp, pr.P); }
                bool flat = MathF.Min(hi[g].X - lo[g].X, MathF.Min(hi[g].Y - lo[g].Y, hi[g].Z - lo[g].Z)) < 1e-3f;
                Vector3 step = flat || moved[g] ? Coplanar.Front(work[f]) * Coplanar.Standoff : Vector3.Zero;
                // Nothing steps down through the floor.
                if (step.Y < 0f && lo[g].Y + step.Y < 0f) step = -step;
                plan[g] = step;
            }
            if (plan.Count == 0) break;
            for (int i = 0; i < work.Count; i++)
            {
                if (!plan.TryGetValue(group[i], out Vector3 step)) continue;
                int g = group[i];
                Coplanar.Face w = work[i];
                work[i] = w with { A = Move(g, w.A, step), B = Move(g, w.B, step), C = Move(g, w.C, step) };
            }
            foreach (var kv in plan)
            {
                moved[kv.Key] = true;
                Vector3 l = Move(kv.Key, lo[kv.Key], kv.Value), h = Move(kv.Key, hi[kv.Key], kv.Value);
                lo[kv.Key] = l;
                hi[kv.Key] = h;
            }
        }
        // A piece that must fit its footprint may stand a standoff proud of it, no more.
        Vector3 fMin = footprint?.Min - new Vector3(Coplanar.Standoff) ?? new Vector3(float.MinValue), fMax = footprint?.Max + new Vector3(Coplanar.Standoff) ?? new Vector3(float.MaxValue);
        fMin.Y = 0f;
        for (int i = 0; i < faces.Count; i++)
        {
            if (work[i] == faces[i]) continue;
            var (s, t) = where[i];
            List<Vector3> p = mesh.Surfaces[s].Positions;
            p[t * 3] = Vector3.Clamp(work[i].A, fMin, fMax); p[t * 3 + 1] = Vector3.Clamp(work[i].B, fMin, fMax); p[t * 3 + 2] = Vector3.Clamp(work[i].C, fMin, fMax);
        }        if (drop.Count > 0)
            for (int s = 0; s < mesh.Surfaces.Length; s++)
            {
                DecoSurfaceData d = mesh.Surfaces[s];
                while (d.Faces.Count < d.Positions.Count) d.Faces.Add(default);
                int keep = 0, n = d.Triangles;
                for (int t = 0; t < n; t++)
                {
                    if (drop.Contains((s, t))) continue;
                    for (int k = 0; k < 3; k++)
                    {
                        d.Positions[keep * 3 + k] = d.Positions[t * 3 + k];
                        d.Normals[keep * 3 + k] = d.Normals[t * 3 + k];
                        d.Colors[keep * 3 + k] = d.Colors[t * 3 + k];
                        d.Faces[keep * 3 + k] = d.Faces[t * 3 + k];
                    }
                    keep++;
                }
                int from = keep * 3, cut = d.Positions.Count - from;
                if (cut == 0) continue;
                d.Positions.RemoveRange(from, cut);
                d.Normals.RemoveRange(from, cut);
                d.Colors.RemoveRange(from, cut);
                d.Faces.RemoveRange(from, d.Faces.Count - from);
            }
        mesh.RecomputeBounds();
    }

    static int Shared(DecoSurfaceData d, int t0, int t1)
    {
        int n = 0;
        for (int i = 0; i < 3; i++)
            for (int j = 0; j < 3; j++)
                if (d.Positions[t0 * 3 + i] == d.Positions[t1 * 3 + j]) { n++; break; }
        return n;
    }
}
