using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Godot;

namespace Wildlife;

/// <summary>
/// Turns a piece's primitives into one low-poly mesh, with colours baked into vertex colours
/// (alpha = glow), pattern weight in UV.x, and smooth normals. A whole piece is one draw call
/// whatever it is made of, and every individual of a species shares the same meshes.
/// </summary>
public sealed class MeshBuilder
{
    readonly List<Vector3> _v = new();
    readonly List<Vector3> _n = new();
    readonly List<Color> _c = new();
    readonly List<Vector2> _uv = new();
    readonly List<int> _i = new();

    static readonly ConditionalWeakTable<Piece, ArrayMesh> Cache = new();

    /// <summary>The shared mesh for a piece, built on first use.</summary>
    public static ArrayMesh For(Piece piece) => Cache.GetValue(piece, p =>
    {
        var b = new MeshBuilder();
        foreach (Prim prim in p.Prims) b.Add(prim);
        return b.Commit();
    });

    public void Add(Prim p)
    {
        // Vertex colours go in linear: the shader treats COLOR as linear albedo.
        Color c = p.Color.SrgbToLinear();
        c.A = Math.Clamp(p.Glow, 0f, 1f);
        int start = _v.Count;
        switch (p.Shape)
        {
            case Shape.Sphere: Sphere(p.Size * 0.5f, p.Sides > 0 ? p.Sides : 10, 7); break;
            case Shape.Box: Box(p.Size * 0.5f); break;
            case Shape.Capsule: Capsule(p.Size.X * 0.5f, p.Size.Y, p.Sides > 0 ? p.Sides : 8); break;
            case Shape.Cylinder: Cylinder(p.Size.X * 0.5f, p.Size.Z * 0.5f, p.Size.Y, p.Sides > 0 ? p.Sides : 10); break;
            case Shape.Cone: Cylinder(p.Size.X * 0.5f, 0f, p.Size.Y, p.Sides > 0 ? p.Sides : 8); break;
        }
        Basis nb = p.Local.Basis;
        for (int i = start; i < _v.Count; i++)
        {
            _v[i] = p.Local * _v[i];
            _n[i] = (nb * _n[i]).Normalized();
            _c.Add(c);
            _uv.Add(new Vector2(p.Pattern, 0f));
        }
    }

