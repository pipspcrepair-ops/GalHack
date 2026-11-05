using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

namespace GalHack.Deco;

/// <summary>Flat-shaded triangles for one surface: three vertices each, with a face normal and an sRGB colour (alpha for glass).</summary>
public sealed class DecoSurfaceData
{
    public readonly List<Vector3> Positions = new();
    public readonly List<Vector3> Normals = new();
    public readonly List<Vector4> Colors = new();
    public int Triangles => Positions.Count / 3;
}

/// <summary>An oriented collision box: centre, three unit axes and half-extents along them.</summary>
public readonly record struct DecoBox(Vector3 Centre, Vector3 X, Vector3 Y, Vector3 Z, Vector3 Half)
{
    public float Volume => 8 * Half.X * Half.Y * Half.Z;

    public IEnumerable<Vector3> Corners()
    {
        for (int i = 0; i < 8; i++)
            yield return Centre + X * ((i & 1) != 0 ? Half.X : -Half.X) + Y * ((i & 2) != 0 ? Half.Y : -Half.Y) + Z * ((i & 4) != 0 ? Half.Z : -Half.Z);
    }
}

/// <summary>
/// A finished deco mesh in the piece's frame: triangles per surface, collision boxes, where it
/// shines from, bounds and centre of mass. Plain C#; the engine adapter turns it into an
/// ArrayMesh and a body.
/// </summary>
public sealed class DecoMeshData
{
    public readonly DecoSurfaceData[] Surfaces = Enumerable.Range(0, 6).Select(_ => new DecoSurfaceData()).ToArray();
    public readonly List<DecoBox> Boxes = new();
    public readonly List<(Vector3 At, DecoRgb Color)> Lights = new();
    public Vector3? Spark;
    public Vector3 Min = new(float.MaxValue), Max = new(float.MinValue);
    public Vector3 CentreOfMass;
    /// <summary>Glows stay lit without power (Precursor and alien pieces).</summary>
    public bool SelfLit;

    public int Triangles => Surfaces.Sum(s => s.Triangles);
    public Vector3 Size => Max - Min;

    public void AddTriangle(DecoSurface surface, Vector3 a, Vector3 b, Vector3 c, DecoRgb col, float alpha = 1f)
    {
        Vector3 n = Vector3.Cross(b - a, c - a);
        float len = n.Length();
        if (len < 1e-10f) return;
        n /= len;
        DecoSurfaceData s = Surfaces[(int)surface];
        var color = new Vector4(col.R, col.G, col.B, alpha);
        s.Positions.Add(a); s.Positions.Add(b); s.Positions.Add(c);
        s.Normals.Add(n); s.Normals.Add(n); s.Normals.Add(n);
        s.Colors.Add(color); s.Colors.Add(color); s.Colors.Add(color);
        Min = Vector3.Min(Min, Vector3.Min(a, Vector3.Min(b, c)));
        Max = Vector3.Max(Max, Vector3.Max(a, Vector3.Max(b, c)));
    }

    /// <summary>Every triangle on every surface, for ray casts (vines feel their way over the piece).</summary>
    public IEnumerable<(Vector3 A, Vector3 B, Vector3 C)> AllTriangles()
    {
        foreach (DecoSurfaceData s in Surfaces)
            for (int i = 0; i < s.Positions.Count; i += 3)
                yield return (s.Positions[i], s.Positions[i + 1], s.Positions[i + 2]);
    }

    /// <summary>Pulls every vertex inside a box (a damaged piece must stay in its footprint).</summary>
    public void Clamp(Vector3 min, Vector3 max)
    {
        foreach (DecoSurfaceData s in Surfaces)
            for (int i = 0; i < s.Positions.Count; i++) s.Positions[i] = Vector3.Clamp(s.Positions[i], min, max);
        RecomputeBounds();
    }

    public void RecomputeBounds()
    {
        Min = new Vector3(float.MaxValue);
        Max = new Vector3(float.MinValue);
        foreach (DecoSurfaceData s in Surfaces)
            foreach (Vector3 p in s.Positions) { Min = Vector3.Min(Min, p); Max = Vector3.Max(Max, p); }
    }
}

