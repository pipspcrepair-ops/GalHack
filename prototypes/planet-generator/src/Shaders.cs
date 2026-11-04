using Godot;

namespace Planets;

/// <summary>
/// Every shader in the prototype, as source built in code like the rest of the scene. They aim for
/// the look of a painted paperback cover: flat bold colour, soft gradients, a few strong details,
/// and no high-frequency noise.
/// </summary>
static class Shaders
{
    /// <summary>Integer hashing and value noise that repeats every `period` cells in x, so
    /// patterns tied to map coordinates stay continuous across the planet's east-west seam.</summary>
    /// <summary>Vertex colours and instance tints arrive as sRGB (the palettes are hex); lighting needs linear.</summary>
    const string Srgb = """
        vec3 srgb_to_linear(vec3 c) {
            return mix(c / 12.92, pow((c + 0.055) / 1.055, vec3(2.4)), step(0.04045, c));
        }
        """;

    const string Noise2D = """
        float hash2(vec2 p) {
            uvec2 q = uvec2(ivec2(floor(p))) * uvec2(1597334673u, 3812015801u);
            return float((q.x ^ q.y) * 1597334673u) * 2.3283064e-10;
        }
        float vnoise(vec2 p, float period) {
            vec2 i = floor(p);
            vec2 f = fract(p);
            vec2 u = f * f * (3.0 - 2.0 * f);
            float x0 = mod(i.x, period), x1 = mod(i.x + 1.0, period);
            float a = hash2(vec2(x0, i.y)), b = hash2(vec2(x1, i.y));
            float c = hash2(vec2(x0, i.y + 1.0)), d = hash2(vec2(x1, i.y + 1.0));
            return mix(mix(a, b, u.x), mix(c, d, u.x), u.y);
        }
        """;

    const string Noise3D = """
        float hash3(vec3 p) {
            uvec3 q = uvec3(ivec3(floor(p))) * uvec3(1597334673u, 3812015801u, 2798796415u);
            return float((q.x ^ q.y ^ q.z) * 1597334673u) * 2.3283064e-10;
        }
        float vnoise3(vec3 p) {
            vec3 i = floor(p);
            vec3 f = fract(p);
            vec3 u = f * f * (3.0 - 2.0 * f);
            float a = mix(hash3(i), hash3(i + vec3(1, 0, 0)), u.x);
            float b = mix(hash3(i + vec3(0, 1, 0)), hash3(i + vec3(1, 1, 0)), u.x);
            float c = mix(hash3(i + vec3(0, 0, 1)), hash3(i + vec3(1, 0, 1)), u.x);
            float d = mix(hash3(i + vec3(0, 1, 1)), hash3(i + vec3(1, 1, 1)), u.x);
            return mix(mix(a, b, u.y), mix(c, d, u.y), u.z);
        }
        float fbm3(vec3 p) {
            float s = 0.0, a = 0.5;
            for (int i = 0; i < 4; i++) { s += a * vnoise3(p); p *= 2.03; a *= 0.5; }
            return s / 0.9375;
        }
        """;

    // ---- Surface ------------------------------------------------------------------------

    /// <summary>
    /// Terrain: vertex colours from the biome blend, softly mottled at two scales so large areas
    /// read as painted rather than flat. Vertex alpha marks glowing ground (lava fields), which
    /// gets veins of light. The far variant cuts a hole where detailed chunks are loaded.
    /// </summary>
    static string TerrainSource(bool far) => $$"""
        shader_type spatial;
        render_mode cull_back, diffuse_burley, specular_schlick_ggx;
        uniform vec3 glow_color : source_color = vec3(1.0, 0.3, 0.05);
        uniform float glow_strength = 4.0;
        uniform float veins = 0.0;  // 1: glow runs in cracks (lava fields); 0: a soft even glow (moss, springs)
        uniform float map_width = 16000.0;
        uniform vec4 hole = vec4(0.0);
        varying vec3 world_pos;
        {{Srgb}}
        {{Noise2D}}
        void vertex() {
            world_pos = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xyz;
        }
        void fragment() {
            {{(far ? "if (world_pos.x > hole.x && world_pos.x < hole.z && world_pos.z > hole.y && world_pos.z < hole.w) discard;" : "")}}
            vec2 m = UV; // map coordinates in metres, continuous across chunk borders
            float mottle = vnoise(m * 0.045, map_width * 0.045) * 0.6 + vnoise(m * 0.18, map_width * 0.18) * 0.4;
            vec3 c = srgb_to_linear(COLOR.rgb) * (0.88 + 0.24 * mottle);
            ALBEDO = c;
            ROUGHNESS = 0.92;
            SPECULAR = 0.25;
            if (COLOR.a > 0.01) {
                if (veins > 0.5) {
                    float v = vnoise(m * 0.05, map_width * 0.05) + 0.5 * vnoise(m * 0.13, map_width * 0.13);
                    float vein = 1.0 - smoothstep(0.0, 0.035, abs(v - 0.78));
                    EMISSION = glow_color * glow_strength * COLOR.a * (vein + 0.08);
                } else {
                    EMISSION = c * COLOR.a * 0.6;
                }
            }
        }
        """;

