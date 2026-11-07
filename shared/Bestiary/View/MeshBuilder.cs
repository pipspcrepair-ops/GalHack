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

    /// <summary>
    /// Drops the shared meshes on the main thread (at shutdown), rather than leaving their C#
    /// wrappers to the finalizer thread. Meshes still on screen stay alive in the engine.
    /// </summary>
    public static void ReleaseCache()
    {
        var meshes = new List<ArrayMesh>();
        foreach (var (_, mesh) in (IEnumerable<KeyValuePair<Piece, ArrayMesh>>)Cache) meshes.Add(mesh);
        Cache.Clear();
        foreach (ArrayMesh mesh in meshes) mesh.Dispose();
    }

    public void Add(Prim p)
    {
        // Vertex colours go in linear: the shader treats COLOR as linear albedo.
        Color c = p.Color.SrgbToLinear();
        c.A = Math.Clamp(p.Glow, 0f, 1f);
        int start = _v.Count;
        if (p.Shape == Shape.Loft)
        {
            Loft(p, c);
            return;
        }
        switch (p.Shape)
        {
            case Shape.Sphere: Sphere(p.Size * 0.5f, p.Sides > 0 ? p.Sides : 10, 7); break;
            case Shape.Box: Box(p.Size * 0.5f); break;
            case Shape.Capsule: Capsule(p.Size.X * 0.5f, p.Size.Y, p.Sides > 0 ? p.Sides : 8); break;
            case Shape.Cylinder: Cylinder(p.Size.X * 0.5f, p.Size.Z * 0.5f, p.Size.Y, p.Sides > 0 ? p.Sides : 10); break;
            case Shape.Cone: Cylinder(p.Size.X * 0.5f, 0f, p.Size.Y, p.Sides > 0 ? p.Sides : 8); break;
            case Shape.Taper: Taper(p.Size.X * 0.5f, p.Size.Z * 0.5f, p.Size.Y, p.Sides > 0 ? p.Sides : 10); break;
        }
        Basis nb = p.Local.Basis;
        for (int i = start; i < _v.Count; i++)
        {
            _v[i] = p.Local * _v[i];
            _n[i] = (nb * _n[i]).Normalized();
            _c.Add(c);
            _uv.Add(new Vector2(p.Pattern, p.Material));
        }
    }

    /// <summary>
    /// Every piece in one mesh, each vertex bound whole to its piece's bone: one draw call for a
    /// whole body, posed by a skeleton instead of a node per piece.
    /// </summary>
    public static ArrayMesh Skinned(IReadOnlyList<Piece> pieces) => Skinning(pieces).Commit();

    /// <summary>The vertex work of <see cref="Skinned"/>, with no engine calls: safe off the main thread. <see cref="Prepared.Commit"/> on the main thread is quick.</summary>
    public static Prepared Skinning(IReadOnlyList<Piece> pieces)
    {
        var b = new MeshBuilder();
        var bones = new List<int>();
        for (int i = 0; i < pieces.Count; i++)
        {
            foreach (Prim prim in pieces[i].Prims) b.Add(prim);
            while (bones.Count < b._v.Count) bones.Add(i);
        }
        return b.Prepare(bones);
    }

    /// <summary>A plain piece's vertex work, off the main thread.</summary>
    public static Prepared Plain(Piece piece)
    {
        var b = new MeshBuilder();
        foreach (Prim prim in piece.Prims) b.Add(prim);
        return b.Prepare(null);
    }

    /// <summary>A mesh's arrays, built and wound, waiting for the main thread to hand them to the engine.</summary>
    public sealed class Prepared
    {
        public Vector3[] V, N;
        public Color[] C;
        public Vector2[] Uv;
        public int[] I, Bones;
        public float[] Weights;

        public ArrayMesh Commit()
        {
            var mesh = new ArrayMesh();
            if (V.Length == 0) return mesh;
            var arrays = new Godot.Collections.Array();
            arrays.Resize((int)Mesh.ArrayType.Max);
            arrays[(int)Mesh.ArrayType.Vertex] = V;
            arrays[(int)Mesh.ArrayType.Normal] = N;
            arrays[(int)Mesh.ArrayType.Color] = C;
            arrays[(int)Mesh.ArrayType.TexUV] = Uv;
            arrays[(int)Mesh.ArrayType.Index] = I;
            if (Bones != null)
            {
                arrays[(int)Mesh.ArrayType.Bones] = Bones;
                arrays[(int)Mesh.ArrayType.Weights] = Weights;
            }
            mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays);
            return mesh;
        }
    }

    Prepared Prepare(List<int> bones)
    {
        for (int i = 0; i + 2 < _i.Count; i += 3) (_i[i + 1], _i[i + 2]) = (_i[i + 2], _i[i + 1]);
        var p = new Prepared { V = _v.ToArray(), N = _n.ToArray(), C = _c.ToArray(), Uv = _uv.ToArray(), I = _i.ToArray() };
        if (bones != null)
        {
            p.Bones = new int[_v.Count * 4];
            p.Weights = new float[_v.Count * 4];
            for (int i = 0; i < _v.Count; i++) { p.Bones[i * 4] = bones[i]; p.Weights[i * 4] = 1f; }
        }
        return p;
    }

    public ArrayMesh Commit(List<int> bones = null)
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
        if (bones != null)
        {
            var b = new int[_v.Count * 4];
            var w = new float[_v.Count * 4];
            for (int i = 0; i < _v.Count; i++) { b[i * 4] = bones[i]; w[i * 4] = 1f; }
            arrays[(int)Mesh.ArrayType.Bones] = b;
            arrays[(int)Mesh.ArrayType.Weights] = w;
        }
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

    /// <summary>
    /// A sphere-swept cone along Y: a sphere of radius r0 at the origin and one of radius r1 at
    /// len, joined where a cone touches both (at latitude asin((r0 - r1) / len)). Normals are
    /// the spheres' own, so they run on smoothly across the join and into the next taper.
    /// </summary>
    void Taper(float r0, float r1, float len, int segs)
    {
        int start = _v.Count;
        float sa = Math.Clamp((r0 - r1) / MathF.Max(len, 1e-4f), -0.92f, 0.92f);
        float a = MathF.Asin(sa);
        const int cap = 4;
        int rows = 0;
        // Top sphere from its pole down to the tangent latitude, then the bottom sphere on down.
        for (int k = 0; k <= cap * 2 + 1; k++)
        {
            bool top = k <= cap;
            float lat = top
                ? MathF.PI * 0.5f - (MathF.PI * 0.5f - a) * k / cap
                : a - (a + MathF.PI * 0.5f) * (k - cap - 1) / cap;
            float r = top ? r1 : r0, cy = top ? len : 0f;
            float cl = MathF.Cos(lat), sl = MathF.Sin(lat);
            for (int x = 0; x <= segs; x++)
            {
                float theta = x / (float)segs * MathF.Tau;
                var n = new Vector3(cl * MathF.Cos(theta), sl, cl * MathF.Sin(theta));
                _v.Add(new Vector3(n.X * r, cy + n.Y * r, n.Z * r));
                _n.Add(n);
            }
            rows++;
        }
        Grid(start, segs, rows - 1);
    }

    /// <summary>
    /// A loft: an elliptical ring at each station along Z, blended from the top colour to the
    /// underside colour round the ring. Normals come from the surface itself; the closed ends
    /// (zero-radius stations) face straight along the axis.
    /// </summary>
    void Loft(Prim p, Color top)
    {
        LoftProfile f = p.Loft;
        int segs = p.Sides > 0 ? p.Sides : 14, n = f.Count, start = _v.Count;
        Color under = p.Color2.SrgbToLinear();
        under.A = top.A;
        var local = new Vector3[n * (segs + 1)];
        for (int i = 0; i < n; i++)
        for (int x = 0; x <= segs; x++)
        {
            float theta = x / (float)segs * MathF.Tau;
            local[i * (segs + 1) + x] = new Vector3(f.W[i] * MathF.Cos(theta), f.Y[i] + f.H[i] * MathF.Sin(theta), f.Z[i]);
        }
        Basis nb = p.Local.Basis;
        for (int i = 0; i < n; i++)
        for (int x = 0; x <= segs; x++)
        {
            int xm = x == 0 ? segs - 1 : x - 1, xp = x == segs ? 1 : x + 1;
            int im = Math.Max(0, i - 1), ip = Math.Min(n - 1, i + 1);
            Vector3 du = local[i * (segs + 1) + xp] - local[i * (segs + 1) + xm];
            Vector3 dv = local[ip * (segs + 1) + x] - local[im * (segs + 1) + x];
            Vector3 normal = du.Cross(dv);
            if (normal.LengthSquared() < 1e-12f || f.W[i] < 1e-4f) normal = i < n / 2 ? Vector3.Back * -1f : Vector3.Back;
            float s = MathF.Sin(x / (float)segs * MathF.Tau);
            float belly = Math.Clamp((0.15f - s) / 0.75f, 0f, 1f);
            belly = belly * belly * (3f - 2f * belly);
            _v.Add(p.Local * local[i * (segs + 1) + x]);
            _n.Add((nb * normal).Normalized());
            _c.Add(top.Lerp(under, belly));
            _uv.Add(new Vector2(p.Pattern * (1f - 0.85f * belly), p.Material));
        }
        // Rings run front to back and round each ring, the same orientation as the sphere grid.
        Grid(start, segs, n - 1);
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
