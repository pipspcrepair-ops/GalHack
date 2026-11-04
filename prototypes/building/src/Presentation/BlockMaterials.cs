using System.Linq;
using Godot;

namespace GalHack.Structures.Presentation;

/// <summary>
/// The shaders that draw structures. Every block face in a chunk shares one material: the
/// mesher packs each face's paint into vertex data (base colour in COLOR, accent colour and
/// pattern/stencil/texture/grime/damage flags in UV2), and the shader draws the livery from that.
/// UV holds the face's position in block units, so stripes and checks run on across merged faces.
/// </summary>
public static class BlockMaterials
{
    // Bit layout of the flags in UV2.y. Kept below 2^24 so a float carries it exactly.
    public const int PatternShift = 0, StencilShift = 3, TextureShift = 7, DamageShift = 9,
        GlowShift = 11, MetalShift = 12, GrimeShift = 14, PaintedShift = 16;

    static ShaderMaterial _opaque, _glass;

    public static ShaderMaterial Opaque => _opaque ??= new ShaderMaterial { Shader = new Shader { Code = OpaqueCode() } };
    public static ShaderMaterial Glass => _glass ??= new ShaderMaterial { Shader = new Shader { Code = GlassCode } };

    /// <summary>A translucent hologram for the ghost preview, tinted per use.</summary>
    public static ShaderMaterial Ghost(Color tint)
    {
        var m = new ShaderMaterial { Shader = _ghostShader ??= new Shader { Code = GhostCode } };
        m.SetShaderParameter("tint", tint);
        return m;
    }

    static Shader _ghostShader;

    // Stencil numerals, 5×7, drawn as chunky paint-mask digits.
    static readonly string[] Digits =
    {
        ".###. #...# #..## #.#.# ##..# #...# .###.",
        "..#.. .##.. ..#.. ..#.. ..#.. ..#.. .###.",
        ".###. #...# ....# ...#. ..#.. .#... #####",
        "####. ....# ....# .###. ....# ....# ####.",
        "...#. ..##. .#.#. #..#. ##### ...#. ...#.",
        "##### #.... ####. ....# ....# #...# .###.",
        ".###. #.... #.... ####. #...# #...# .###.",
        "##### ....# ...#. ..#.. .#... .#... .#...",
        ".###. #...# #...# .###. #...# #...# .###.",
        ".###. #...# #...# .#### ....# ....# .###.",
    };

    /// <summary>Rows 0-3 of each digit packed 5 bits per row into one int, rows 4-6 into another.</summary>
    static (string a, string b) GlyphTables()
    {
        var a = new int[10];
        var b = new int[10];
        for (int d = 0; d < 10; d++)
        {
            string[] rows = Digits[d].Split(' ');
            int Row(int r) => rows[r].Aggregate(0, (acc, c) => (acc << 1) | (c == '#' ? 1 : 0));
            a[d] = (Row(0) << 15) | (Row(1) << 10) | (Row(2) << 5) | Row(3);
            b[d] = (Row(4) << 10) | (Row(5) << 5) | Row(6);
        }
        return (string.Join(", ", a), string.Join(", ", b));
    }

    const string Common = @"
vec3 to_linear(vec3 c) { return mix(c / 12.92, pow((c + 0.055) / 1.055, vec3(2.4)), step(0.04045, c)); }
vec3 unpack_rgb(int c) { return vec3(float((c >> 16) & 255), float((c >> 8) & 255), float(c & 255)) / 255.0; }
float aastep(float edge, float x) { float w = max(fwidth(x) * 0.7, 1e-4); return smoothstep(edge - w, edge + w, x); }
float hash3(vec3 p) { p = fract(p * 0.3183099 + 0.1); p *= 17.0; return fract(p.x * p.y * p.z * (p.x + p.y + p.z)); }
float noise3(vec3 x) {
    vec3 i = floor(x); vec3 f = fract(x); f = f * f * (3.0 - 2.0 * f);
    return mix(mix(mix(hash3(i), hash3(i + vec3(1,0,0)), f.x), mix(hash3(i + vec3(0,1,0)), hash3(i + vec3(1,1,0)), f.x), f.y),
               mix(mix(hash3(i + vec3(0,0,1)), hash3(i + vec3(1,0,1)), f.x), mix(hash3(i + vec3(0,1,1)), hash3(i + vec3(1,1,1)), f.x), f.y), f.z);
}
float fbm3(vec3 p) { return 0.55 * noise3(p) + 0.3 * noise3(p * 2.03 + 3.1) + 0.15 * noise3(p * 4.07 + 7.7); }
";