    /// <summary>
    /// Scatter. Each instance carries a tint and a glow in its custom data; vertex alpha says which
    /// parts take the tint, so trunks and stalks keep their own colour.
    /// </summary>
    const string FloraSource = $$"""
        shader_type spatial;
        render_mode cull_back, diffuse_burley, specular_schlick_ggx;
        varying vec4 tint;
        {{Srgb}}
        void vertex() {
            tint = INSTANCE_CUSTOM;
        }
        void fragment() {
            vec3 base = srgb_to_linear(COLOR.rgb);
            vec3 c = mix(base, base * srgb_to_linear(tint.rgb), COLOR.a);
            ALBEDO = c;
            ROUGHNESS = 0.85;
            SPECULAR = 0.3;
            EMISSION = c * tint.a * COLOR.a * 2.0;
        }
        """;

    /// <summary>
    /// Liquids. Water and alien seas are translucent with depth colour from a small map texture
    /// (r = depth, g = frozen), so shallows and pack ice line up with the world map. Lava is opaque
    /// and self-lit, with a slowly drifting crust.
    /// </summary>
    const string WaterSource = $$"""
        shader_type spatial;
        render_mode blend_mix, cull_disabled, depth_draw_always, specular_schlick_ggx;
        uniform sampler2D sea_map : filter_linear, repeat_enable;
        uniform vec2 map_size = vec2(16000.0, 8000.0);
        uniform vec3 shallow : source_color;
        uniform vec3 deep : source_color;
        uniform vec3 ice : source_color;
        uniform vec3 sky : source_color;
        uniform float lava = 0.0;
        uniform float glow = 0.0;
        varying vec3 world_pos;
        {{Noise2D}}
        void vertex() {
            world_pos = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xyz;
        }
        void fragment() {
            vec2 sea = texture(sea_map, world_pos.xz / map_size).rg;
            vec2 p = world_pos.xz;
            float t = TIME;
            if (lava > 0.5) {
                // Dark crust plates with crisp, bright seams and open pools of melt between them.
                float n = vnoise(p * 0.03 + vec2(t * 0.015, 0.0), 1e6) * 0.7 + vnoise(p * 0.09 - vec2(0.0, t * 0.02), 1e6) * 0.3;
                float seam = 1.0 - smoothstep(0.0, 0.022, abs(n - 0.48));
                float pool = smoothstep(0.6, 0.625, n);
                float hot = max(seam, pool);
                vec3 c = mix(shallow * 0.22, deep, hot);
                ALBEDO = c * 0.4;
                EMISSION = c * glow * (0.12 + hot);
                ROUGHNESS = 0.8;
                ALPHA = 1.0;
            } else {
                // Gentle swell: three crossing sine waves, no noisy normal maps.
                vec2 g = vec2(0.0);
                g += vec2(0.6, 0.8) * cos(dot(p, vec2(0.6, 0.8)) * 0.12 + t * 0.9) * 0.05;
                g += vec2(-0.9, 0.3) * cos(dot(p, vec2(-0.9, 0.3)) * 0.21 + t * 1.3) * 0.035;
                g += vec2(0.2, -1.0) * cos(dot(p, vec2(0.2, -1.0)) * 0.5 + t * 2.1) * 0.015;
                vec3 n = normalize(vec3(-g.x, 1.0, -g.y));
                float depth = sea.r;
                vec3 c = mix(shallow, deep, smoothstep(0.0, 0.6, depth));
                float fres = pow(1.0 - clamp(dot(normalize(CAMERA_POSITION_WORLD - world_pos), n), 0.0, 1.0), 4.0);
                c = mix(c, sky, fres * 0.3); // keep the water's own colour: a pale sky sheen reads as grey
                float frozen = smoothstep(0.35, 0.65, sea.g);
                float crack = 1.0 - smoothstep(0.0, 0.03, abs(vnoise(p * 0.02, 1e6) - 0.5));
                c = mix(c, ice * (1.0 - 0.25 * crack), frozen);
                n = normalize(mix(n, vec3(0.0, 1.0, 0.0), frozen));
                NORMAL = normalize((VIEW_MATRIX * vec4(n, 0.0)).xyz);
                ALBEDO = c;
                // A little self-light keeps the water's colour at grazing angles, where the sky's
                // reflection would otherwise turn every river pale grey.
                EMISSION = c * (glow + 0.12 * (1.0 - frozen));
                ROUGHNESS = mix(0.25, 0.6, frozen);
                SPECULAR = 0.2;
                ALPHA = mix(mix(0.82, 0.94, smoothstep(0.0, 0.4, depth)), 1.0, frozen);
            }
        }
        """;

