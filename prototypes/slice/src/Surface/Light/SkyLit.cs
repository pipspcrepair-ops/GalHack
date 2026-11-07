using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Godot;

namespace Slice.Surface;

/// <summary>
/// Makes everything drawn in the world answer to <see cref="SkyField"/>. The sky's ambient is one
/// global value, so it reaches every surface whatever walls stand in the way; the one thing a
/// material can do about it is occlude it, and AO scales ambient and sky reflections and leaves
/// real lights alone. So every lit shader ends by setting AO from the field at that point, one
/// metre out from the surface. This is done to the shaders where they are used, not in each
/// prototype's copy: mounted files as they are packed (ResourceMount), the rest as their meshes
/// enter the tree. Plain StandardMaterial3Ds can't take a line of shader, so each becomes the
/// small equivalent below.
/// </summary>
public static class SkyLit
{
    const string Globals = @"
global uniform sampler3D sky_field : filter_linear, repeat_disable;
global uniform vec3 sky_field_min;
global uniform vec3 sky_field_size;
global uniform sampler2D sky_ground : filter_linear, repeat_disable;
global uniform vec4 sky_ground_rect;

// How open to the sky a point is. Inside the field, the field. Past it, the coarse ground map: below
// the land is dark, on or above it is open, so a long tunnel doesn't end in a lit wall.
float sky_open(vec3 p, vec3 n) {
	vec3 q = p + n;
	vec3 u = (q - sky_field_min) / sky_field_size;
	vec3 e = min(u, 1.0 - u) * sky_field_size;
	float inside = clamp(min(e.x, min(e.y, e.z)) / 4.0, 0.0, 1.0);
	float far_away = 1.0;
	if (inside < 1.0) {
		vec2 g = (q.xz - sky_ground_rect.xy) / sky_ground_rect.zw;
		if (g.x > 0.0 && g.x < 1.0 && g.y > 0.0 && g.y < 1.0) {
			float h = textureLod(sky_ground, g, 0.0).r;
			far_away = smoothstep(h - 10.0, h - 4.0, q.y);
		}
	}
	if (inside <= 0.0) return far_away;
	float near = textureLod(sky_field, vec3(u.x, u.z, u.y), 0.0).r;
	return mix(far_away, near * near, inside);
}

// The voxel ground stops streaming about 80 m out, so a long passage ends in a hole that looks out on
// whatever far land lies behind it. A sight line still under the ground at that edge is really in
// rock: what it reaches is drawn black, as the rock it can't really see past would be.
bool sky_walled(vec3 p, vec3 eye) {
	vec3 d = p - eye;
	float l = length(d);
	if (l < 62.0) return false;
	vec3 at = eye + d / l * 60.0;
	vec2 g = (at.xz - sky_ground_rect.xy) / sky_ground_rect.zw;
	return g.x > 0.0 && g.x < 1.0 && g.y > 0.0 && g.y < 1.0 && at.y < textureLod(sky_ground, g, 0.0).r - 4.0;
}
";

    const string Ending = @"
	{
		vec3 sky_at = (INV_VIEW_MATRIX * vec4(VERTEX, 1.0)).xyz;
		AO = min(AO, sky_open(sky_at, mat3(INV_VIEW_MATRIX) * NORMAL));
		if (sky_walled(sky_at, CAMERA_POSITION_WORLD)) {
			ALBEDO = vec3(0.0);
			EMISSION = vec3(0.0);
			SPECULAR = 0.0;
			METALLIC = 0.0;
		}
	}
";

    /// <summary>Where the field sits while there is none: everything then reads as open.</summary>
    public static readonly Vector3 Nowhere = new(1e7f, 1e7f, 1e7f);

    static readonly List<Resource> Keep = new();
    static bool _declared;

    /// <summary>The field's shader globals. Call before any shader compiles.</summary>
    public static void Declare()
    {
        if (_declared) return;
        _declared = true;
        var white = Image.CreateFromData(1, 1, false, Image.Format.R8, new byte[] { 255 });
        var field = new ImageTexture3D();
        field.Create(Image.Format.R8, 1, 1, 1, false, new Godot.Collections.Array<Image> { white });
        Keep.Add(field);
        var ground = ImageTexture.CreateFromImage(Image.CreateFromData(1, 1, false, Image.Format.Rf, new byte[4]));
        Keep.Add(ground);
        RenderingServer.GlobalShaderParameterAdd("sky_field", RenderingServer.GlobalShaderParameterType.Sampler3D, field.GetRid());
        RenderingServer.GlobalShaderParameterAdd("sky_field_min", RenderingServer.GlobalShaderParameterType.Vec3, Nowhere);
        RenderingServer.GlobalShaderParameterAdd("sky_field_size", RenderingServer.GlobalShaderParameterType.Vec3, new Vector3(SkyField.SizeX, SkyField.SizeY, SkyField.SizeZ));
        RenderingServer.GlobalShaderParameterAdd("sky_ground", RenderingServer.GlobalShaderParameterType.Sampler2D, ground.GetRid());
        RenderingServer.GlobalShaderParameterAdd("sky_ground_rect", RenderingServer.GlobalShaderParameterType.Vec4, new Vector4(Nowhere.X, Nowhere.Z, 1f, 1f));
    }

