using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

namespace GalHack.Structures;

public enum Coverage : byte { None, Partial, Full }

/// <summary>A flat face of a shape, in cell coordinates from -0.5 to 0.5, wound counter-clockwise seen from outside.</summary>
public sealed class Polygon
{
    public Vector3[] Points;
    public Vector3 Normal;

    /// <summary>The cell face this polygon lies on, or null if it is inside the cell (a slope's ramp).</summary>
    public Dir? Boundary;

    /// <summary>For curved parts: the axis of the cylinder this polygon belongs to, so it can be smooth-shaded.</summary>
    public int SmoothAxis = -1;

    public float Area()
    {
        Vector3 sum = Vector3.Zero;
        for (int i = 1; i + 1 < Points.Length; i++)
            sum += Vector3.Cross(Points[i] - Points[0], Points[i + 1] - Points[0]);
        return sum.Length() * 0.5f;
    }
}

/// <summary>
/// A block shape. Everything gameplay needs (volume, which faces are full, the mirror symmetry)
/// is worked out from the geometry, so a new shape in shapes.json is just a list of points.
/// </summary>
public sealed class ShapeDef
{
    public string Id, Name;
    public int Index;
    public IReadOnlyList<Polygon> Polygons;
    public Vector3[] HullPoints;
    public float Volume;
    public readonly Coverage[] Faces = new Coverage[6];

    /// <summary>A reflection that maps the shape onto itself (see <see cref="Orientations.Mirror"/>).</summary>
    public Mat3 MirrorSymmetry = Mat3.Mirror(0);

    public bool IsCube => Volume > 0.999f;
    public Coverage Face(Dir local) => Faces[(int)local];

    public static ShapeDef FromHull(string id, string name, IEnumerable<Vector3> points)
    {
        List<Polygon> polygons = Geometry.ConvexHull(points.ToList());
        var shape = new ShapeDef { Id = id, Name = name, Polygons = polygons };
        shape.HullPoints = polygons.SelectMany(p => p.Points).Distinct(new NearVector()).ToArray();
        shape.Volume = Geometry.Volume(polygons);
        foreach (Dir d in Dirs.All)
        {
            float area = polygons.Where(p => p.Boundary == d).Sum(p => p.Area());
            shape.Faces[(int)d] = area > 0.999f ? Coverage.Full : area > 1e-4f ? Coverage.Partial : Coverage.None;
        }
        shape.MirrorSymmetry = Geometry.FindMirrorSymmetry(shape.HullPoints) ?? Mat3.Mirror(0);
        return shape;
    }

    sealed class NearVector : IEqualityComparer<Vector3>
    {
        public bool Equals(Vector3 a, Vector3 b) => Vector3.DistanceSquared(a, b) < 1e-8f;
        public int GetHashCode(Vector3 v) => HashCode.Combine(MathF.Round(v.X * 1000), MathF.Round(v.Y * 1000), MathF.Round(v.Z * 1000));
    }
}

/// <summary>Small geometry helpers for shapes and component parts. All in cell units.</summary>
public static class Geometry
{
    const float Eps = 1e-4f;

    /// <summary>
    /// The convex hull of a handful of points, as polygons. Brute force (every triple of points
    /// proposes a plane) is plenty for shapes with at most a few dozen points, built once at load.
    /// </summary>
    public static List<Polygon> ConvexHull(List<Vector3> input)
    {
        var points = new List<Vector3>();
        foreach (Vector3 p in input)
            if (!points.Any(q => Vector3.DistanceSquared(p, q) < 1e-8f)) points.Add(p);

        var planes = new List<(Vector3 n, float d)>();
        for (int i = 0; i < points.Count; i++)
            for (int j = i + 1; j < points.Count; j++)
                for (int k = j + 1; k < points.Count; k++)
                {
                    Vector3 n = Vector3.Cross(points[j] - points[i], points[k] - points[i]);
                    if (n.LengthSquared() < 1e-10f) continue;
                    n = Vector3.Normalize(n);
                    float d = Vector3.Dot(n, points[i]);
                    bool allBelow = true, allAbove = true;
                    foreach (Vector3 p in points)
                    {
                        float s = Vector3.Dot(n, p) - d;
                        if (s > Eps) allBelow = false;
                        if (s < -Eps) allAbove = false;
                    }
                    if (!allBelow && !allAbove) continue;
                    if (!allBelow) { n = -n; d = -d; }
                    if (!planes.Any(pl => Vector3.Dot(pl.n, n) > 1 - 1e-5f && MathF.Abs(pl.d - d) < Eps))
                        planes.Add((n, d));
                }

        var polygons = new List<Polygon>();
        foreach (var (n, d) in planes)
        {
            List<Vector3> onPlane = points.Where(p => MathF.Abs(Vector3.Dot(n, p) - d) < Eps).ToList();
            if (onPlane.Count < 3) continue;
            polygons.Add(MakePolygon(SortAround(onPlane, n), n));
        }
        return polygons;
    }