    /// <summary>
    /// The polar storm wall (or blizzard): a towering curtain of slowly churning cloud. It can fill
    /// half the screen, so it costs three texture reads per pixel rather than procedural noise.
    /// The texture wraps exactly once around the planet (UV.x 0-1), so it meets itself at the seam;
    /// the billows only drift sideways, at whole-number scales, to keep that true.
    /// </summary>
    const string CurtainSource = $$"""
        shader_type spatial;
        render_mode unshaded, blend_mix, cull_disabled, depth_draw_always;
        uniform sampler2D noise_tex : repeat_enable, filter_linear_mipmap;
        uniform vec3 dark : source_color;
        uniform vec3 light : source_color;
        uniform vec3 fog : source_color;
        uniform float drift = 0.0002;   // texture widths per second
        {{Noise3D}}
        void fragment() {
            float t = TIME * drift;
            float a = texture(noise_tex, vec2(UV.x + t, UV.y)).r;
            float b = texture(noise_tex, vec2(UV.x * 2.0 - t * 1.7 + (a - 0.5) * 0.004, 1.0 - UV.y)).r;
            float billow = a * 0.65 + b * 0.35;
            float lit = smoothstep(0.4, 0.62, billow) * (0.3 + 0.7 * UV.y);
            vec3 c = mix(dark, light, lit);
            // Distant lightning: rare, broad flashes deep in the cloud.
            float flash = step(0.985, hash3(vec3(floor(UV.x * 400.0), floor(TIME * 1.5), 7.0))) * smoothstep(0.5, 0.7, billow);
            c += light * flash * 1.5;
            // Ragged cloud tops instead of a ruler-straight edge.
            float top = 0.58 + 0.24 * texture(noise_tex, vec2(UV.x + t * 0.5, 0.5)).r;
            float fade = smoothstep(top, top - 0.12 - 0.1 * billow, UV.y);
            ALBEDO = mix(fog, c, 0.9);
            ALPHA = clamp(0.93 + 0.07 * lit, 0.0, 1.0) * fade;
        }
        """;