    static string OpaqueCode()
    {
        var (a, b) = GlyphTables();
        return @"
shader_type spatial;
render_mode cull_back, diffuse_burley, specular_schlick_ggx;

varying flat vec2 v_paint;
varying vec3 v_local;

const int GLYPH_A[10] = { " + a + @" };
const int GLYPH_B[10] = { " + b + @" };
" + Common + @"
float glyph(int digit, vec2 g) {
    if (g.x < 0.0 || g.x >= 1.0 || g.y < 0.0 || g.y >= 1.0) return 0.0;
    int col = int(g.x * 5.0);
    int row = 6 - int(g.y * 7.0);
    int bits = row < 4 ? (GLYPH_A[digit] >> ((3 - row) * 5)) : (GLYPH_B[digit] >> ((6 - row) * 5));
    return float((bits >> (4 - col)) & 1);
}

void vertex() {
    v_paint = UV2;
    v_local = VERTEX;
}

void fragment() {
    int accent_rgb = int(v_paint.x + 0.5);
    int flags = int(v_paint.y + 0.5);
    int pattern = flags & 7;
    int stencil = (flags >> 3) & 15;
    int tex = (flags >> 7) & 3;
    int damage = (flags >> 9) & 3;
    int glow = (flags >> 11) & 1;
    int metal = (flags >> 12) & 3;
    int grime = (flags >> 14) & 3;
    int painted = (flags >> 16) & 1;

    vec3 base = COLOR.rgb;
    vec3 accent = unpack_rgb(accent_rgb);
    vec2 uv = UV;
    vec2 f = fract(uv);

    // Livery patterns, continuous across neighbouring blocks.
    float m = 0.0;
    if (pattern == 1) {        // stripes: a bold band and a pinstripe per block
        m = aastep(0.16, f.y) * (1.0 - aastep(0.56, f.y)) + aastep(0.68, f.y) * (1.0 - aastep(0.8, f.y));
    } else if (pattern == 2) { // chevrons pointing along the face
        m = aastep(0.5, fract(uv.x - abs(f.y - 0.5) * 1.1));
    } else if (pattern == 3) { // checkerboard, 2×2 per block
        m = abs(aastep(0.5, f.x) - aastep(0.5, f.y));
    } else if (pattern == 4) { // hazard stripes
        m = aastep(0.5, fract((uv.x + uv.y) * 1.5));
    }
    vec3 col = mix(base, accent, m);
    if (stencil > 0) {
        vec2 g = (f - vec2(0.2, 0.14)) / vec2(0.6, 0.72);
        col = mix(col, accent, glyph(stencil - 1, g));
    }
    col = to_linear(col);

    // Surface texture.
    float edge = min(min(f.x, 1.0 - f.x), min(f.y, 1.0 - f.y));
    float shade = 1.0;
    if (tex == 0) {            // panel: a seam round each block and a soft bevel
        shade *= mix(0.5, 1.0, aastep(0.022, edge));
        shade *= 0.93 + 0.07 * smoothstep(0.02, 0.14, edge);
    } else if (tex == 2) {     // plates: half-block plates with rivets
        vec2 h = fract(uv * 2.0);
        float e2 = min(min(h.x, 1.0 - h.x), min(h.y, 1.0 - h.y));
        shade *= mix(0.55, 1.0, aastep(0.03, e2));
        shade *= mix(0.72, 1.0, aastep(0.045, length(min(h, 1.0 - h) - vec2(0.13))));
    } else if (tex == 3) {     // ribbed: vertical corrugation
        shade *= 0.8 + 0.2 * smoothstep(0.1, 0.9, abs(fract(uv.x * 4.0) - 0.5) * 2.0);
        shade *= mix(0.5, 1.0, aastep(0.022, edge));
    }
    col *= shade;

    // Grime: blotches and streaks that gather low on each block.
    if (grime > 0) {
        float g = float(grime) / 3.0;
        float n = fbm3(v_local * 0.9);
        float streak = noise3(vec3(uv.x * 7.0, uv.y * 0.5, 1.7));
        float low = 1.0 - smoothstep(0.0, 0.7, f.y);
        float dirt = clamp((n - 0.35) * 1.6 + low * 0.45 * streak, 0.0, 1.0) * g;
        col = mix(col, col * vec3(0.3, 0.25, 0.2), dirt);
    }

    // Damage: scorch marks spreading as hit points fall, glowing cracks when critical.
    vec3 emission = vec3(0.0);
    if (damage > 0) {
        float d = float(damage) / 3.0;
        float n = fbm3(v_local * 1.6 + 11.0);
        col = mix(col, vec3(0.02, 0.018, 0.015), smoothstep(0.72 - d * 0.42, 0.8 - d * 0.36, n) * 0.92);
        if (damage == 3) emission = vec3(1.0, 0.32, 0.04) * smoothstep(0.62, 0.7, n) * (1.0 - smoothstep(0.7, 0.8, n)) * 3.0;
    }

    ALBEDO = col;
    ROUGHNESS = painted == 1 ? 0.6 : mix(0.72, 0.38, float(metal) / 3.0);
    METALLIC = painted == 1 ? 0.04 : float(metal) * 0.17;
    if (glow == 1) {
        emission += to_linear(base) * 3.2;
        ALBEDO = col * 0.4;
    }
    EMISSION = emission;
}
";
    }

