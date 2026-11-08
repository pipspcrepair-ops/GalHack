using System.Collections.Generic;
using Godot;

namespace PlanetSurface.Fx;

/// <summary>
/// Dithered fades for anything drawn: one level of detail giving way to the next, or a thing coming
/// into view, without a hard swap. While it fades, a geometry draws through a copy of each of its
/// materials that keeps only part of a fixed screen pattern: the pixels whose pattern value falls in
/// its band (<see cref="Set"/>). Two drawings of one thing given bands that meet ([0, t) and [t, 1))
/// never both draw a pixel and never both leave one out, so the one dissolves into the other and
/// nothing doubles or shows through. Fades are short; once over, the geometry has its own materials
/// back, so nothing pays for the pattern while it stands still.
///
/// The copies: a shader material's own code with the band put in front of its fragment; a standard
/// material (the vertex-coloured ones places, props and creatures are drawn with) as the same shading
/// written out. A material with anything else (textures, normal maps) has no copy, and draws whole.
/// </summary>
public static class Fade
{
    const string Band = "fade_band";

    static readonly Dictionary<Material, Material> _copies = new();
    static readonly Dictionary<string, Shader> _shaders = new(); // by code: places make their own shaders of the same code
    static readonly Dictionary<ulong, (Material Override, Material[] Surfaces)> _own = new();

    /// <summary>
    /// Draws a geometry (and everything under a node) through the pattern between <paramref name="from"/>
    /// and <paramref name="to"/>, 0..1: (0, 1) is all of it, (0, 0) none, (0, t) the part a fade in
    /// shows at t and (t, 1) the part the drawing it replaces still shows.
    /// </summary>
    public static void Set(Node node, float from, float to)
    {
        if (node is GeometryInstance3D g) SetOne(g, from, to);
        foreach (Node child in node.GetChildren()) Set(child, from, to);
    }

    /// <summary>The fade is over: everything under the node has its own materials back, drawn whole.</summary>
    public static void Clear(Node node)
    {
        if (node is GeometryInstance3D g) ClearOne(g);
        foreach (Node child in node.GetChildren()) Clear(child);
    }

    static void SetOne(GeometryInstance3D g, float from, float to)
    {
        ulong id = g.GetInstanceId();
        if (!_own.ContainsKey(id))
        {
            Material[] surfaces = null;
            Material over = g.MaterialOverride;
            if (over != null) g.MaterialOverride = CopyOf(over) ?? over;
            else if (g is MeshInstance3D mi && mi.Mesh != null)
            {
                int n = mi.GetSurfaceOverrideMaterialCount();
                surfaces = new Material[n];
                for (int i = 0; i < n; i++)
                {
                    surfaces[i] = mi.GetSurfaceOverrideMaterial(i);
                    Material active = mi.GetActiveMaterial(i);
                    if (active != null && CopyOf(active) is Material copy) mi.SetSurfaceOverrideMaterial(i, copy);
                }
            }
            _own[id] = (over, surfaces);
        }
        g.SetInstanceShaderParameter(Band, new Vector2(from, to));
    }

    static void ClearOne(GeometryInstance3D g)
    {
        if (!_own.Remove(g.GetInstanceId(), out var own)) return;
        if (own.Override != null) g.MaterialOverride = own.Override;
        if (own.Surfaces != null && g is MeshInstance3D mi)
            for (int i = 0; i < own.Surfaces.Length && i < mi.GetSurfaceOverrideMaterialCount(); i++) mi.SetSurfaceOverrideMaterial(i, own.Surfaces[i]);
        g.SetInstanceShaderParameter(Band, new Vector2(0f, 1f));
    }

    /// <summary>A node is going away: forgets what it had (its materials go with it).</summary>
    public static void Forget(Node node)
    {
        if (node is GeometryInstance3D g) _own.Remove(g.GetInstanceId());
        foreach (Node child in node.GetChildren()) Forget(child);
    }

    /// <summary>Makes the copies of these materials now (while loading), so no first fade makes a shader in play.</summary>
    public static void Warm(IEnumerable<Material> materials)
    {
        foreach (Material m in materials)
            if (m != null) CopyOf(m);
    }

    /// <summary>The pattern copy of a material, made once; null where it can't be made.</summary>
    public static Material CopyOf(Material m)
    {
        if (_copies.TryGetValue(m, out Material copy)) return copy;
        copy = m switch
        {
            ShaderMaterial s => CopyShader(s),
            StandardMaterial3D s => CopyStandard(s),
            _ => null,
        };
        _copies[m] = copy;
        return copy;
    }

    /// <summary>The pattern: a value 0..1 for each pixel of the screen, interleaved so that any band of it is an even sprinkle.</summary>
    public const string PatternCode = @"
bool fade_out(vec2 at, vec2 band) {
	float n = fract(52.9829189 * fract(dot(at, vec2(0.06711056, 0.00583715))));
	return n < band.x || n >= band.y;
}
";

    static ShaderMaterial CopyShader(ShaderMaterial m)
    {
        if (m.Shader == null) return null;
        string source = m.Shader.Code;
        if (!_shaders.TryGetValue(source, out Shader shader))
        {
            string code = Inject(source);
            _shaders[source] = shader = code == null ? null : new Shader { Code = code };
        }
        if (shader == null) return null;
        var copy = new ShaderMaterial { Shader = shader, RenderPriority = m.RenderPriority };
        foreach (Godot.Collections.Dictionary u in m.Shader.GetShaderUniformList())
        {
            StringName name = (string)u["name"];
            Variant v = m.GetShaderParameter(name);
            if (v.VariantType != Variant.Type.Nil) copy.SetShaderParameter(name, v);
        }
        return copy;
    }

