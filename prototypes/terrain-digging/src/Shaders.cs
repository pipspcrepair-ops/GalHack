using Godot;

namespace TerrainDigging;

/// <summary>The handful of shaders the prototype uses, kept in code like the scenes.</summary>
public static class Shaders
{
    /// <summary>
    /// Terrain: vertex colours with painted detail and toon light, like a paperback cover.
    /// Broad colour fields break up open ground, rock faces get sedimentary bands, ore glows and
    /// pulses, and bedrock shows a faint cell grid so "this won't dig" reads before you try.
    /// </summary>
    public const string Terrain = @"
shader_type spatial;
render_mode cull_back, diffuse_toon, specular_schlick_ggx;

uniform vec3 grid_color : source_color = vec3(0.45, 0.38, 0.95);
// 1 shows normals, 2 shows unlit albedo: for checking the mesher and the palette.
uniform int debug_view = 0;

varying vec3 world_pos;
varying vec3 world_normal;
varying vec2 flags;

void vertex() {
    // Chunk meshes are built in world space, so VERTEX is already a world position.
    world_pos = VERTEX;
    world_normal = NORMAL;
    flags = UV;
}

void fragment() {
    float ore = flags.x;
    float bedrock = flags.y;
    vec3 base = COLOR.rgb;
    vec3 n = normalize(world_normal);
    float near = 1.0 - smoothstep(35.0, 80.0, distance(world_pos, CAMERA_POSITION_WORLD));

    // Broad colour fields, like big brush strokes, so open ground isn't one flat orange.
    float field = sin(world_pos.x * 0.043 + sin(world_pos.z * 0.031) * 2.0) * sin(world_pos.z * 0.051 + world_pos.x * 0.017);
    vec3 albedo = base * (1.0 + 0.10 * field * (1.0 - ore));

    // Sedimentary bands on steep faces, faded out before they can shimmer in the distance.
    float warp = sin(world_pos.x * 0.11 + world_pos.z * 0.07) * 0.6;
    float band = smoothstep(0.3, 0.7, abs(fract((world_pos.y + warp) / 2.4) * 2.0 - 1.0));
    float steep = smoothstep(0.25, 0.75, 1.0 - abs(n.y));
    albedo *= 1.0 - 0.14 * band * steep * near * (1.0 - ore);

    vec3 cell = abs(fract(world_pos) - 0.5);
    vec3 an = abs(n);
    float gx = max(cell.y, cell.z) * an.x;
    float gy = max(cell.x, cell.z) * an.y;
    float gz = max(cell.x, cell.y) * an.z;
    float grid = smoothstep(0.43, 0.48, gx + gy + gz) * bedrock;
    albedo = mix(albedo, grid_color, grid * 0.6);

    float pulse = 0.75 + 0.25 * sin(TIME * 2.4 + world_pos.x * 0.6 + world_pos.y * 0.9);
    ALBEDO = albedo;
    ROUGHNESS = mix(0.93, 0.3, ore);
    SPECULAR = 0.3;
    EMISSION = base * ore * 2.2 * pulse + grid_color * grid * 0.35;
    if (debug_view != 0) {
        EMISSION = debug_view == 1 ? n * 0.5 + 0.5 : albedo;
        ALBEDO = vec3(0.0);
    }
}
";

    /// <summary>
    /// Static water (rule 6): one flat plane, cut to the lakes by a mask made from the
    /// natural terrain. The mask never changes when players dig, so pits beside a lake stay dry.
    /// </summary>
    public const string Water = @"
shader_type spatial;
render_mode blend_mix, cull_disabled, depth_draw_opaque, specular_schlick_ggx;

uniform sampler2D lake_mask : filter_linear, repeat_disable;
uniform vec2 mask_origin;
uniform vec2 mask_size;
uniform vec3 deep_color : source_color;
uniform vec3 shallow_color : source_color;

varying vec3 world_pos;

void vertex() {
    world_pos = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xyz;
}

void fragment() {
    float m = texture(lake_mask, (world_pos.xz - mask_origin) / mask_size).r;
    if (m < 0.5) discard;
    float fresnel = pow(1.0 - clamp(dot(NORMAL, VIEW), 0.0, 1.0), 3.0);
    float ripple = sin(world_pos.x * 0.37 + TIME * 0.7) * sin(world_pos.z * 0.29 - TIME * 0.55);
    float glint = step(0.88, ripple);
    ALBEDO = mix(deep_color, shallow_color, clamp(fresnel * 0.8 + glint * 0.25, 0.0, 1.0));
    ALPHA = mix(0.80, 0.96, fresnel);
    ROUGHNESS = 0.06;
    SPECULAR = 0.7;
}
";

    /// <summary>Building blocks: painted panels with dark trim and a hazard stripe.</summary>
    public const string Block = @"
shader_type spatial;
render_mode diffuse_burley;

uniform vec3 body_color : source_color;
uniform vec3 trim_color : source_color;
uniform vec3 stripe_color : source_color;
uniform vec3 half_size = vec3(1.0);

varying vec3 local_pos;

void vertex() {
    local_pos = VERTEX;
}

void fragment() {
    vec3 a = abs(local_pos) / half_size;
    float near_edges = step(0.92, a.x) + step(0.92, a.y) + step(0.92, a.z);
    float trim = step(1.5, near_edges);
    float band = step(abs(local_pos.y - half_size.y * 0.35), 0.12) * step(a.x + a.z, 1.85);
    vec3 albedo = mix(body_color, stripe_color, band);
    albedo = mix(albedo, trim_color, trim);
    ALBEDO = albedo;
    ROUGHNESS = mix(0.55, 0.8, trim);
    METALLIC = 0.15;
}
";

    /// <summary>Unshaded, always-on-top marker: detector pings and anchor zones show through ground.</summary>
    public const string Marker = @"
shader_type spatial;
render_mode unshaded, blend_add, depth_test_disabled, cull_disabled, shadows_disabled;

uniform vec4 color : source_color = vec4(0.2, 1.0, 0.85, 1.0);
uniform float strength = 1.0;

void fragment() {
    float rim = 1.0 - abs(dot(NORMAL, VIEW));
    ALBEDO = color.rgb * (0.35 + rim) * strength;
    ALPHA = color.a * strength;
}
";

    public static ShaderMaterial Make(string code) => new() { Shader = new Shader { Code = code } };
}
