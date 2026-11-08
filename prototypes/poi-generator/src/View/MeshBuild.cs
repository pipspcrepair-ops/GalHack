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

    /// <summary>
    /// The still water standing in a place's low rooms (<see cref="PoiInstance.Water"/>): a flat sheet at each pool's
    /// level over the floor cells it covers, in the place's own cells; null where nothing is flooded.
    /// </summary>
    public static MeshInstance3D Flooding(PoiInstance inst)
    {
        if (inst.Water.Count == 0) return null;
        var st = new SurfaceTool();
        st.Begin(Mesh.PrimitiveType.Triangles);
        st.SetNormal(Vector3.Up);
        st.SetColor(new Color(1, 1, 1, 1)); // the river shader reads its alpha as the shore: all of it is water
        foreach (WaterPool pool in inst.Water)
            foreach (Int3 c in pool.Cells)
            {
                Vector3 a = new(c.X, pool.Level, c.Z), b = new(c.X + 1, pool.Level, c.Z), d = new(c.X + 1, pool.Level, c.Z + 1), e = new(c.X, pool.Level, c.Z + 1);
                st.AddVertex(a); st.AddVertex(b); st.AddVertex(d);
                st.AddVertex(a); st.AddVertex(d); st.AddVertex(e);
            }
        st.Index();
        return new MeshInstance3D { Name = "Flooding", Mesh = st.Commit(), MaterialOverride = StillWater(), CastShadow = GeometryInstance3D.ShadowCastingSetting.Off };
    }

    /// <summary>
    /// Standing water indoors: the game's water shader (where the project has it) in the dark green of still,
    /// stale water, glassy enough to hold the room in it; elsewhere a plain glossy dark surface.
    /// </summary>
    public static Material StillWater()
    {
        const string path = "res://shaders/water.gdshader";
        Color shallow = new(0.075f, 0.1f, 0.07f), deep = new(0.018f, 0.032f, 0.026f);
        if (ResourceLoader.Exists(path))
        {
            var m = new ShaderMaterial { Shader = GD.Load<Shader>(path) };
            m.SetShaderParameter("shallow", shallow);
            m.SetShaderParameter("deep", deep);
            m.SetShaderParameter("daylight", 0.4f);
            return m;
        }
        return new StandardMaterial3D { AlbedoColor = deep.Lerp(shallow, 0.5f), Roughness = 0.12f, Metallic = 0f, MetallicSpecular = 0.35f, CullMode = BaseMaterial3D.CullModeEnum.Disabled };
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