/// <summary>Turns parts into triangles. Faces are wound counter-clockwise seen from outside (normal = (b-a)×(c-a)).</summary>
public static class DecoEmit
{
    static readonly int[][] HexFaces =
    {
        new[] { 0, 2, 6, 4 }, new[] { 1, 5, 7, 3 }, new[] { 0, 4, 5, 1 },
        new[] { 2, 3, 7, 6 }, new[] { 0, 1, 3, 2 }, new[] { 4, 6, 7, 5 },
    };

    /// <summary>A part's corner and surface vertices in the piece frame (for settling and bounds).</summary>
    public static List<Vector3> Vertices(DecoPart p)
    {
        var list = new List<Vector3>();
        switch (p.Kind)
        {
            case DecoPrim.Hex:
                foreach (Vector3 c in p.Corners) list.Add(Vector3.Transform(c, p.M));
                break;
            case DecoPrim.Cylinder:
                for (int i = 0; i < p.Segments; i++)
                {
                    float a = i * MathF.Tau / p.Segments + MathF.PI / p.Segments;
                    list.Add(Vector3.Transform(new Vector3(MathF.Cos(a) * 0.5f, -0.5f, MathF.Sin(a) * 0.5f), p.M));
                    list.Add(Vector3.Transform(new Vector3(MathF.Cos(a) * 0.5f * p.Taper, 0.5f, MathF.Sin(a) * 0.5f * p.Taper), p.M));
                }
                break;
            case DecoPrim.Dome:
                foreach (Vector3 v in DomeVerts(p.Segments)) list.Add(Vector3.Transform(v, p.M));
                list.Add(Vector3.Transform(Vector3.Zero, p.M));
                break;
            default:
                foreach (Vector3 v in SphereVerts(p.Segments)) list.Add(Vector3.Transform(v, p.M));
                break;
        }
        return list;
    }

    /// <summary>A hemisphere of radius 0.5 standing on y = 0, apex up: rings from the apex to the rim.</summary>
    static IEnumerable<Vector3> DomeVerts(int seg)
    {
        int rings = Math.Max(2, seg / 4);
        for (int r = 0; r <= rings; r++)
        {
            float phi = MathF.PI * 0.5f * r / rings;
            for (int i = 0; i < seg; i++)
            {
                float th = i * MathF.Tau / seg;
                yield return new Vector3(MathF.Sin(phi) * MathF.Cos(th) * 0.5f, MathF.Cos(phi) * 0.5f, MathF.Sin(phi) * MathF.Sin(th) * 0.5f);
            }
        }
    }

    static IEnumerable<Vector3> SphereVerts(int seg)
    {
        int rings = Math.Max(2, seg / 2);
        for (int r = 0; r <= rings; r++)
        {
            float phi = MathF.PI * r / rings;
            for (int i = 0; i < seg; i++)
            {
                float th = i * MathF.Tau / seg;
                yield return new Vector3(MathF.Sin(phi) * MathF.Cos(th) * 0.5f, MathF.Cos(phi) * 0.5f, MathF.Sin(phi) * MathF.Sin(th) * 0.5f);
            }
        }
    }