    /// <summary>
    /// The sky from the ground: a two-colour gradient, a sun with a soft halo, a big neighbour
    /// world or moon, optional planetary ring arc, stars and a band of painterly clouds.
    /// Static (no TIME) so the radiance map is only computed once.
    /// </summary>
    const string SkySource = $$"""
        shader_type sky;
        render_mode use_debanding;
        uniform vec3 top_color : source_color;
        uniform vec3 horizon_color : source_color;
        uniform vec3 ground_color : source_color;
        uniform vec3 sun_color : source_color;
        uniform vec3 moon_dir = vec3(0.0, 0.4, -1.0);
        uniform vec3 moon_color : source_color;
        uniform float moon_size = 0.06;
        uniform float ring = 0.0;
        uniform vec3 ring_color : source_color;
        uniform float stars = 0.0;
        uniform vec3 cloud_color : source_color;
        uniform float cloud_cover = 0.4;
        {{Noise3D}}
        void sky() {
            vec3 d = EYEDIR;
            float h = d.y;
            vec3 sun = LIGHT0_DIRECTION;
            float s = max(dot(d, sun), 0.0);
            vec3 col = mix(horizon_color, top_color, smoothstep(-0.02, 0.55, h));
            col += sun_color * (pow(s, 8.0) * 0.25 + pow(s, 64.0) * 0.4);

            if (stars > 0.0 && h > 0.0) {
                vec3 cell = floor(d * 220.0);
                float r = hash3(cell);
                vec3 centre = (cell + 0.5 + (vec3(hash3(cell + 3.1), hash3(cell + 7.3), hash3(cell + 1.7)) - 0.5) * 0.7) / 220.0;
                float star = smoothstep(0.0028, 0.0, length(d - centre)) * step(0.985, r);
                col += vec3(star) * stars * smoothstep(0.0, 0.3, h) * (1.0 - smoothstep(0.9, 1.0, s));
            }

            if (ring > 0.0) {
                // The planet's own ring, seen from inside its plane: a thin arch from horizon to
                // horizon, foreshortened to a hairline near the ground and widest overhead.
                vec3 n = normalize(vec3(0.0, 0.42, 1.0));
                float k = dot(d, n);
                float w = 0.008 + 0.03 * clamp(h, 0.0, 1.0);
                float u = k / w;                                   // -1..1 across the band
                float band = smoothstep(1.0, 0.85, abs(u)) * smoothstep(-0.01, 0.05, h);
                float stripes = 0.65 + 0.35 * sin(u * 9.0) * sin(u * 23.0 + 1.3);
                float gap = smoothstep(0.04, 0.12, abs(u - 0.3));
                col = mix(col, ring_color * (0.8 + 0.3 * stripes), band * gap * ring * 0.85);
            }

            vec3 md = normalize(moon_dir);
            float m = dot(d, md);
            float edge = cos(moon_size);
            if (m > edge) {
                // A lit sphere: shade the disc by the sun's direction, with a darker limb.
                vec3 local = normalize(d - md * m);
                float rr = sqrt(clamp((1.0 - m) / (1.0 - edge), 0.0, 1.0));
                vec3 nrm = normalize(-md * sqrt(max(1.0 - rr * rr, 0.0)) + local * rr);
                float lit = clamp(dot(nrm, sun) * 0.9 + 0.35, 0.12, 1.0);
                float bands = 0.92 + 0.08 * sin(dot(nrm, vec3(0.2, 1.0, 0.1)) * 18.0);
                vec3 body = moon_color * lit * bands;
                col = mix(col, body, smoothstep(edge, edge + 0.0004, m) * 0.97);
            }

            if (h > 0.0) {
                vec2 uv = d.xz / (h + 0.12);
                float c = fbm3(vec3(uv * 0.9, 1.7)) * 0.7 + fbm3(vec3(uv * 2.6, 4.2)) * 0.3;
                float cover = smoothstep(0.62 - cloud_cover * 0.35, 0.82 - cloud_cover * 0.3, c);
                float fade = smoothstep(0.0, 0.08, h) * (1.0 - smoothstep(0.45, 0.9, h));
                vec3 cc = cloud_color * (0.85 + 0.3 * pow(s, 4.0)) + sun_color * pow(s, 16.0) * 0.4;
                col = mix(col, mix(horizon_color, cc, 0.85), cover * fade * 0.9);
            }

            col = mix(col, ground_color, smoothstep(0.0, -0.12, h));
            col += sun_color * smoothstep(0.9994, 0.9997, s) * 6.0;
            COLOR = col;
        }
        """;

    // ---- Orbit --------------------------------------------------------------------------

    /// <summary>The globe: the generator's colour map, glossy seas, and glow that brightens on the night side.</summary>
    const string PlanetSource = """
        shader_type spatial;
        render_mode diffuse_burley, specular_schlick_ggx;
        uniform sampler2D albedo_map : source_color, filter_linear_mipmap_anisotropic, repeat_enable;
        uniform sampler2D glow_map : source_color, filter_linear_mipmap, repeat_enable;
        uniform vec3 sun_dir = vec3(1.0, 0.0, 0.0);
        uniform float day_glow = 0.6;
        uniform float night_glow = 1.6;
        void fragment() {
            vec4 a = texture(albedo_map, UV);
            ALBEDO = a.rgb;
            ROUGHNESS = mix(0.9, 0.42, a.a);
            SPECULAR = mix(0.1, 0.45, a.a);
            vec3 n = normalize((INV_VIEW_MATRIX * vec4(NORMAL, 0.0)).xyz);
            float night = smoothstep(0.15, -0.25, dot(n, sun_dir));
            EMISSION = texture(glow_map, UV).rgb * (day_glow + night * night_glow);
        }
        """;