    /// <summary>A spatial shader's code with the band in front of its fragment; null if it isn't one, or has its own.</summary>
    public static string Inject(string code)
    {
        if (code == null || !code.Contains("shader_type spatial") || code.Contains(Band)) return null;
        int head = code.IndexOf("render_mode", System.StringComparison.Ordinal);
        if (head < 0) head = code.IndexOf("shader_type", System.StringComparison.Ordinal);
        int end = code.IndexOf(';', head) + 1;
        string uniforms = $"\ninstance uniform vec2 {Band} = vec2(0.0, 1.0);\n{PatternCode}";
        code = code[..end] + uniforms + code[end..];
        const string cut = "\n\tif (fade_out(FRAGCOORD.xy, " + Band + ")) discard;\n";
        int f = code.IndexOf("void fragment()", System.StringComparison.Ordinal);
        if (f < 0) return code + "\nvoid fragment() {" + cut + "}\n";
        int open = code.IndexOf('{', f) + 1;
        return code[..open] + cut + code[open..];
    }

    static readonly Dictionary<string, Shader> _standard = new();

    /// <summary>A standard material, written out as the same shading with the band: vertex colour, colour, roughness, metal, see-through, unshaded, culling. Anything more: null.</summary>
    static ShaderMaterial CopyStandard(StandardMaterial3D m)
    {
        if (m.AlbedoTexture != null || m.NormalEnabled || m.EmissionEnabled || m.RimEnabled || m.ClearcoatEnabled || m.AOEnabled
            || m.BillboardMode != BaseMaterial3D.BillboardModeEnum.Disabled || m.DistanceFadeMode != BaseMaterial3D.DistanceFadeModeEnum.Disabled
            || m.Transparency is not (BaseMaterial3D.TransparencyEnum.Disabled or BaseMaterial3D.TransparencyEnum.Alpha)
            || m.MetallicTexture != null || m.RoughnessTexture != null || m.DetailEnabled || m.HeightmapEnabled || m.SubsurfScatterEnabled
            || m.BlendMode != BaseMaterial3D.BlendModeEnum.Mix || m.UseParticleTrails || m.UsePointSize || m.FixedSize)
            return null;
        bool alpha = m.Transparency == BaseMaterial3D.TransparencyEnum.Alpha, unshaded = m.ShadingMode == BaseMaterial3D.ShadingModeEnum.Unshaded;
        string cull = m.CullMode switch { BaseMaterial3D.CullModeEnum.Front => "cull_front", BaseMaterial3D.CullModeEnum.Disabled => "cull_disabled", _ => "cull_back" };
        string depth = m.DepthDrawMode switch { BaseMaterial3D.DepthDrawModeEnum.Always => "depth_draw_always", BaseMaterial3D.DepthDrawModeEnum.Disabled => "depth_draw_never", _ => "depth_draw_opaque" };
        string key = $"{m.VertexColorUseAsAlbedo}{m.VertexColorIsSrgb}{alpha}{unshaded}{cull}{depth}{m.NoDepthTest}{m.DisableReceiveShadows}";
        if (!_standard.TryGetValue(key, out Shader shader))
        {
            string modes = $"blend_mix,{depth},{cull},diffuse_burley,specular_schlick_ggx{(unshaded ? ",unshaded" : "")}{(m.NoDepthTest ? ",depth_test_disabled" : "")}{(m.DisableReceiveShadows ? ",shadows_disabled" : "")}";
            string srgb = m.VertexColorUseAsAlbedo && m.VertexColorIsSrgb
                ? "\tif (!OUTPUT_IS_SRGB) COLOR.rgb = mix(pow((COLOR.rgb + vec3(0.055)) * (1.0 / (1.0 + 0.055)), vec3(2.4)), COLOR.rgb * (1.0 / 12.92), lessThan(COLOR.rgb, vec3(0.04045)));\n" : "";
            string colour = m.VertexColorUseAsAlbedo ? " * COLOR" : "";
            string code = $@"shader_type spatial;
render_mode {modes};
instance uniform vec2 {Band} = vec2(0.0, 1.0);
uniform vec4 albedo : source_color;
uniform float roughness : hint_range(0.0, 1.0);
uniform float metallic : hint_range(0.0, 1.0);
uniform float specular : hint_range(0.0, 1.0);
{PatternCode}
void vertex() {{
{srgb}}}
void fragment() {{
	if (fade_out(FRAGCOORD.xy, {Band})) discard;
	vec4 c = albedo{colour};
	ALBEDO = c.rgb;
	METALLIC = metallic;
	ROUGHNESS = roughness;
	SPECULAR = specular;
{(alpha ? "\tALPHA = c.a;\n" : "")}}}
";
            _standard[key] = shader = new Shader { Code = code };
        }
        var copy = new ShaderMaterial { Shader = shader, RenderPriority = m.RenderPriority };
        copy.SetShaderParameter("albedo", m.AlbedoColor);
        copy.SetShaderParameter("roughness", m.Roughness);
        copy.SetShaderParameter("metallic", m.Metallic);
        copy.SetShaderParameter("specular", m.MetallicSpecular);
        return copy;
    }
}
