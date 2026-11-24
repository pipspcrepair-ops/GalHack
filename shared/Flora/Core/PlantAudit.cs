using System;
using System.Collections.Generic;
using System.Numerics;

namespace GalHack.Plants;

/// <summary>What a mesh part is: foliage and bodies, wood (a tube: trunk, limb, branch) or a lobe (a lump of leaves on a crown).</summary>
public enum PartKind : byte { Body = 0, Wood = 1, Lobe = 2 }

/// <summary>
/// Measures whether a plant mesh holds together, from its triangles alone. A part is one primitive
/// the sculptor laid down (a tube, a lump, a skirt, a blade). Two parts touch when any two of their
/// triangles come within <see cref="Touch"/> of each other, through each other or not. A part is held
/// when a chain of touching parts leads to one that stands in the ground; one that doesn't is adrift.
/// The sculptor welds with the same rule, so nothing it builds is left floating.
/// </summary>
public static class PlantAudit
{
    /// <summary>How near two triangles must come to count as touching, m at scale 1.</summary>
    public const float Touch = 0.12f;

    /// <summary>A part with a vertex at or below this height is in the ground.</summary>
    public const float RootHeight = 0.4f;

    /// <summary>Every body plan that is one plant on one stem or a few, not the low cover and crops (many small separate pieces): nothing of it may float.</summary>
    public static bool Joined(PlantForm form) =>
        form is not (PlantForm.Tussock or PlantForm.Flowers or PlantForm.Twigs or PlantForm.Reeds or PlantForm.Lichen or PlantForm.PinStalks or PlantForm.GrainCrop or PlantForm.LeafCrop or PlantForm.VineCrop);

    /// <summary>Body plans with a trunk that ends where limbs or a crown begin: that end must meet them.</summary>
    public static bool Crowned(PlantForm form) =>
        form is PlantForm.Spire or PlantForm.Pagoda or PlantForm.Spindle or PlantForm.Stacked or PlantForm.Domed or PlantForm.Umbrella or PlantForm.Weeping
            or PlantForm.CloudPads or PlantForm.Bottle or PlantForm.Pole or PlantForm.Palm or PlantForm.FanPalm or PlantForm.SpikeTree or PlantForm.Lantern
            or PlantForm.Capped or PlantForm.Parasol;

    public sealed class Report
    {
        public int Parts;
        /// <summary>Parts grouped by what touches what: the group each part is in.</summary>
        public int[] Group;
        /// <summary>Whether each group has a part standing in the ground.</summary>
        public bool[] GroupRooted;
        public bool[] Rooted;
        public bool[,] Touching;
        public int[] First, Count;
        public int[] Tris;
        public Vector3[] Min, Max;

        public IEnumerable<int> Adrift()
        {
            for (int p = 1; p <= Parts; p++)
                if (Count[p] > 0 && !GroupRooted[Group[p]]) yield return p;
        }

        /// <summary>Lobes of leaves that no wood reaches: a crown's mass the branches stop short of.</summary>
        public IEnumerable<int> Loose(PlantMesh m)
        {
            bool anyWood = false;
            for (int p = 1; p <= Parts; p++) if (Count[p] > 0 && m.PartKind[p] == (byte)PartKind.Wood) anyWood = true;
            if (!anyWood) yield break;
            for (int p = 1; p <= Parts; p++)
            {
                if (Count[p] == 0 || m.PartKind[p] != (byte)PartKind.Lobe) continue;
                bool held = false;
                for (int q = 1; q <= Parts && !held; q++)
                    held = q != p && Count[q] > 0 && m.PartKind[q] == (byte)PartKind.Wood && Touching[p, q];
                if (!held) yield return p;
            }
        }
    }