    const string GlassCode = @"
shader_type spatial;
render_mode blend_mix, cull_back, depth_draw_opaque, specular_schlick_ggx;
" + Common + @"
void fragment() {
    vec2 f = fract(UV);
    float edge = min(min(f.x, 1.0 - f.x), min(f.y, 1.0 - f.y));
    float frame = 1.0 - aastep(0.035, edge);
    float fres = pow(1.0 - clamp(dot(NORMAL, VIEW), 0.0, 1.0), 3.0);
    vec3 tint = to_linear(COLOR.rgb);
    ALBEDO = mix(tint * 0.25, vec3(0.02, 0.02, 0.03), frame);
    ALPHA = mix(0.22 + fres * 0.55, 1.0, frame);
    ROUGHNESS = mix(0.04, 0.5, frame);
    METALLIC = 0.3;
    SPECULAR = 0.9;
}
";

    const string GhostCode = @"
shader_type spatial;
render_mode unshaded, blend_mix, cull_back, depth_draw_never;
uniform vec4 tint : source_color = vec4(0.4, 1.0, 0.9, 1.0);
void fragment() {
    vec2 f = fract(UV);
    float edge = min(min(f.x, 1.0 - f.x), min(f.y, 1.0 - f.y));
    float wire = 1.0 - smoothstep(0.025, 0.06, edge);
    float fres = pow(1.0 - clamp(dot(NORMAL, VIEW), 0.0, 1.0), 2.0);
    float scan = 0.5 + 0.5 * sin(UV.y * 18.0 - TIME * 5.0);
    ALBEDO = tint.rgb;
    ALPHA = clamp(0.16 + fres * 0.3 + wire * 0.75 + scan * 0.06, 0.0, 1.0) * tint.a;
}
";
}
