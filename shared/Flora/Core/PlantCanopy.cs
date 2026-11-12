using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

namespace GalHack.Plants;

/// <summary>
/// A tree's canopy as colliders see it: every part of the mesh that is not a solid column (its leaf
/// lobes, tiers, skirts, caps, fronds and fungal bodies) as a convex hull of its own points, so the
/// collision is the canopy's real shape, not a box round the tree. Hulls are in the plant frame at scale 1
/// (the foot at the origin); an instance scales them with the tree. Parts the trunk columns already cover
/// are left to them.
/// </summary>
public static class PlantCanopy
{
    /// <summary>A part smaller than this across, m at scale 1, is a leaf or a bud: not solid.</summary>
    const float SmallestPart = 0.3f;

    /// <summary>The most hulls one near mesh gets: neighbouring parts merge past it (each hull is a physics shape).</summary>
    public const int MaxHulls = 14;

    /// <summary>The most points a hull keeps.</summary>
    const int MaxPoints = 40;

    /// <summary>Every hull is at least this thick, so a flat blade still has a volume (m).</summary>
    const float Thickness = 0.04f;

    static readonly Vector3[] Directions = MakeDirections();

    static Vector3[] MakeDirections()
    {
        var d = new List<Vector3>();
        int n = MaxPoints;
        float golden = MathF.PI * (3f - MathF.Sqrt(5f));
        for (int i = 0; i < n; i++)
        {
            float y = 1f - 2f * (i + 0.5f) / n, r = MathF.Sqrt(1f - y * y), a = golden * i;
            d.Add(new Vector3(MathF.Cos(a) * r, y, MathF.Sin(a) * r));
        }
        return d.ToArray();
    }

    /// <summary>The canopy hulls of one near mesh (point sets for convex shapes); empty if it has no canopy.</summary>
    public static Vector3[][] Hulls(PlantMesh mesh, PlantColumn[] columns)
    {
        var covered = new HashSet<int>(columns.Select(c => c.Part));
        var byPart = new Dictionary<int, List<Vector3>>();
        for (int t = 0; t < mesh.Triangles; t++)
        {
            int part = mesh.PartOf[t];
            if (part <= 0 || covered.Contains(part) || mesh.PartKind[part] == (byte)PartKind.Wood) continue;
            if (!byPart.TryGetValue(part, out var list)) byPart[part] = list = new List<Vector3>();
            for (int v = 0; v < 3; v++) list.Add(mesh.Positions[t * 3 + v]);
        }
        var groups = new List<List<Vector3>>();
        foreach (var list in byPart.Values)
        {
            Vector3 lo = new(float.MaxValue), hi = new(float.MinValue);
            foreach (Vector3 p in list) { lo = Vector3.Min(lo, p); hi = Vector3.Max(hi, p); }
            if ((hi - lo).Length() < SmallestPart || hi.Y < 0f) continue;
            groups.Add(list);
        }
        // Past the limit, the two nearest hulls become one.
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
        return groups.Select(Reduce).ToArray();
    }

    /// <summary>A part's points thickened a little and cut to the extreme few: the hull stays the part's own shape.</summary>
    static Vector3[] Reduce(List<Vector3> points)
    {
        var fat = new List<Vector3>(points.Count * 2);
        foreach (Vector3 p in points.Distinct())
            for (int c = 0; c < 8; c++)
                fat.Add(p + new Vector3((c & 1) == 0 ? -Thickness : Thickness, (c & 2) == 0 ? -Thickness : Thickness, (c & 4) == 0 ? -Thickness : Thickness) * 0.5f);
        var keep = new List<Vector3>();
        foreach (Vector3 d in Directions)
        {
            Vector3 best = fat[0];
            float top = float.MinValue;
            foreach (Vector3 p in fat)
            {
                float s = Vector3.Dot(p, d);
                if (s > top) { top = s; best = p; }
            }
            if (!keep.Contains(best)) keep.Add(best);
        }
        return keep.ToArray();
    }
}
