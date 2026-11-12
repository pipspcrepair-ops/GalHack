using System;
using System.Collections.Generic;
using GalHack.Geometry;
using Godot;
using NV3 = System.Numerics.Vector3;
using NV4 = System.Numerics.Vector4;

namespace GalHack.Testing;

/// <summary>
/// The drawn scene as <see cref="Coplanar"/> faces: every visible mesh and multimesh under a node,
/// opaque surfaces only, in one frame centred on <c>origin</c> (so a place far out keeps its
/// millimetres). A surface drawn from both sides gives each side. A node out of its visibility range
/// from <c>eye</c> is left out, as the renderer leaves it out.
/// </summary>
public sealed class CoplanarScene
{
    public readonly List<Coplanar.Face> Faces = new();
    public readonly List<string> Sources = new();
    readonly Dictionary<string, int> _source = new();
    public int MaxInstances = 4000;

    /// <summary>What a node counts as when the worst sources are listed: its name and its parent's, digits dropped.</summary>
    public Func<Node, string> Label = DefaultLabel;

    public static string DefaultLabel(Node n)
    {
        static string Clean(string s)
        {
            var b = new System.Text.StringBuilder();
            foreach (char c in s) if (!char.IsDigit(c) && c != '@' && c != ',' && c != '(' && c != ')' && c != '-') b.Append(c);
            return b.ToString().Trim();
        }
        // Generated names (@Node3D@12) say nothing: the nearest two named ancestors do.
        var names = new List<string>();
        for (Node a = n; a != null && names.Count < 2; a = a.GetParent())
            if (!a.Name.ToString().StartsWith('@')) names.Insert(0, Clean(a.Name));
        return names.Count == 0 ? Clean(n.Name) : string.Join("/", names);
    }

    int Source(Node n)
    {
        string s = Label(n);
        if (!_source.TryGetValue(s, out int id)) { _source[s] = id = Sources.Count; Sources.Add(s); }
        return id;
    }

    float _radius = float.PositiveInfinity;

    public void Gather(Node root, Vector3 origin, Vector3? eye = null, float radius = float.PositiveInfinity)
    {
        _radius = radius;
        if (root is Node3D n3 && !n3.Visible) return;
        if (root is GeometryInstance3D g && eye is Vector3 e)
        {
            float far = (g.GlobalPosition - e).Length();
            if (g.VisibilityRangeEnd > 0f && far > g.VisibilityRangeEnd + g.VisibilityRangeEndMargin) return;
            if (g.VisibilityRangeBegin > 0f && far < g.VisibilityRangeBegin) return;
        }
        if (root is MeshInstance3D mi && mi.Mesh != null && Near(mi, origin, radius))
            for (int s = 0; s < mi.Mesh.GetSurfaceCount(); s++)
                Add(mi.Mesh, s, mi.GetActiveMaterial(s), mi.GlobalTransform, origin, Source(mi), null);
        else if (root is MultiMeshInstance3D mm && mm.Multimesh?.Mesh != null)
        {
            MultiMesh m = mm.Multimesh;
            int count = Math.Min(m.VisibleInstanceCount >= 0 ? m.VisibleInstanceCount : m.InstanceCount, MaxInstances);
            int src = Source(mm);
            for (int i = 0; i < count; i++)
            {
                Transform3D at = mm.GlobalTransform * m.GetInstanceTransform(i);
                if (radius < float.PositiveInfinity && (at.Origin - origin).Length() > radius) continue;
                Color? tint = m.UseColors ? m.GetInstanceColor(i) : null;
                for (int s = 0; s < m.Mesh.GetSurfaceCount(); s++)
                    Add(m.Mesh, s, mm.MaterialOverride ?? m.Mesh.SurfaceGetMaterial(s), at, origin, src, tint);
            }
        }
        foreach (Node c in root.GetChildren()) Gather(c, origin, eye, radius);
    }

    static bool Near(MeshInstance3D mi, Vector3 origin, float radius)
    {
        if (float.IsPositiveInfinity(radius)) return true;
        Aabb box = mi.GlobalTransform * mi.GetAabb();
        Vector3 closest = origin.Clamp(box.Position, box.End);
        return (closest - origin).Length() <= radius;
    }