    /// <summary>Orders coplanar points counter-clockwise around the plane normal.</summary>
    static List<Vector3> SortAround(List<Vector3> points, Vector3 n)
    {
        Vector3 c = Vector3.Zero;
        foreach (Vector3 p in points) c += p;
        c /= points.Count;
        Vector3 u = Vector3.Normalize(MathF.Abs(n.X) < 0.9f ? Vector3.Cross(n, Vector3.UnitX) : Vector3.Cross(n, Vector3.UnitY));
        Vector3 v = Vector3.Cross(n, u);
        return points.OrderBy(p => MathF.Atan2(Vector3.Dot(p - c, v), Vector3.Dot(p - c, u))).ToList();
    }

    static Polygon MakePolygon(List<Vector3> points, Vector3 n)
    {
        var polygon = new Polygon { Points = points.ToArray(), Normal = n };
        Dir slot = Dirs.Closest(n);
        Vector3 axis = slot.Normal();
        if (Vector3.Dot(axis, n) > 1 - 1e-4f && points.All(p => MathF.Abs(Vector3.Dot(p, axis) - 0.5f) < 0.02f))
            polygon.Boundary = slot;
        return polygon;
    }

    /// <summary>Volume by the divergence theorem over fan triangles.</summary>
    public static float Volume(IEnumerable<Polygon> polygons)
    {
        float v = 0;
        foreach (Polygon p in polygons)
            for (int i = 1; i + 1 < p.Points.Length; i++)
                v += Vector3.Dot(p.Points[0], Vector3.Cross(p.Points[i], p.Points[i + 1]));
        return MathF.Abs(v / 6f);
    }

    public static Mat3? FindMirrorSymmetry(Vector3[] points)
    {
        foreach (Mat3 m in Mat3.AllSignedPermutations())
        {
            if (m.Det != -1) continue;
            bool maps = points.All(p =>
            {
                Vector3 q = m.Apply(p);
                return points.Any(r => Vector3.DistanceSquared(q, r) < 1e-6f);
            });
            if (maps) return m;
        }
        return null;
    }

    public static IEnumerable<Vector3> BoxPoints(Vector3 min, Vector3 max)
    {
        for (int i = 0; i < 8; i++)
            yield return new Vector3((i & 1) == 0 ? min.X : max.X, (i & 2) == 0 ? min.Y : max.Y, (i & 4) == 0 ? min.Z : max.Z);
    }

    /// <summary>Points of a cylinder along an axis, filling the box across the other two axes.</summary>
    public static IEnumerable<Vector3> CylinderPoints(int axis, Vector3 min, Vector3 max, int segments)
    {
        int a = (axis + 1) % 3, b = (axis + 2) % 3;
        Vector3 c = (min + max) * 0.5f;
        float ra = (Get(max, a) - Get(min, a)) * 0.5f, rb = (Get(max, b) - Get(min, b)) * 0.5f;
        for (int i = 0; i < segments; i++)
        {
            float t = MathF.Tau * i / segments;
            foreach (float end in new[] { Get(min, axis), Get(max, axis) })
            {
                Vector3 p = c;
                p = Set(p, axis, end);
                p = Set(p, a, Get(c, a) + MathF.Cos(t) * ra);
                p = Set(p, b, Get(c, b) + MathF.Sin(t) * rb);
                yield return p;
            }
        }
    }

    /// <summary>A cylinder's polygons, with its sides marked for smooth shading.</summary>
    public static List<Polygon> Cylinder(int axis, Vector3 min, Vector3 max, int segments)
    {
        List<Polygon> polygons = ConvexHull(CylinderPoints(axis, min, max, segments).ToList());
        foreach (Polygon p in polygons)
            if (MathF.Abs(Get(p.Normal, axis)) < 0.5f) p.SmoothAxis = axis;
        return polygons;
    }

    public static float Get(Vector3 v, int axis) => axis switch { 0 => v.X, 1 => v.Y, _ => v.Z };

    public static Vector3 Set(Vector3 v, int axis, float value)
    {
        if (axis == 0) v.X = value;
        else if (axis == 1) v.Y = value;
        else v.Z = value;
        return v;
    }
}
