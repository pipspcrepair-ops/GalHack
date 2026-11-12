using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

namespace GalHack.Plants;

/// <summary>
/// A tree's canopy as colliders see it: every part of the mesh that is not a solid column (its leaf
/// lobes, tiers, skirts, caps, fronds and fungal bodies) as convex hulls of its own points, so the
/// collision is the canopy's real shape, not a box round the tree. Hulls are in the plant frame at scale 1
/// (the foot at the origin); an instance scales them with the tree. Parts the trunk columns already cover
/// are left to them.
/// </summary>
public static class PlantCanopy
{
    /// <summary>A part smaller than this across, m at scale 1, is a leaf or a bud: not solid.</summary>
    const float SmallestPart = 0.3f;

    /// <summary>A longer part is cut into pieces about this long, so a bent frond is not one fat hull.</summary>
    const float LongestPiece = 2.5f;

    /// <summary>The most hulls one near mesh gets: the nearest small ones merge past it (each is a physics shape).</summary>
    public const int MaxHulls = 24;

    /// <summary>A hull with more points than this is cut to its extremes in this many directions.</summary>
    const int MaxPoints = 48;

    /// <summary>Every hull is at least this thick, so a flat blade still has a volume (m).</summary>
    const float Thickness = 0.04f;

    static readonly Vector3[] Directions = Enumerable.Range(0, 64).Select(i =>
    {
        float y = 1f - 2f * (i + 0.5f) / 64, r = MathF.Sqrt(1f - y * y), a = MathF.PI * (3f - MathF.Sqrt(5f)) * i;
        return new Vector3(MathF.Cos(a) * r, y, MathF.Sin(a) * r);
    }).ToArray();

    /// <summary>The canopy hulls of one near mesh (point sets for convex shapes); empty if it has no canopy.</summary>
    public static Vector3[][] Hulls(PlantMesh mesh, PlantColumn[] columns)
    {
        var covered = new HashSet<int>(columns.Select(c => c.Part));
        var tris = new Dictionary<int, List<int>>();
        for (int t = 0; t < mesh.Triangles; t++)
        {
            int part = mesh.PartOf[t];
            if (part <= 0 || covered.Contains(part) || mesh.PartKind[part] == (byte)PartKind.Wood) continue;
            if (!tris.TryGetValue(part, out var list)) tris[part] = list = new List<int>();
            list.Add(t);
        }
        var groups = new List<List<Vector3>>();
        foreach (List<int> part in tris.Values)
        {
            Vector3 lo = new(float.MaxValue), hi = new(float.MinValue);
            foreach (int t in part)
                for (int v = 0; v < 3; v++) { lo = Vector3.Min(lo, mesh.Positions[t * 3 + v]); hi = Vector3.Max(hi, mesh.Positions[t * 3 + v]); }
            Vector3 size = hi - lo;
            if (size.Length() < SmallestPart || hi.Y < 0f) continue;
            // Cut along its longest axis, each triangle going to the piece its centre lies in.
            int axis = size.X >= size.Y && size.X >= size.Z ? 0 : size.Y >= size.Z ? 1 : 2;
            int pieces = Math.Max(1, (int)MathF.Ceiling(Component(size, axis) / LongestPiece));
            var cut = Enumerable.Range(0, pieces).Select(_ => new List<Vector3>()).ToArray();
            foreach (int t in part)
            {
                float c = (Component(mesh.Positions[t * 3] + mesh.Positions[t * 3 + 1] + mesh.Positions[t * 3 + 2], axis) / 3f - Component(lo, axis)) / Math.Max(Component(size, axis), 1e-4f);
                var piece = cut[Math.Clamp((int)(c * pieces), 0, pieces - 1)];
                for (int v = 0; v < 3; v++) piece.Add(mesh.Positions[t * 3 + v]);
            }
            groups.AddRange(cut.Where(p => p.Count > 0));
        }
        while (groups.Count > MaxHulls)
        {
            var centres = groups.Select(g => g.Aggregate(Vector3.Zero, (a, b) => a + b) / g.Count).ToList();
            int bi = 0, bj = 1;
            float best = float.MaxValue;
            for (int i = 0; i < groups.Count; i++)
                for (int j = i + 1; j < groups.Count; j++)
                {
                    float d = Vector3.DistanceSquared(centres[i], centres[j]);
                    if (d < best) { best = d; bi = i; bj = j; }
                }
            groups[bi].AddRange(groups[bj]);
            groups.RemoveAt(bj);
        }
        return groups.Select(Thicken).ToArray();
    }

    static float Component(Vector3 v, int axis) => axis == 0 ? v.X : axis == 1 ? v.Y : v.Z;

    /// <summary>A piece's own points, doubled either side of its widest face (a flat blade is a slab, not a plane), cut to the extremes if there are many.</summary>
    static Vector3[] Thicken(List<Vector3> points)
    {
        Vector3 normal = Vector3.UnitY;
        float area = 0f;
        for (int i = 0; i + 2 < points.Count; i += 3)
        {
            Vector3 n = Vector3.Cross(points[i + 1] - points[i], points[i + 2] - points[i]);
            if (n.Length() > area) { area = n.Length(); normal = n / area; }
        }
        Vector3[] slab = points.Distinct().SelectMany(q => new[] { q - normal * (Thickness / 2), q + normal * (Thickness / 2) }).ToArray();
        if (slab.Length <= MaxPoints) return slab;
        return Directions.Select(d => slab.MaxBy(q => Vector3.Dot(q, d))).Distinct().ToArray();
    }
}