    public ArrayMesh Commit()
    {
        var mesh = new ArrayMesh();
        if (_v.Count == 0) return mesh;
        // The shapes above are wound counter-clockwise seen from outside; Godot's front faces
        // are clockwise, so flip every triangle once here.
        for (int i = 0; i + 2 < _i.Count; i += 3) (_i[i + 1], _i[i + 2]) = (_i[i + 2], _i[i + 1]);
        var arrays = new Godot.Collections.Array();
        arrays.Resize((int)Mesh.ArrayType.Max);
        arrays[(int)Mesh.ArrayType.Vertex] = _v.ToArray();
        arrays[(int)Mesh.ArrayType.Normal] = _n.ToArray();
        arrays[(int)Mesh.ArrayType.Color] = _c.ToArray();
        arrays[(int)Mesh.ArrayType.TexUV] = _uv.ToArray();
        arrays[(int)Mesh.ArrayType.Index] = _i.ToArray();
        mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays);
        return mesh;
    }

    // ---- Shapes, in local space before the prim's transform ------------------------------

    /// <summary>An ellipsoid with radii r: normals from the ellipsoid, not the sphere.</summary>
    void Sphere(Vector3 r, int segs, int rings)
    {
        int start = _v.Count;
        for (int y = 0; y <= rings; y++)
        {
            float v = y / (float)rings, phi = v * MathF.PI;
            for (int x = 0; x <= segs; x++)
            {
                float u = x / (float)segs, theta = u * MathF.Tau;
                var n = new Vector3(MathF.Sin(phi) * MathF.Cos(theta), MathF.Cos(phi), MathF.Sin(phi) * MathF.Sin(theta));
                _v.Add(new Vector3(n.X * r.X, n.Y * r.Y, n.Z * r.Z));
                _n.Add(new Vector3(n.X / MathF.Max(r.X, 1e-4f), n.Y / MathF.Max(r.Y, 1e-4f), n.Z / MathF.Max(r.Z, 1e-4f)).Normalized());
            }
        }
        Grid(start, segs, rings);
    }

    /// <summary>A capsule along Y: total length len, two hemispheres of radius r.</summary>
    void Capsule(float r, float len, int segs)
    {
        int start = _v.Count;
        float half = MathF.Max(0f, len * 0.5f - r);
        const int cap = 3;
        int rows = 0;
        for (int y = 0; y <= cap * 2 + 1; y++)
        {
            // Rows 0..cap: top hemisphere; cap+1..2cap+1: bottom hemisphere.
            bool top = y <= cap;
            float phi = top ? y / (float)cap * MathF.PI * 0.5f : MathF.PI * 0.5f + (y - cap - 1) / (float)cap * MathF.PI * 0.5f;
            float cy = top ? half : -half;
            for (int x = 0; x <= segs; x++)
            {
                float theta = x / (float)segs * MathF.Tau;
                var n = new Vector3(MathF.Sin(phi) * MathF.Cos(theta), MathF.Cos(phi), MathF.Sin(phi) * MathF.Sin(theta));
                _v.Add(new Vector3(n.X * r, cy + n.Y * r, n.Z * r));
                _n.Add(n);
            }
            rows++;
        }
        Grid(start, segs, rows - 1);
    }

    /// <summary>A cylinder or cone along Y, centred, with caps.</summary>
    void Cylinder(float r0, float r1, float h, int segs)
    {
        int start = _v.Count;
        float slope = (r0 - r1) / MathF.Max(h, 1e-4f);
        for (int y = 0; y <= 1; y++)
        {
            float r = y == 0 ? r0 : r1, py = y == 0 ? -h * 0.5f : h * 0.5f;
            for (int x = 0; x <= segs; x++)
            {
                float theta = x / (float)segs * MathF.Tau;
                var dir = new Vector3(MathF.Cos(theta), 0f, MathF.Sin(theta));
                _v.Add(dir * r + Vector3.Up * py);
                _n.Add((dir + Vector3.Up * slope).Normalized());
            }
        }
        // Side, wound so the outside faces out.
        for (int x = 0; x < segs; x++)
        {
            int a = start + x, b = a + 1, c = start + segs + 1 + x, d = c + 1;
            _i.Add(a); _i.Add(c); _i.Add(b);
            _i.Add(b); _i.Add(c); _i.Add(d);
        }
        Cap(r0, -h * 0.5f, segs, false);
        if (r1 > 1e-4f) Cap(r1, h * 0.5f, segs, true);
    }

    void Cap(float r, float y, int segs, bool up)
    {
        int center = _v.Count;
        _v.Add(new Vector3(0f, y, 0f));
        _n.Add(up ? Vector3.Up : Vector3.Down);
        for (int x = 0; x <= segs; x++)
        {
            float theta = x / (float)segs * MathF.Tau;
            _v.Add(new Vector3(MathF.Cos(theta) * r, y, MathF.Sin(theta) * r));
            _n.Add(up ? Vector3.Up : Vector3.Down);
        }
        for (int x = 0; x < segs; x++)
        {
            int a = center + 1 + x, b = a + 1;
            if (up) { _i.Add(center); _i.Add(b); _i.Add(a); }
            else { _i.Add(center); _i.Add(a); _i.Add(b); }
        }
    }

    void Box(Vector3 h)
    {
        Vector3[] normals = { Vector3.Right, Vector3.Left, Vector3.Up, Vector3.Down, Vector3.Back, Vector3.Forward };
        foreach (Vector3 n in normals)
        {
            Vector3 u = MathF.Abs(n.Y) > 0.5f ? Vector3.Right : Vector3.Up;
            Vector3 v = n.Cross(u);
            int s = _v.Count;
            foreach (var (a, b) in new[] { (-1f, -1f), (1f, -1f), (1f, 1f), (-1f, 1f) })
            {
                Vector3 p = n + u * a + v * b;
                _v.Add(new Vector3(p.X * h.X, p.Y * h.Y, p.Z * h.Z));
                _n.Add(n);
            }
            _i.Add(s); _i.Add(s + 1); _i.Add(s + 2);
            _i.Add(s); _i.Add(s + 2); _i.Add(s + 3);
        }
    }

    /// <summary>Indices for a (segs+1) x (rows+1) vertex grid, outward facing.</summary>
    void Grid(int start, int segs, int rows)
    {
        for (int y = 0; y < rows; y++)
        for (int x = 0; x < segs; x++)
        {
            int a = start + y * (segs + 1) + x, b = a + 1, c = a + segs + 1, d = c + 1;
            _i.Add(a); _i.Add(b); _i.Add(c);
            _i.Add(b); _i.Add(d); _i.Add(c);
        }
    }
}
