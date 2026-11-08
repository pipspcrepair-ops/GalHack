using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using GalHack.Items;

namespace GalHack.Deco;

/// <summary>
/// Flat-shaded triangles for one surface: three vertices each, with a face normal and an sRGB
/// colour. The colour's alpha carries the part's finish and wear (<see cref="DecoFinishes.Code"/>),
/// or how clear a pane is on glass. <see cref="Faces"/> places each vertex on its face, in metres,
/// for the shader's grain, seams and bevels.
/// </summary>
public sealed class DecoSurfaceData
{
    public readonly List<Vector3> Positions = new();
    public readonly List<Vector3> Normals = new();
    public readonly List<Vector4> Colors = new();
    /// <summary>
    /// Per vertex: where it lies on its face (u, v in metres) and the face's extent (Lu, Lv): a
    /// rectangle when both are positive; a negative extent wraps round that way with no edge (a
    /// cylinder's side, a ball); Lv zero with Lu negative is a disc of radius -Lu centred on (0, 0);
    /// both zero means no face (the shader works from the position). May run short of the positions
    /// (a mesh merged from others); the missing ones are no face.
    /// </summary>
    public readonly List<Vector4> Faces = new();
    public int Triangles => Positions.Count / 3;

    /// <summary>The face data for vertex <paramref name="i"/> (none if it was never given).</summary>
    public Vector4 FaceAt(int i) => i < Faces.Count ? Faces[i] : default;
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
    /// <summary>How worn the piece's surfaces are drawn, 0 (new) to 3 (weathered): see <see cref="DecoFinishes.WearOf"/>.</summary>
    public int Wear;

    public int Triangles => Surfaces.Sum(s => s.Triangles);
    public Vector3 Size => Max - Min;

    /// <summary>A triangle with no face of its own (marks, leaves, blades): <paramref name="alpha"/> as given (1 draws it plain).</summary>
    public void AddTriangle(DecoSurface surface, Vector3 a, Vector3 b, Vector3 c, DecoRgb col, float alpha = 1f) =>
        AddTriangle(surface, a, b, c, col, alpha, default, default, default);