    /// <summary>Emits one part's triangles, coloured by <paramref name="color"/> (given each triangle's centre, normal and slot).</summary>
    public static void Part(DecoMeshData mesh, DecoPart p, Func<Vector3, Vector3, DecoSlot, DecoRgb> color, Func<DecoSlot, DecoSurface> surfaceOf, Func<Vector3, Vector3> warp = null)
    {
        DecoSurface surface = surfaceOf(p.Slot);
        float alpha = surface == DecoSurface.Glass ? 0.42f : 1f;
        var tris = new List<(Vector3, Vector3, Vector3)>();
        switch (p.Kind)
        {
            case DecoPrim.Hex:
            {
                var c = new Vector3[8];
                for (int i = 0; i < 8; i++) c[i] = Vector3.Transform(p.Corners[i], p.M);
                foreach (int[] f in HexFaces)
                {
                    tris.Add((c[f[0]], c[f[1]], c[f[2]]));
                    tris.Add((c[f[0]], c[f[2]], c[f[3]]));
                }
                break;
            }
            case DecoPrim.Cylinder:
            {
                int n = p.Segments;
                var bot = new Vector3[n];
                var top = new Vector3[n];
                for (int i = 0; i < n; i++)
                {
                    float a = i * MathF.Tau / n + MathF.PI / n;
                    bot[i] = Vector3.Transform(new Vector3(MathF.Cos(a) * 0.5f, -0.5f, MathF.Sin(a) * 0.5f), p.M);
                    top[i] = Vector3.Transform(new Vector3(MathF.Cos(a) * 0.5f * p.Taper, 0.5f, MathF.Sin(a) * 0.5f * p.Taper), p.M);
                }
                Vector3 cb = Vector3.Transform(new Vector3(0, -0.5f, 0), p.M), ct = Vector3.Transform(new Vector3(0, 0.5f, 0), p.M);
                for (int i = 0; i < n; i++)
                {
                    int j = (i + 1) % n;
                    tris.Add((bot[i], bot[j], top[j]));
                    tris.Add((bot[i], top[j], top[i]));
                    tris.Add((cb, bot[j], bot[i]));
                    if (p.Taper > 0.001f) tris.Add((ct, top[i], top[j]));
                }
                break;
            }
            case DecoPrim.Dome:
            {
                int seg = p.Segments, rings = Math.Max(2, seg / 4);
                var v = DomeVerts(seg).Select(x => Vector3.Transform(x, p.M)).ToArray();
                Vector3 baseC = Vector3.Transform(Vector3.Zero, p.M);
                for (int r = 0; r < rings; r++)
                    for (int i = 0; i < seg; i++)
                    {
                        int j = (i + 1) % seg;
                        tris.Add((v[r * seg + i], v[r * seg + j], v[(r + 1) * seg + j]));
                        tris.Add((v[r * seg + i], v[(r + 1) * seg + j], v[(r + 1) * seg + i]));
                    }
                for (int i = 0; i < seg; i++) tris.Add((baseC, v[rings * seg + (i + 1) % seg], v[rings * seg + i]));
                break;
            }
            default:
            {
                int seg = p.Segments, rings = Math.Max(2, seg / 2);
                var v = SphereVerts(seg).Select(x => Vector3.Transform(x, p.M)).ToArray();
                for (int r = 0; r < rings; r++)
                    for (int i = 0; i < seg; i++)
                    {
                        int j = (i + 1) % seg;
                        Vector3 a = v[r * seg + i], b = v[r * seg + j], c2 = v[(r + 1) * seg + j], d = v[(r + 1) * seg + i];
                        tris.Add((a, b, c2));
                        tris.Add((a, c2, d));
                    }
                break;
            }
        }
        Vector3 centre = Vector3.Zero;
        foreach (var (a, b, c) in tris) centre += a + b + c;
        centre /= tris.Count * 3;
        foreach (var (a0, b0, c0) in tris)
        {
            Vector3 a = a0, b = b0, c = c0;
            if (warp != null) { a = warp(a); b = warp(b); c = warp(c); }
            Vector3 n = Vector3.Cross(b - a, c - a);
            if (n.LengthSquared() < 1e-14f) continue;
            Vector3 mid = (a + b + c) / 3f;
            if (Vector3.Dot(n, mid - centre) < 0) (b, c) = (c, b);
            n = Vector3.Normalize(Vector3.Cross(b - a, c - a));
            mesh.AddTriangle(surface, a, b, c, color(mid, n, p.Slot), alpha);
        }
    }