    // ---- Shaders --------------------------------------------------------------------------------

    /// <summary>A lit spatial shader's code with the field worked in; anything else unchanged.</summary>
    public static string Inject(string code)
    {
        if (!Wants(code)) return code;
        int body = code.IndexOf("void fragment()", System.StringComparison.Ordinal);
        int open = body < 0 ? -1 : code.IndexOf('{', body);
        if (open < 0) return code;
        int depth = 0, close = -1;
        for (int i = open; i < code.Length && close < 0; i++)
        {
            if (code[i] == '{') depth++;
            else if (code[i] == '}' && --depth == 0) close = i;
        }
        if (close < 0) return code;
        code = code.Insert(close, Ending);
        return code.Insert(AfterHeader(code), Globals);
    }

    static bool Wants(string code) =>
        code.Contains("shader_type spatial") && !code.Contains("sky_open(") && !RenderModes(code).Contains("unshaded");

    static string RenderModes(string code)
    {
        int at = code.IndexOf("render_mode", System.StringComparison.Ordinal);
        return at < 0 ? "" : code[at..code.IndexOf(';', at)];
    }

    static int AfterHeader(string code)
    {
        int at = code.IndexOf("render_mode", System.StringComparison.Ordinal);
        if (at < 0) at = code.IndexOf("shader_type", System.StringComparison.Ordinal);
        return code.IndexOf(';', at) + 1;
    }

    static readonly Dictionary<string, Shader> Shaders = new();

    /// <summary>Works the field into a shader material's shader, in place.</summary>
    public static void Patch(ShaderMaterial material)
    {
        Shader shader = material.Shader;
        if (shader == null || shader.GetMeta(Done, false).AsBool()) return;
        string code = shader.Code;
        if (!Wants(code))
        {
            shader.SetMeta(Done, true);
            return;
        }
        if (!Shaders.TryGetValue(code, out Shader patched))
        {
            patched = new Shader { Code = Inject(code) };
            patched.SetMeta(Done, true);
            Shaders[code] = patched;
        }
        material.Shader = patched;
    }

    static readonly StringName Done = "sky_lit";

    // ---- Plain materials ------------------------------------------------------------------------

    const string PlainShader = @"
shader_type spatial;
render_mode CULL, diffuse_burley, specular_schlick_ggx;
uniform vec4 albedo : source_color = vec4(1.0);
uniform sampler2D albedo_texture : source_color, hint_default_white;
uniform bool vertex_albedo = false;
uniform bool vertex_srgb = false;
uniform float roughness = 1.0;
uniform float metallic = 0.0;
uniform float specular = 0.5;
void fragment() {
	vec4 c = albedo * texture(albedo_texture, UV);
	if (vertex_albedo) c *= vec4(vertex_srgb ? pow(COLOR.rgb, vec3(2.2)) : COLOR.rgb, COLOR.a);
	ALBEDO = c.rgb;
	OPACITY
	ROUGHNESS = roughness;
	METALLIC = metallic;
	SPECULAR = specular;
}";

    static readonly Dictionary<(BaseMaterial3D.CullModeEnum, bool), Shader> PlainShaders = new();
    static readonly ConditionalWeakTable<Material, Material> Converted = new();

    static Shader PlainOf(BaseMaterial3D.CullModeEnum cull, bool alpha)
    {
        if (PlainShaders.TryGetValue((cull, alpha), out Shader s)) return s;
        string mode = cull switch { BaseMaterial3D.CullModeEnum.Disabled => "cull_disabled", BaseMaterial3D.CullModeEnum.Front => "cull_front", _ => "cull_back" };
        s = new Shader { Code = Inject(PlainShader.Replace("CULL", mode).Replace("OPACITY", alpha ? "ALPHA = c.a;" : "")) };
        s.SetMeta(Done, true);
        return PlainShaders[(cull, alpha)] = s;
    }