    /// <summary>Whether a material lets what is behind it through (then it can't fight: it blends).</summary>
    public static bool SeeThrough(Material m) => m switch
    {
        BaseMaterial3D b => b.Transparency is BaseMaterial3D.TransparencyEnum.Alpha or BaseMaterial3D.TransparencyEnum.AlphaDepthPrePass or BaseMaterial3D.TransparencyEnum.AlphaHash
            || b.BlendMode != BaseMaterial3D.BlendModeEnum.Mix || b.NoDepthTest,
        ShaderMaterial s => s.Shader?.Code is string code && (code.Contains("ALPHA =") || code.Contains("ALPHA=") || code.Contains("blend_add") || code.Contains("depth_test_disabled") || code.Contains("unshaded") && code.Contains("ALPHA")),
        _ => false,
    };

    /// <summary>A far terrain ring sunk out of sight inside its hole round the near ground (its shader moves it; the arrays don't say).</summary>
    static bool Sunk(Material m) => m is ShaderMaterial s && s.GetShaderParameter("hole_sink") is var v && v.VariantType is Variant.Type.Float && v.AsSingle() > 0f;

    static bool TwoSided(Material m) => m switch
    {
        BaseMaterial3D b => b.CullMode == BaseMaterial3D.CullModeEnum.Disabled,
        ShaderMaterial s => s.Shader?.Code?.Contains("cull_disabled") == true,
        _ => false,
    };

    void Add(Mesh mesh, int s, Material mat, Transform3D at, Vector3 origin, int src, Color? tint)
    {
        if (mat != null && (SeeThrough(mat) || Sunk(mat))) return;
        if (mesh is ArrayMesh am && am.SurfaceGetPrimitiveType(s) != Mesh.PrimitiveType.Triangles) return;
        Godot.Collections.Array arrays = mesh.SurfaceGetArrays(s);
        if (arrays == null || arrays.Count <= (int)Mesh.ArrayType.Index) return;
        // A skinned body's arrays hold each piece in its own bone's frame, all laid over each other: BestiaryTest
        // checks bodies piece by piece instead.
        if (arrays[(int)Mesh.ArrayType.Bones].VariantType != Variant.Type.Nil) return;
        Vector3[] v = arrays[(int)Mesh.ArrayType.Vertex].AsVector3Array();
        if (v == null || v.Length < 3) return;
        Vector3[] nr = arrays[(int)Mesh.ArrayType.Normal].VariantType == Variant.Type.Nil ? null : arrays[(int)Mesh.ArrayType.Normal].AsVector3Array();
        Color[] col = arrays[(int)Mesh.ArrayType.Color].VariantType == Variant.Type.Nil ? null : arrays[(int)Mesh.ArrayType.Color].AsColorArray();
        int[] idx = arrays[(int)Mesh.ArrayType.Index].VariantType == Variant.Type.Nil ? null : arrays[(int)Mesh.ArrayType.Index].AsInt32Array();
        int matId = mat == null ? 0 : (int)(mat.GetInstanceId() & 0x7fffffff);
        bool both = mat != null && TwoSided(mat);
        Basis nb = at.Basis.Inverse().Transposed();
        int tris = idx != null ? idx.Length / 3 : v.Length / 3;
        for (int t = 0; t < tris; t++)
        {
            int i0 = idx != null ? idx[t * 3] : t * 3, i1 = idx != null ? idx[t * 3 + 1] : t * 3 + 1, i2 = idx != null ? idx[t * 3 + 2] : t * 3 + 2;
            NV3 a = N(at * v[i0] - origin), b = N(at * v[i1] - origin), c = N(at * v[i2] - origin);
            if (((a + b + c) / 3f).Length() > _radius) continue;
            // Godot's front faces wind clockwise: the face normal from the winding, or the vertex normal where one is given.
            NV3 front = nr != null ? N((nb * (nr[i0] + nr[i1] + nr[i2])).Normalized()) : -NV3.Cross(b - a, c - a);
            Color k = col != null ? col[i0] : Colors.White;
            if (tint is Color tc) k *= tc;
            int look = Coplanar.Look(matId, new NV4(k.R, k.G, k.B, k.A));
            Faces.Add(new Coplanar.Face(a, b, c, front, look, src));
            if (both) Faces.Add(new Coplanar.Face(a, b, c, -front, look, src));
        }
    }

    static NV3 N(Vector3 v) => new(v.X, v.Y, v.Z);
}