    public static Report Analyse(PlantMesh m, float eps = Touch)
    {
        int parts = 0;
        foreach (int p in m.PartOf) parts = Math.Max(parts, p);
        var r = new Report
        {
            Parts = parts,
            Group = new int[parts + 1],
            Rooted = new bool[parts + 1],
            Touching = new bool[parts + 1, parts + 1],
            First = new int[parts + 1],
            Count = new int[parts + 1],
            Tris = new int[m.PartOf.Length],
            Min = new Vector3[parts + 1],
            Max = new Vector3[parts + 1],
        };
        // Triangles grouped by part.
        var order = new int[m.PartOf.Length];
        for (int t = 0; t < order.Length; t++) r.Count[m.PartOf[t]]++;
        for (int p = 1; p <= parts; p++) r.First[p] = r.First[p - 1] + r.Count[p - 1];
        var fill = new int[parts + 1];
        for (int t = 0; t < order.Length; t++)
        {
            int p = m.PartOf[t];
            order[r.First[p] + fill[p]++] = t;
        }
        r.Tris = order;
        for (int p = 1; p <= parts; p++)
        {
            var lo = new Vector3(float.MaxValue);
            var hi = new Vector3(float.MinValue);
            for (int k = 0; k < r.Count[p]; k++)
                for (int c = 0; c < 3; c++)
                {
                    Vector3 v = m.Positions[order[r.First[p] + k] * 3 + c];
                    lo = Vector3.Min(lo, v);
                    hi = Vector3.Max(hi, v);
                }
            r.Min[p] = lo;
            r.Max[p] = hi;
            r.Rooted[p] = r.Count[p] > 0 && lo.Y <= RootHeight;
        }
        var parent = new int[parts + 1];
        for (int p = 0; p <= parts; p++) parent[p] = p;
        int Find(int x)
        {
            while (parent[x] != x) x = parent[x] = parent[parent[x]];
            return x;
        }
        for (int a = 1; a <= parts; a++)
        {
            if (r.Count[a] == 0) continue;
            for (int b = a + 1; b <= parts; b++)
            {
                if (r.Count[b] == 0) continue;
                if (r.Min[a].X > r.Max[b].X + eps || r.Min[b].X > r.Max[a].X + eps || r.Min[a].Y > r.Max[b].Y + eps || r.Min[b].Y > r.Max[a].Y + eps
                    || r.Min[a].Z > r.Max[b].Z + eps || r.Min[b].Z > r.Max[a].Z + eps) continue;
                if (!PartsTouch(m, r, a, b, eps)) continue;
                r.Touching[a, b] = r.Touching[b, a] = true;
                parent[Find(a)] = Find(b);
            }
        }
        r.GroupRooted = new bool[parts + 1];
        for (int p = 1; p <= parts; p++)
        {
            r.Group[p] = Find(p);
            if (r.Rooted[p]) r.GroupRooted[r.Group[p]] = true;
        }
        return r;
    }

    static bool PartsTouch(PlantMesh m, Report r, int a, int b, float eps)
    {
        for (int i = 0; i < r.Count[a]; i++)
        {
            int ta = r.Tris[r.First[a] + i] * 3;
            Vector3 a0 = m.Positions[ta], a1 = m.Positions[ta + 1], a2 = m.Positions[ta + 2];
            Vector3 alo = Vector3.Min(a0, Vector3.Min(a1, a2)) - new Vector3(eps), ahi = Vector3.Max(a0, Vector3.Max(a1, a2)) + new Vector3(eps);
            for (int j = 0; j < r.Count[b]; j++)
            {
                int tb = r.Tris[r.First[b] + j] * 3;
                Vector3 b0 = m.Positions[tb], b1 = m.Positions[tb + 1], b2 = m.Positions[tb + 2];
                Vector3 blo = Vector3.Min(b0, Vector3.Min(b1, b2)), bhi = Vector3.Max(b0, Vector3.Max(b1, b2));
                if (alo.X > bhi.X || blo.X > ahi.X || alo.Y > bhi.Y || blo.Y > ahi.Y || alo.Z > bhi.Z || blo.Z > ahi.Z) continue;
                if (TrianglesWithin(a0, a1, a2, b0, b1, b2, eps)) return true;
            }
        }
        return false;
    }

    /// <summary>
    /// Whether the top of a trunk (part 1, the first thing a body plan lays down) meets something: any triangle of
    /// the trunk near its top touches a triangle of another part (a limb leaving it, a crown over it, fronds from it).
    /// </summary>
    public static bool TrunkMeets(PlantMesh m, Vector3 top)
    {
        for (int t = 0; t < m.PartOf.Length; t++)
        {
            if (m.PartOf[t] != 1) continue;
            Vector3 a = m.Positions[t * 3], b = m.Positions[t * 3 + 1], c = m.Positions[t * 3 + 2];
            if (Vector3.Distance(a, top) > 0.7f && Vector3.Distance(b, top) > 0.7f && Vector3.Distance(c, top) > 0.7f) continue;
            for (int u = 0; u < m.PartOf.Length; u++)
            {
                if (m.PartOf[u] == 1) continue;
                if (TrianglesWithin(a, b, c, m.Positions[u * 3], m.Positions[u * 3 + 1], m.Positions[u * 3 + 2], Touch)) return true;
            }
        }
        return false;
    }

    // ---- Triangle geometry --------------------------------------------------------------------

