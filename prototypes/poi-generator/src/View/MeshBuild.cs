using Godot;
using PoiGenerator.Gen;
using Material = Godot.Material;

namespace PoiGenerator;

/// <summary>Turns the generator's plain vertex arrays into Godot meshes and collision shapes.</summary>
public static class MeshBuild
{
    public static ArrayMesh ToMesh(ChunkMesh chunk, Material[] materials)
    {
        var mesh = new ArrayMesh();
        for (int s = 0; s < chunk.Surfaces.Length; s++)
        {
            var buf = chunk.Surfaces[s];
            if (buf.Indices.Count == 0) continue;
            int n = buf.VertexCount;
            var verts = new Vector3[n];
            var normals = new Vector3[n];
            var colors = new Color[n];
            for (int i = 0; i < n; i++)
            {
                verts[i] = new Vector3(buf.Positions[i * 3], buf.Positions[i * 3 + 1], buf.Positions[i * 3 + 2]);
                normals[i] = new Vector3(buf.Normals[i * 3], buf.Normals[i * 3 + 1], buf.Normals[i * 3 + 2]);
                colors[i] = new Color(buf.Colors[i * 4], buf.Colors[i * 4 + 1], buf.Colors[i * 4 + 2], buf.Colors[i * 4 + 3]);
            }
            var arrays = new Godot.Collections.Array();
            arrays.Resize((int)Mesh.ArrayType.Max);
            arrays[(int)Mesh.ArrayType.Vertex] = verts;
            arrays[(int)Mesh.ArrayType.Normal] = normals;
            arrays[(int)Mesh.ArrayType.Color] = colors;
            arrays[(int)Mesh.ArrayType.Index] = buf.Indices.ToArray();
            mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays);
            mesh.SurfaceSetMaterial(mesh.GetSurfaceCount() - 1, materials[s]);
        }
        return mesh;
    }

    public static ConcavePolygonShape3D ToShape(ChunkMesh chunk)
    {
        var l = chunk.Collision;
        var faces = new Vector3[l.Count / 3];
        for (int i = 0; i < faces.Length; i++) faces[i] = new Vector3(l[i * 3], l[i * 3 + 1], l[i * 3 + 2]);
        // Two-sided, so a body can't slip through a face whatever its winding.
        return new ConcavePolygonShape3D { Data = faces, BackfaceCollision = true };
    }
}

/// <summary>
/// The five block materials. Colour lives in the vertices, so a whole place is five
/// materials. Glow is an unshaded shader that pushes colour past 1 so the environment's glow
/// picks it up; its energy uniform is what flickers or pulses per state. Flesh is the Hollow
/// Bloom: lit like anything else, wet, and giving off a little light of its own, so it reads
/// as sick pink even in the dark.
/// </summary>
public sealed class BlockMaterials
{
    public readonly Material[] BySurface;
    public readonly ShaderMaterial Glow;

    const string GlowShader = @"
shader_type spatial;
render_mode unshaded, cull_back;
uniform float energy = 2.2;
void fragment() {
    vec3 c = COLOR.rgb;
    c = c * c; // the vertex colours are sRGB; near enough to linear for a glow
    ALBEDO = c * energy;
}";

    const string FleshShader = @"
shader_type spatial;
render_mode cull_back;
uniform float glow = 0.4;
void fragment() {
    vec3 c = COLOR.rgb * COLOR.rgb;
    ALBEDO = c;
    EMISSION = c * glow;
    ROUGHNESS = 0.35;
    SPECULAR = 0.7;
}";

    public BlockMaterials(float glowEnergy = 2.2f)
    {
        var matte = new StandardMaterial3D
        {
            VertexColorUseAsAlbedo = true,
            VertexColorIsSrgb = true,
            Roughness = 0.92f,
        };
        var metal = new StandardMaterial3D
        {
            VertexColorUseAsAlbedo = true,
            VertexColorIsSrgb = true,
            Roughness = 0.55f,
            Metallic = 0.35f,
        };
        Glow = new ShaderMaterial { Shader = new Shader { Code = GlowShader } };
        Glow.SetShaderParameter("energy", glowEnergy);
        var glass = new StandardMaterial3D
        {
            VertexColorUseAsAlbedo = true,
            VertexColorIsSrgb = true,
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
            Roughness = 0.08f,
            Metallic = 0.2f,
            CullMode = BaseMaterial3D.CullModeEnum.Disabled,
        };
        var flesh = new ShaderMaterial { Shader = new Shader { Code = FleshShader } };
        BySurface = new Material[] { matte, metal, Glow, glass, flesh };
    }
}