    /// <summary>
    /// Whether a plain material can be swapped: lit, in the world, nothing a shader line doesn't
    /// cover. Glowing ones and see-through fx are left alone: code animates those through their
    /// own StandardMaterial3D, and they shine or barely show in the dark anyway.
    /// </summary>
    static bool Swappable(BaseMaterial3D m) =>
        m.ShadingMode == BaseMaterial3D.ShadingModeEnum.PerPixel && m.BillboardMode == BaseMaterial3D.BillboardModeEnum.Disabled
        && !m.EmissionEnabled && !m.NormalEnabled && !m.NoDepthTest && m.DistanceFadeMode == BaseMaterial3D.DistanceFadeModeEnum.Disabled
        && (m.Transparency == BaseMaterial3D.TransparencyEnum.Disabled || m.VertexColorUseAsAlbedo);

    static Material Swap(BaseMaterial3D m)
    {
        if (Converted.TryGetValue(m, out Material done)) return done;
        Material made = m;
        if (Swappable(m))
        {
            var s = new ShaderMaterial { Shader = PlainOf(m.CullMode, m.Transparency != BaseMaterial3D.TransparencyEnum.Disabled) };
            s.SetShaderParameter("albedo", m.AlbedoColor);
            if (m.AlbedoTexture != null) s.SetShaderParameter("albedo_texture", m.AlbedoTexture);
            s.SetShaderParameter("vertex_albedo", m.VertexColorUseAsAlbedo);
            s.SetShaderParameter("vertex_srgb", m.VertexColorIsSrgb);
            s.SetShaderParameter("roughness", m.Roughness);
            s.SetShaderParameter("metallic", m.Metallic);
            s.SetShaderParameter("specular", m.MetallicSpecular);
            made = s;
        }
        Converted.AddOrUpdate(m, made);
        return made;
    }

    static Material Adapted(Material m)
    {
        if (m is ShaderMaterial s)
        {
            Patch(s);
            return s;
        }
        return m is BaseMaterial3D b ? Swap(b) : m;
    }

    // ---- The world as it comes in ---------------------------------------------------------------

    /// <summary>Every real light in the world, for <see cref="SkyLight.LightAt"/>. Sun and moon aren't in it.</summary>
    public static readonly HashSet<Light3D> Lights = new();

    static SceneTree _watching;

    /// <summary>Adapts what is in the tree now and whatever comes in after.</summary>
    public static void Watch(SceneTree tree)
    {
        Patch(GalHack.Structures.Presentation.BlockMaterials.Opaque);
        Patch(GalHack.Structures.Presentation.BlockMaterials.Glass);
        if (_watching == tree) return;
        _watching = tree;
        Lights.Clear();
        tree.NodeAdded += Adopt;
        tree.NodeRemoved += node =>
        {
            if (node is Light3D l) Lights.Remove(l);
        };
        Walk(tree.Root);
    }

    static void Walk(Node node)
    {
        Adopt(node);
        foreach (Node child in node.GetChildren()) Walk(child);
    }

    static void Adopt(Node node)
    {
        if (node is Light3D light)
        {
            if (light is not DirectionalLight3D && light.GetViewport() is not SubViewport) Lights.Add(light);
            return;
        }
        if (node is not GeometryInstance3D g || g.GetViewport() is SubViewport) return;
        if (g.GetParent() is DiegeticUi.Screens.WorldScreen) return; // its bezel lights up through its own material
        if (g.MaterialOverride != null)
        {
            g.MaterialOverride = Adapted(g.MaterialOverride);
            return;
        }
        switch (g)
        {
            case MeshInstance3D m when m.Mesh != null:
                for (int i = m.Mesh.GetSurfaceCount() - 1; i >= 0; i--)
                {
                    Material used = m.GetSurfaceOverrideMaterial(i) ?? m.Mesh.SurfaceGetMaterial(i);
                    if (used == null) continue;
                    Material adapted = Adapted(used);
                    if (adapted != used) m.SetSurfaceOverrideMaterial(i, adapted);
                }
                break;
            case MultiMeshInstance3D mm when mm.Multimesh?.Mesh is { } mesh:
                for (int i = mesh.GetSurfaceCount() - 1; i >= 0; i--)
                {
                    Material used = mesh.SurfaceGetMaterial(i);
                    if (used == null) continue;
                    Material adapted = Adapted(used);
                    if (adapted == used) continue;
                    if (mesh is ArrayMesh a) a.SurfaceSetMaterial(i, adapted);
                    else if (mesh is PrimitiveMesh p) p.Material = adapted;
                }
                break;
        }
    }
}