    /// <summary>A triangle and where its corners lie on their face (see <see cref="DecoSurfaceData.Faces"/>).</summary>
    public void AddTriangle(DecoSurface surface, Vector3 a, Vector3 b, Vector3 c, DecoRgb col, float alpha, Vector4 fa, Vector4 fb, Vector4 fc)
    {
        Vector3 n = Vector3.Cross(b - a, c - a);
        float len = n.Length();
        if (len < 1e-10f) return;
        n /= len;
        DecoSurfaceData s = Surfaces[(int)surface];
        var color = new Vector4(col.R, col.G, col.B, alpha);
        // Faces stay in step with the positions even after triangles merged in without them.
        while (s.Faces.Count < s.Positions.Count) s.Faces.Add(default);
        s.Positions.Add(a); s.Positions.Add(b); s.Positions.Add(c);
        s.Normals.Add(n); s.Normals.Add(n); s.Normals.Add(n);
        s.Colors.Add(color); s.Colors.Add(color); s.Colors.Add(color);
        s.Faces.Add(fa); s.Faces.Add(fb); s.Faces.Add(fc);
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

    /// <summary>A triangle of a part and where its corners lie on their face (see <see cref="DecoSurfaceData.Faces"/>).</summary>
    readonly record struct FaceTri(Vector3 A, Vector3 B, Vector3 C, Vector2 Ua, Vector2 Ub, Vector2 Uc, Vector2 L, bool End);

    /// <summary>
    /// Emits one part's triangles, coloured by <paramref name="color"/> (given each triangle's centre,
    /// normal and slot), each vertex placed on its face and the part's finish and the mesh's wear
    /// carried for the shader.
    /// </summary>
    public static void Part(DecoMeshData mesh, DecoPart p, Func<Vector3, Vector3, DecoSlot, DecoRgb> color, Func<DecoSlot, DecoSurface> surfaceOf, Func<Vector3, Vector3> warp = null)
    {
        DecoSurface surface = surfaceOf(p.Slot);
        DecoFinish finish = DecoFinishes.Of(p);
        var tris = new List<FaceTri>();
        float lx = new Vector3(p.M.M11, p.M.M12, p.M.M13).Length(), ly = new Vector3(p.M.M21, p.M.M22, p.M.M23).Length(), lz = new Vector3(p.M.M31, p.M.M32, p.M.M33).Length();
        switch (p.Kind)
        {
            case DecoPrim.Hex:
            {
                var c = new Vector3[8];
                for (int i = 0; i < 8; i++) c[i] = Vector3.Transform(p.Corners[i], p.M);
                float longest = MathF.Max(lx, MathF.Max(ly, lz));
                for (int fi = 0; fi < 6; fi++)
                {
                    int[] f = HexFaces[fi];
                    Vector3 q0 = c[f[0]], q1 = c[f[1]], q2 = c[f[2]], q3 = c[f[3]];
                    float l1 = (Vector3.Distance(q0, q1) + Vector3.Distance(q3, q2)) / 2, l2 = (Vector3.Distance(q0, q3) + Vector3.Distance(q1, q2)) / 2;
                    // u runs along the face's longer side (a board's grain runs along it).
                    bool swap = l2 > l1;
                    float lu = swap ? l2 : l1, lv = swap ? l1 : l2;
                    Vector2 At(float s, float t) => swap ? new Vector2(t * lu, s * lv) : new Vector2(s * lu, t * lv);
                    // An end face: across the part's long axis (a board's sawn end).
                    float across = (fi / 2) switch { 0 => lx, 1 => ly, _ => lz };
                    bool end = across >= longest - 1e-5f && across > 1.3f * lu;
                    var L = new Vector2(MathF.Max(lu, 1e-4f), MathF.Max(lv, 1e-4f));
                    tris.Add(new FaceTri(q0, q1, q2, At(0, 0), At(1, 0), At(1, 1), L, end));
                    tris.Add(new FaceTri(q0, q2, q3, At(0, 0), At(1, 1), At(0, 1), L, end));
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
                // The side, unrolled: round it and along it, u along the longer of the two; the ends are discs.
                float r = (lx + lz) / 4, around = MathF.PI * 2 * r * (1 + p.Taper) / 2, along = MathF.Sqrt(ly * ly + r * r * (1 - p.Taper) * (1 - p.Taper));
                bool roundU = around > along;
                Vector2 Side(float s, float t) => roundU ? new Vector2(s * around, t * along) : new Vector2(t * along, s * around);
                var side = roundU ? new Vector2(-around, along) : new Vector2(along, -around);
                bool end = ly > 2.6f * r;
                Vector2 Rim(int i, float k)
                {
                    float a = i * MathF.Tau / n + MathF.PI / n;
                    return new Vector2(MathF.Cos(a), MathF.Sin(a)) * (r * k);
                }
                for (int i = 0; i < n; i++)
                {
                    int j = (i + 1) % n;
                    float s0 = (float)i / n, s1 = (float)(i + 1) / n;
                    tris.Add(new FaceTri(bot[i], bot[j], top[j], Side(s0, 0), Side(s1, 0), Side(s1, 1), side, false));
                    tris.Add(new FaceTri(bot[i], top[j], top[i], Side(s0, 0), Side(s1, 1), Side(s0, 1), side, false));
                    tris.Add(new FaceTri(cb, bot[j], bot[i], Vector2.Zero, Rim(j, 1), Rim(i, 1), new Vector2(-r, 0), end));
                    if (p.Taper > 0.001f) tris.Add(new FaceTri(ct, top[i], top[j], Vector2.Zero, Rim(i, p.Taper), Rim(j, p.Taper), new Vector2(-r * p.Taper, 0), end));
                }
                break;
            }
            case DecoPrim.Dome:
            {
                int seg = p.Segments, rings = Math.Max(2, seg / 4);
                var v = DomeVerts(seg).Select(x => Vector3.Transform(x, p.M)).ToArray();
                Vector3 baseC = Vector3.Transform(Vector3.Zero, p.M);
                float r = (lx + lz) / 4, around = MathF.Tau * r, meridian = MathF.PI * 0.5f * (r + ly / 2) / 2;
                Vector2 On(int ring, float i) => new(ring * meridian / rings, i / seg * around);
                var shell = new Vector2(-meridian, -around);
                for (int k = 0; k < rings; k++)
                    for (int i = 0; i < seg; i++)
                    {
                        int j = (i + 1) % seg;
                        tris.Add(new FaceTri(v[k * seg + i], v[k * seg + j], v[(k + 1) * seg + j], On(k, i), On(k, i + 1), On(k + 1, i + 1), shell, false));
                        tris.Add(new FaceTri(v[k * seg + i], v[(k + 1) * seg + j], v[(k + 1) * seg + i], On(k, i), On(k + 1, i + 1), On(k + 1, i), shell, false));
                    }
                for (int i = 0; i < seg; i++)
                {
                    float a0 = i * MathF.Tau / seg, a1 = (i + 1) * MathF.Tau / seg;
                    tris.Add(new FaceTri(baseC, v[rings * seg + (i + 1) % seg], v[rings * seg + i], Vector2.Zero, new Vector2(MathF.Cos(a1), MathF.Sin(a1)) * r, new Vector2(MathF.Cos(a0), MathF.Sin(a0)) * r, new Vector2(-r, 0), false));
                }
                break;
            }
            default:
            {
                int seg = p.Segments, rings = Math.Max(2, seg / 2);
                var v = SphereVerts(seg).Select(x => Vector3.Transform(x, p.M)).ToArray();
                float r = (lx + lz) / 4, around = MathF.Tau * r, meridian = MathF.PI * (r + ly / 2) / 2;
                Vector2 On(int ring, float i) => new(ring * meridian / rings, i / seg * around);
                var shell = new Vector2(-meridian, -around);
                for (int k = 0; k < rings; k++)
                    for (int i = 0; i < seg; i++)
                    {
                        int j = (i + 1) % seg;
                        Vector3 a = v[k * seg + i], b = v[k * seg + j], c2 = v[(k + 1) * seg + j], d = v[(k + 1) * seg + i];
                        tris.Add(new FaceTri(a, b, c2, On(k, i), On(k, i + 1), On(k + 1, i + 1), shell, false));
                        tris.Add(new FaceTri(a, c2, d, On(k, i), On(k + 1, i + 1), On(k + 1, i), shell, false));
                    }
                break;
            }
        }
        Vector3 centre = Vector3.Zero;
        foreach (FaceTri t in tris) centre += t.A + t.B + t.C;
        centre /= tris.Count * 3;
        int wear = mesh.Wear;
        foreach (FaceTri t in tris)
        {
            Vector3 a = t.A, b = t.B, c = t.C;
            Vector2 ua = t.Ua, ub = t.Ub, uc = t.Uc;
            if (warp != null) { a = warp(a); b = warp(b); c = warp(c); }
            Vector3 n = Vector3.Cross(b - a, c - a);
            if (n.LengthSquared() < 1e-14f) continue;
            Vector3 mid = (a + b + c) / 3f;
            if (Vector3.Dot(n, mid - centre) < 0) { (b, c) = (c, b); (ub, uc) = (uc, ub); }
            n = Vector3.Normalize(Vector3.Cross(b - a, c - a));
            // The finish's flag: a board's end grain, or a tyre's tread.
            bool flag = finish == DecoFinish.Rubber ? p.Is(PartFlags.Tyre) : t.End;
            float alpha = surface == DecoSurface.Glass ? DecoFinishes.GlassAlpha(wear) : DecoFinishes.Code(finish, wear, flag);
            mesh.AddTriangle(surface, a, b, c, color(mid, n, p.Slot), alpha, new Vector4(ua, t.L.X, t.L.Y), new Vector4(ub, t.L.X, t.L.Y), new Vector4(uc, t.L.X, t.L.Y));
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

/// <summary>
/// What each part is made of and how worn it is, as the deco shader reads them: the rule for a
/// part with no finish of its own, and how a piece's state and age wear its surfaces.
/// </summary>
public static class DecoFinishes
{
    /// <summary>
    /// What a part is made of: its model's word, else its flags (a screen, a tyre, a pane), else its
    /// slot; hay, food and crops are organic whatever their colour, and cushions and cloth painted in
    /// a body or plastic colour are fabric.
    /// </summary>
    public static DecoFinish Of(DecoPart p)
    {
        if (p.Finish != DecoFinish.Auto) return p.Finish;
        if (p.Is(PartFlags.Screen)) return DecoFinish.Screen;
        if (p.Is(PartFlags.Tyre)) return DecoFinish.Rubber;
        DecoFinish f = DecoPalette.FinishOf(p.Slot);
        if (p.Is(PartFlags.Pane) && f is not (DecoFinish.Glass or DecoFinish.Screen)) return DecoFinish.Glass;
        // Hay, food and crops, whatever colour they are painted.
        if (p.Is(PartFlags.Perishable) && f is not (DecoFinish.Paper or DecoFinish.Organic)) return DecoFinish.Organic;
        if (p.Is(PartFlags.Soft) && f is DecoFinish.Paint or DecoFinish.Plastic) return DecoFinish.Fabric;
        return f;
    }

    /// <summary>
    /// How worn a look's surfaces are: 0 new (in use and whole), 1 used (knocked over, sealed under
    /// dust), 2 old (a century indoors, broken, overgrown), 3 weathered (out in the weather, rusted, burnt).
    /// </summary>
    public static int WearOf(DecoState state, DecoAge age, bool exposed, bool outdoor)
    {
        int w = state switch
        {
            DecoState.Burnt or DecoState.Rusted => 3,
            DecoState.Broken or DecoState.Overgrown or DecoState.Buried => 2,
            DecoState.Open => outdoor ? 2 : 1,
            DecoState.Knocked => 1,
            _ => 0,
        };
        if (age == DecoAge.Preserved) w = Math.Max(w, 1);
        if (age == DecoAge.OldWorld) w = Math.Max(w, exposed || outdoor ? 3 : 2);
        return w;
    }

    /// <summary>
    /// The finish, wear and flag (a board's end grain, a tyre's tread) packed into the colour's alpha
    /// for the opaque materials. Coded values stay below 128, so an alpha of 1 (a triangle drawn by
    /// other code) reads as plain.
    /// </summary>
    public static float Code(DecoFinish f, int wear, bool flag) => (((int)f & 15) | (Math.Clamp(wear, 0, 3) << 4) | (flag ? 64 : 0)) / 255f;

    /// <summary>Glass carries only its wear, in how clear it is: a dirtier pane is less see-through.</summary>
    public static float GlassAlpha(int wear) => 0.36f + 0.04f * Math.Clamp(wear, 0, 3);

    /// <summary>Unpacks a colour alpha made by <see cref="Code"/> (null if it isn't one).</summary>
    public static (DecoFinish Finish, int Wear, bool Flag)? Decode(float alpha)
    {
        int code = (int)MathF.Round(alpha * 255f);
        if (code >= 128) return null;
        return ((DecoFinish)(code & 15), (code >> 4) & 3, (code & 64) != 0);
    }
}