    /// <summary>Whether two triangles pass through each other or come within eps.</summary>
    public static bool TrianglesWithin(Vector3 a0, Vector3 a1, Vector3 a2, Vector3 b0, Vector3 b1, Vector3 b2, float eps)
    {
        if (SegmentThrough(a0, a1, b0, b1, b2) || SegmentThrough(a1, a2, b0, b1, b2) || SegmentThrough(a2, a0, b0, b1, b2)
            || SegmentThrough(b0, b1, a0, a1, a2) || SegmentThrough(b1, b2, a0, a1, a2) || SegmentThrough(b2, b0, a0, a1, a2)) return true;
        float best = float.MaxValue;
        foreach (Vector3 p in new[] { a0, a1, a2 }) best = MathF.Min(best, Vector3.DistanceSquared(p, Closest(p, b0, b1, b2)));
        foreach (Vector3 p in new[] { b0, b1, b2 }) best = MathF.Min(best, Vector3.DistanceSquared(p, Closest(p, a0, a1, a2)));
        Vector3[] ea = { a0, a1, a2 }, eb = { b0, b1, b2 };
        for (int i = 0; i < 3; i++)
            for (int j = 0; j < 3; j++)
                best = MathF.Min(best, SegmentGapSq(ea[i], ea[(i + 1) % 3], eb[j], eb[(j + 1) % 3]));
        return best <= eps * eps;
    }

    static bool SegmentThrough(Vector3 p, Vector3 q, Vector3 a, Vector3 b, Vector3 c)
    {
        Vector3 d = q - p, e1 = b - a, e2 = c - a, h = Vector3.Cross(d, e2);
        float det = Vector3.Dot(e1, h);
        if (MathF.Abs(det) < 1e-9f) return false;
        float inv = 1f / det;
        Vector3 s = p - a;
        float u = inv * Vector3.Dot(s, h);
        if (u < 0f || u > 1f) return false;
        Vector3 qv = Vector3.Cross(s, e1);
        float v = inv * Vector3.Dot(d, qv);
        if (v < 0f || u + v > 1f) return false;
        float t = inv * Vector3.Dot(e2, qv);
        return t >= 0f && t <= 1f;
    }

    /// <summary>The nearest point of a triangle to a point.</summary>
    public static Vector3 Closest(Vector3 p, Vector3 a, Vector3 b, Vector3 c)
    {
        Vector3 ab = b - a, ac = c - a, ap = p - a;
        float d1 = Vector3.Dot(ab, ap), d2 = Vector3.Dot(ac, ap);
        if (d1 <= 0f && d2 <= 0f) return a;
        Vector3 bp = p - b;
        float d3 = Vector3.Dot(ab, bp), d4 = Vector3.Dot(ac, bp);
        if (d3 >= 0f && d4 <= d3) return b;
        float vc = d1 * d4 - d3 * d2;
        if (vc <= 0f && d1 >= 0f && d3 <= 0f) return a + ab * (d1 / (d1 - d3));
        Vector3 cp = p - c;
        float d5 = Vector3.Dot(ab, cp), d6 = Vector3.Dot(ac, cp);
        if (d6 >= 0f && d5 <= d6) return c;
        float vb = d5 * d2 - d1 * d6;
        if (vb <= 0f && d2 >= 0f && d6 <= 0f) return a + ac * (d2 / (d2 - d6));
        float va = d3 * d6 - d5 * d4;
        if (va <= 0f && d4 - d3 >= 0f && d5 - d6 >= 0f) return b + (c - b) * ((d4 - d3) / (d4 - d3 + d5 - d6));
        float denom = 1f / (va + vb + vc);
        return a + ab * (vb * denom) + ac * (vc * denom);
    }

    static float SegmentGapSq(Vector3 p1, Vector3 q1, Vector3 p2, Vector3 q2)
    {
        Vector3 d1 = q1 - p1, d2 = q2 - p2, r = p1 - p2;
        float a = Vector3.Dot(d1, d1), e = Vector3.Dot(d2, d2), f = Vector3.Dot(d2, r);
        float s, t;
        if (a <= 1e-12f && e <= 1e-12f) return r.LengthSquared();
        if (a <= 1e-12f) { s = 0f; t = Math.Clamp(f / e, 0f, 1f); }
        else
        {
            float c = Vector3.Dot(d1, r);
            if (e <= 1e-12f) { t = 0f; s = Math.Clamp(-c / a, 0f, 1f); }
            else
            {
                float b = Vector3.Dot(d1, d2), denom = a * e - b * b;
                s = denom > 1e-12f ? Math.Clamp((b * f - c * e) / denom, 0f, 1f) : 0f;
                t = (b * s + f) / e;
                if (t < 0f) { t = 0f; s = Math.Clamp(-c / a, 0f, 1f); }
                else if (t > 1f) { t = 1f; s = Math.Clamp((b - c) / a, 0f, 1f); }
            }
        }
        return Vector3.DistanceSquared(p1 + d1 * s, p2 + d2 * t);
    }
}
