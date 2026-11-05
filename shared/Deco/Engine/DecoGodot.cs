using System;
using System.Collections.Generic;
using GalHack.Items;
using Godot;
using NVector3 = System.Numerics.Vector3;

namespace GalHack.Deco.Engine;

/// <summary>
/// The six deco materials. Colour lives in the vertices, so every piece in a place shares them.
/// Glow is unshaded and pushed past 1 so the environment's glow catches it; a per-instance
/// <c>energy</c> lets one screen flicker while the others hold. Leaf is double-sided for vines.
/// </summary>
public sealed class DecoMaterials
{
    public readonly Material[] BySurface;

    const string GlowShader = @"
shader_type spatial;
render_mode unshaded, cull_back;
instance uniform float energy = 1.0;
uniform float base_energy = 2.0;
void fragment() {
    vec3 c = COLOR.rgb;
    c = c * c;
    ALBEDO = c * base_energy * energy;
}";

    const string FleshShader = @"
shader_type spatial;
render_mode cull_back;
void fragment() {
    vec3 c = COLOR.rgb * COLOR.rgb;
    ALBEDO = c;
    EMISSION = c * 0.35;
    ROUGHNESS = 0.35;
    SPECULAR = 0.7;
}";

    static DecoMaterials _shared;
    public static DecoMaterials Shared => _shared ??= new DecoMaterials();

    public DecoMaterials(float glowEnergy = 2.0f)
    {
        var matte = new StandardMaterial3D { VertexColorUseAsAlbedo = true, VertexColorIsSrgb = true, Roughness = 0.9f };
        var metal = new StandardMaterial3D { VertexColorUseAsAlbedo = true, VertexColorIsSrgb = true, Roughness = 0.5f, Metallic = 0.45f };
        var glow = new ShaderMaterial { Shader = new Shader { Code = GlowShader } };
        glow.SetShaderParameter("base_energy", glowEnergy);
        var glass = new StandardMaterial3D
        {
            VertexColorUseAsAlbedo = true, VertexColorIsSrgb = true,
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha, Roughness = 0.08f, Metallic = 0.2f,
            CullMode = BaseMaterial3D.CullModeEnum.Disabled,
        };
        var flesh = new ShaderMaterial { Shader = new Shader { Code = FleshShader } };
        var leaf = new StandardMaterial3D { VertexColorUseAsAlbedo = true, VertexColorIsSrgb = true, Roughness = 0.85f, CullMode = BaseMaterial3D.CullModeEnum.Disabled };
        BySurface = new Material[] { matte, metal, glow, glass, flesh, leaf };
    }
}

/// <summary>Turns plain deco meshes into Godot meshes and collision shapes.</summary>
public static class DecoGodot
{
    public static Vector3 V(NVector3 v) => new(v.X, v.Y, v.Z);
    public static Color C(DecoRgb c) => new(c.R, c.G, c.B);

    /// <summary>An ArrayMesh with one surface per deco surface that has triangles.</summary>
    public static ArrayMesh ToMesh(DecoMeshData data, DecoMaterials materials = null)
    {
        materials ??= DecoMaterials.Shared;
        var mesh = new ArrayMesh();
        for (int s = 0; s < data.Surfaces.Length; s++)
        {
            DecoSurfaceData buf = data.Surfaces[s];
            int n = buf.Positions.Count;
            if (n == 0) continue;
            var verts = new Vector3[n];
            var normals = new Vector3[n];
            var colors = new Color[n];
            // Godot's front faces are clockwise: swap the second and third vertex of each triangle.
            for (int i = 0; i < n; i += 3)
            {
                for (int k = 0; k < 3; k++)
                {
                    int src = i + (k == 0 ? 0 : k == 1 ? 2 : 1);
                    verts[i + k] = V(buf.Positions[src]);
                    normals[i + k] = V(buf.Normals[src]);
                    var c = buf.Colors[src];
                    colors[i + k] = new Color(c.X, c.Y, c.Z, c.W);
                }
            }
            var arrays = new Godot.Collections.Array();
            arrays.Resize((int)Mesh.ArrayType.Max);
            arrays[(int)Mesh.ArrayType.Vertex] = verts;
            arrays[(int)Mesh.ArrayType.Normal] = normals;
            arrays[(int)Mesh.ArrayType.Color] = colors;
            mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays);
            mesh.SurfaceSetMaterial(mesh.GetSurfaceCount() - 1, materials.BySurface[s]);
        }
        return mesh;
    }

    /// <summary>The piece's collision boxes as shapes with their transforms (in the piece's frame).</summary>
    public static List<(BoxShape3D Shape, Transform3D At)> ToShapes(DecoMeshData data)
    {
        var list = new List<(BoxShape3D, Transform3D)>();
        foreach (DecoBox b in data.Boxes)
        {
            var basis = new Basis(V(b.X), V(b.Y), V(b.Z));
            list.Add((new BoxShape3D { Size = V(b.Half) * 2 }, new Transform3D(basis, V(b.Centre))));
        }
        return list;
    }
}

/// <summary>
/// Godot meshes for deco looks, made once each and shared by every placement that uses them.
/// </summary>
public sealed class DecoMeshCache
{
    public readonly DecoForge Forge;
    public readonly DecoMaterials Materials;
    readonly Dictionary<DecoLook, ArrayMesh> _meshes = new();
    readonly Dictionary<DecoLook, List<(BoxShape3D, Transform3D)>> _shapes = new();

    public DecoMeshCache(DecoForge forge, DecoMaterials materials = null)
    {
        Forge = forge;
        Materials = materials ?? DecoMaterials.Shared;
    }

    public int Count => _meshes.Count;

    public ArrayMesh Mesh(DecoLook look)
    {
        if (!_meshes.TryGetValue(look, out ArrayMesh m)) _meshes[look] = m = DecoGodot.ToMesh(Forge.Mesh(look), Materials);
        return m;
    }

    public List<(BoxShape3D Shape, Transform3D At)> Shapes(DecoLook look)
    {
        if (!_shapes.TryGetValue(look, out var s)) _shapes[look] = s = DecoGodot.ToShapes(Forge.Mesh(look));
        return s;
    }

    public void Clear()
    {
        _meshes.Clear();
        _shapes.Clear();
    }
}