    /// <summary>The oriented box around a part, or null if it is too small to collide with.</summary>
    public static DecoBox? BoxOf(DecoPart p)
    {
        if (p.Is(PartFlags.NoCollide)) return null;
        if (p.Kind == DecoPrim.Dome) p = new DecoPart { Kind = DecoPrim.Hex, Corners = DecoPart.UnitCube, M = Matrix4x4.CreateScale(1, 0.5f, 1) * DecoGeo.Move(0, 0.25f, 0) * p.M };
        Vector3 ax = new(p.M.M11, p.M.M12, p.M.M13), ay = new(p.M.M21, p.M.M22, p.M.M23), az = new(p.M.M31, p.M.M32, p.M.M33);
        float lx = ax.Length(), ly = ay.Length(), lz = az.Length();
        if (MathF.Max(lx, MathF.Max(ly, lz)) < 0.06f || lx * ly * lz < 1e-5f) return null;
        Vector3 nx = ax / lx, ny = ay / ly, nz = az / lz;
        bool orthogonal = MathF.Abs(Vector3.Dot(nx, ny)) < 1e-3f && MathF.Abs(Vector3.Dot(ny, nz)) < 1e-3f && MathF.Abs(Vector3.Dot(nx, nz)) < 1e-3f;
        Vector3 centre = Vector3.Transform(Vector3.Zero, p.M);
        if (orthogonal) return new DecoBox(centre, nx, ny, nz, new Vector3(lx, ly, lz) * 0.5f);
        // Sheared (a bent frame): an axis-aligned box around it.
        Vector3 min = new(float.MaxValue), max = new(float.MinValue);
        foreach (Vector3 c in DecoPart.UnitCube) { Vector3 w = Vector3.Transform(c, p.M); min = Vector3.Min(min, w); max = Vector3.Max(max, w); }
        return new DecoBox((min + max) / 2, Vector3.UnitX, Vector3.UnitY, Vector3.UnitZ, (max - min) / 2);
    }

    /// <summary>A handful of boxes for a whole piece: the biggest parts, at most <paramref name="max"/>.</summary>
    public static void Collision(DecoMeshData mesh, IEnumerable<DecoPart> parts, int max = 12)
    {
        var boxes = parts.Select(BoxOf).Where(b => b != null).Select(b => b.Value).OrderByDescending(b => b.Volume).ToList();
        var kept = new List<DecoBox>();
        var rest = new List<DecoBox>();
        foreach (DecoBox b in boxes)
        {
            if (kept.Any(k => Contains(k, b))) continue;
            if (kept.Count < max - 1) kept.Add(b); else rest.Add(b);
        }
        // Many small parts (a sandbag wall, a rack of bottles): the rest share one box around them all.
        if (rest.Count == 1) kept.Add(rest[0]);
        else if (rest.Count > 1)
        {
            Vector3 lo = new(float.MaxValue), hi = new(float.MinValue);
            foreach (DecoBox b in rest) foreach (Vector3 c in b.Corners()) { lo = Vector3.Min(lo, c); hi = Vector3.Max(hi, c); }
            kept.Add(new DecoBox((lo + hi) / 2, Vector3.UnitX, Vector3.UnitY, Vector3.UnitZ, (hi - lo) / 2));
        }
        if (kept.Count == 0)
            kept.Add(new DecoBox((mesh.Min + mesh.Max) / 2, Vector3.UnitX, Vector3.UnitY, Vector3.UnitZ, Vector3.Max((mesh.Max - mesh.Min) / 2, new Vector3(0.02f))));
        mesh.Boxes.AddRange(kept);
    }

    static bool Contains(DecoBox outer, DecoBox inner)
    {
        foreach (Vector3 c in inner.Corners())
        {
            Vector3 d = c - outer.Centre;
            if (MathF.Abs(Vector3.Dot(d, outer.X)) > outer.Half.X + 1e-3f || MathF.Abs(Vector3.Dot(d, outer.Y)) > outer.Half.Y + 1e-3f || MathF.Abs(Vector3.Dot(d, outer.Z)) > outer.Half.Z + 1e-3f)
                return false;
        }
        return true;
    }

    /// <summary>Volume-weighted centre of the parts.</summary>
    public static Vector3 CentreOfMass(IEnumerable<DecoPart> parts)
    {
        Vector3 sum = Vector3.Zero;
        float total = 0;
        foreach (DecoPart p in parts)
        {
            float v = MathF.Max(p.Volume, 1e-6f);
            sum += p.Centre * v;
            total += v;
        }
        return total > 0 ? sum / total : Vector3.Zero;
    }
}