    const string CloudSource = """
        shader_type spatial;
        render_mode blend_mix, depth_draw_never, cull_back, diffuse_lambert_wrap, specular_disabled;
        uniform sampler2D cloud_map : source_color, filter_linear_mipmap, repeat_enable;
        void fragment() {
            vec4 c = texture(cloud_map, UV);
            ALBEDO = c.rgb;
            ALPHA = c.a;
        }
        """;

    /// <summary>Atmosphere: a thin rim of light, brightest on the day side and at the terminator.</summary>
    const string AtmosphereSource = """
        shader_type spatial;
        render_mode unshaded, blend_add, depth_draw_never, cull_back;
        uniform vec3 color : source_color;
        uniform vec3 sun_dir = vec3(1.0, 0.0, 0.0);
        uniform float strength = 1.4;
        void fragment() {
            float rim = 1.0 - clamp(dot(NORMAL, VIEW), 0.0, 1.0);
            vec3 n = normalize((INV_VIEW_MATRIX * vec4(NORMAL, 0.0)).xyz);
            float day = smoothstep(-0.35, 0.4, dot(n, sun_dir));
            float glow = pow(rim, 3.0) * smoothstep(1.0, 0.82, rim);
            ALBEDO = color * glow * day * strength;
        }
        """;

    const string RingSource = """
        shader_type spatial;
        render_mode blend_mix, cull_disabled, depth_draw_always, diffuse_lambert_wrap;
        uniform vec3 color : source_color;
        void fragment() {
            float r = UV.x; // 0 at the inner edge, 1 at the outer
            float bands = 0.55 + 0.25 * sin(r * 61.0) + 0.2 * sin(r * 23.0 + 1.0);
            float gap = smoothstep(0.0, 0.02, abs(r - 0.62)) * smoothstep(0.0, 0.05, r) * smoothstep(1.0, 0.94, r);
            ALBEDO = color * (0.8 + 0.3 * bands);
            ALPHA = clamp(bands, 0.0, 1.0) * gap * 0.75;
        }
        """;

    /// <summary>Deep space: stars, a painted nebula in the planet's accent colour, and the sun's glare.</summary>
    const string SpaceSource = $$"""
        shader_type sky;
        render_mode use_debanding;
        uniform vec3 nebula : source_color;
        uniform vec3 nebula_b : source_color;
        {{Noise3D}}
        void sky() {
            vec3 d = EYEDIR;
            vec3 col = vec3(0.012, 0.012, 0.03);
            float n = fbm3(d * 2.2 + vec3(fbm3(d * 3.0) * 1.2));
            float neb = smoothstep(0.42, 0.85, n);
            col += nebula * neb * 0.55 + nebula_b * smoothstep(0.6, 0.95, fbm3(d * 4.5 + 3.0)) * neb * 0.35;
            vec3 cell = floor(d * 380.0);
            float r = hash3(cell);
            vec3 centre = (cell + 0.5 + (vec3(hash3(cell + 3.1), hash3(cell + 7.3), hash3(cell + 1.7)) - 0.5) * 0.7) / 380.0;
            float star = smoothstep(0.0018, 0.0, length(d - centre)) * step(0.975, r) * (0.4 + 0.6 * hash3(cell + 9.9));
            col += vec3(star) * mix(vec3(1.0, 0.9, 0.8), vec3(0.8, 0.9, 1.0), hash3(cell + 2.2)) * 1.6;
            float s = max(dot(d, LIGHT0_DIRECTION), 0.0);
            col += vec3(1.0, 0.92, 0.8) * (pow(s, 900.0) * 30.0 + pow(s, 40.0) * 0.25);
            COLOR = col;
        }
        """;

    static Shader _terrain, _far, _flora, _water, _curtain, _sky, _planet, _cloud, _atmosphere, _ring, _space;

    public static Shader Terrain => _terrain ??= Make(TerrainSource(false));
    public static Shader FarTerrain => _far ??= Make(TerrainSource(true));
    public static Shader Flora => _flora ??= Make(FloraSource);
    public static Shader Water => _water ??= Make(WaterSource);
    public static Shader Curtain => _curtain ??= Make(CurtainSource);
    public static Shader Sky => _sky ??= Make(SkySource);
    public static Shader Planet => _planet ??= Make(PlanetSource);
    public static Shader Cloud => _cloud ??= Make(CloudSource);
    public static Shader Atmosphere => _atmosphere ??= Make(AtmosphereSource);
    public static Shader Ring => _ring ??= Make(RingSource);
    public static Shader Space => _space ??= Make(SpaceSource);

    static Shader Make(string code) => new() { Code = code };
}
