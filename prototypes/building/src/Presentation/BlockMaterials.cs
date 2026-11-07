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
        GlowShift = 11, MetalShift = 12, GrimeShift = 14, PaintedShift = 16, WearShift = 17, HoledShift = 19;

    static ShaderMaterial _opaque, _glass;

    public static ShaderMaterial Opaque => _opaque ??= new ShaderMaterial { Shader = new Shader { Code = OpaqueCode() } };
    public static ShaderMaterial Glass => _glass ??= new ShaderMaterial { Shader = new Shader { Code = GlassCode } };

    /// <summary>A translucent hologram for the ghost preview, tinted per use. Fill 0 draws only the edges.</summary>
    public static ShaderMaterial Ghost(Color tint, float fill = 0.3f)
    {
        var m = new ShaderMaterial { Shader = _ghostShader ??= new Shader { Code = GhostCode } };
        m.SetShaderParameter("tint", tint);
        m.SetShaderParameter("fill", fill);
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
vec2 hash2(vec2 p) { float a = hash3(vec3(p, 1.7)); return vec2(a, hash3(vec3(p, a * 13.1 + 4.3))); }
// One block's seed: the mesher's, in the colour's alpha, or (merged faces, where that reads 1) from
// which block of which plane this is. Either way it depends only on the block, so remeshing can't change it.
// The seed is snapped to the mesher's 1/128 steps: interpolation jitters it a hair, and the hashes would turn that into speckle.
float block_seed(float alpha, vec2 uv, float plane) { return alpha > 0.998 ? hash3(vec3(floor(uv), floor(plane * 3.0 + 0.5))) : floor(alpha * 128.0 + 0.5) / 128.0; }
// Worley borders: 1 on a line between cells. Thin lines that branch and meet, as real cracks do.
float crack_lines(vec2 p, float seed, float width) {
    vec2 ip = floor(p); vec2 fp = fract(p); float d1 = 9.0; float d2 = 9.0;
    for (int y = -1; y <= 1; y++) for (int x = -1; x <= 1; x++) {
        vec2 g = vec2(float(x), float(y));
        float d = length(g + hash2(ip + g + seed * 37.0) - fp);
        if (d < d1) { d2 = d1; d1 = d; } else if (d < d2) { d2 = d; }
    }
    return 1.0 - aastep(width, d2 - d1);
}
// Worley cells: 1 inside round pits scattered over the face.
float pits(vec2 p, float seed, float radius) {
    vec2 ip = floor(p); vec2 fp = fract(p); float d1 = 9.0;
    for (int y = -1; y <= 1; y++) for (int x = -1; x <= 1; x++) {
        vec2 g = vec2(float(x), float(y));
        d1 = min(d1, length(g + hash2(ip + g + seed * 53.0) - fp));
    }
    return 1.0 - aastep(radius, d1);
}
// Scuffs: a few short straight scratches across a block, each at its own angle.
float scratches(vec2 f, float seed) {
    float m = 0.0;
    for (int i = 0; i < 6; i++) {
        float k = float(i) + seed * 17.0;
        float a = hash3(vec3(k, 1.0, 2.0)) * 3.1416;
        vec2 c = vec2(hash3(vec3(k, 3.0, 4.0)), hash3(vec3(k, 5.0, 6.0))) * 0.8 + 0.1;
        vec2 dir = vec2(cos(a), sin(a));
        vec2 q = f - c;
        float len = 0.12 + 0.22 * hash3(vec3(k, 7.0, 8.0));
        m = max(m, (1.0 - aastep(0.007, abs(dot(q, vec2(-dir.y, dir.x))))) * (1.0 - aastep(len, abs(dot(q, dir)))));
    }
    return m;
}
";

    static string OpaqueCode()
    {
        var (a, b) = GlyphTables();
        return @"
shader_type spatial;
render_mode cull_back, diffuse_burley, specular_schlick_ggx;

varying flat vec2 v_paint;
varying vec3 v_local;
varying float v_plane;
varying float v_dark;

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
    v_plane = dot(VERTEX, NORMAL);
    v_dark = CUSTOM0.r;
}

// Metal dents and scorches: scratches through the paint, then bare metal and cracks, then soot and hot tears.
void wear_metal(int damage, vec2 uv, vec2 f, float edge, float seed, float n, bool holed, inout vec3 col, inout vec3 emission, inout float grit) {
    float scorch = smoothstep(0.66 - float(damage) * 0.1, 0.72 - float(damage) * 0.09, n);
    float s = scratches(f, seed);
    col = mix(col, max(col * 1.5, vec3(0.34, 0.33, 0.32)), s * 0.85);
    if (damage >= 2) {
        float peel = smoothstep(0.5, 0.56, fbm3(vec3(uv * 3.1, seed * 9.0)));
        col = mix(col, vec3(0.2, 0.2, 0.21), peel * 0.8);
        float broken = 1.0 - aastep(0.03 + 0.07 * noise3(vec3(uv * 9.0, seed * 5.0)), edge);
        float crack = crack_lines(uv * 2.6, seed, 0.035);
        col = mix(col, vec3(0.012, 0.011, 0.01), max(broken, crack) * 0.95);
        grit = max(grit, peel * 0.5);
    }
    col = mix(col, vec3(0.018, 0.016, 0.014), scorch * (damage == 1 ? 0.75 : 0.92));
    if (damage == 3) {
        col = mix(col, vec3(0.02, 0.018, 0.016), 0.72);
        float tear = crack_lines(uv * 3.4, seed + 0.31, 0.03);
        col = mix(col, vec3(0.006), tear);
        // Only the odd seam still glows: a burned plate, not a lava tile.
        emission += vec3(1.0, 0.3, 0.04) * tear * smoothstep(0.6, 0.72, n) * 1.5;
        grit = 1.0;
        // Shapes that can't be torn open in the mesh are burned through instead.
        if (!holed) col = mix(col, vec3(0.004), smoothstep(0.6, 0.66, fbm3(vec3(uv * 2.0, seed * 3.0 + 2.0))));
    }
}

// Concrete chips: pale pits and dust, wide cracks with broken corners, then rubble and rusty rebar.
void wear_concrete(int damage, vec2 uv, vec2 f, float edge, float seed, float n, inout vec3 col, inout float grit) {
    float chip = pits(uv * (damage == 1 ? 4.0 : 6.0), seed, damage == 1 ? 0.17 : 0.3);
    col = mix(col, col * 0.42, chip);
    col = mix(col, col * 1.18 + vec3(0.03), (1.0 - chip) * smoothstep(0.5, 0.7, n) * 0.5);
    grit = 1.0;
    if (damage >= 2) {
        float crack = crack_lines(uv * 2.2, seed, damage == 2 ? 0.05 : 0.09);
        float corner = 1.0 - aastep(0.05 + 0.13 * noise3(vec3(uv * 7.0, seed * 5.0)), edge);
        col = mix(col, col * 0.16, max(crack, corner));
    }
    if (damage == 3) {
        col = mix(col, col * 0.45, smoothstep(0.4, 0.55, n));
        float pit = pits(uv * 2.0, seed + 0.5, 0.36);
        float bar = 1.0 - aastep(0.035, abs(fract(uv.x * 3.0 + 0.5 * seed) - 0.5));
        col = mix(col, vec3(0.012), pit * 0.9);
        col = mix(col, vec3(0.2, 0.07, 0.02), pit * bar);
    }
}

// Alien alloy bruises and bleeds light: dark blooms, then glowing veins, then a burned shell over a bright wound.
void wear_alien(int damage, vec2 uv, float seed, float n, inout vec3 col, inout vec3 emission, inout float grit) {
    vec3 glow = mix(vec3(0.1, 0.95, 0.75), vec3(0.75, 0.2, 1.0), seed);
    float bruise = smoothstep(0.55 - float(damage) * 0.08, 0.68 - float(damage) * 0.08, n);
    col = mix(col, vec3(0.05, 0.02, 0.07), bruise * 0.85);
    emission += glow * bruise * 0.025 * float(damage);
    if (damage >= 2) {
        float vein = crack_lines(uv * 2.4, seed, 0.03);
        col = mix(col, vec3(0.01), vein);
        emission += glow * vein * (damage == 2 ? 0.8 : 1.6);
        grit = max(grit, 0.6);
    }
    if (damage == 3) {
        float wound = smoothstep(0.64, 0.7, fbm3(vec3(uv * 2.0, seed * 3.0 + 2.0)));
        col = mix(col, vec3(0.012, 0.01, 0.016), 0.85);
        emission += glow * wound * 0.9;
        grit = 1.0;
    }
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
    int wear = (flags >> 17) & 3;
    bool holed = ((flags >> 19) & 1) == 1;

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

    // Damage, drawn per block and per material: the worse the block, the more of it shows.
    vec3 emission = vec3(0.0);
    float grit = 0.0;
    if (damage > 0) {
        float seed = block_seed(COLOR.a, uv, v_plane);
        float n = fbm3(vec3(uv * 2.3, seed * 7.0 + v_plane * 0.7));
        if (wear == 1) wear_concrete(damage, uv, f, edge, seed, n, col, grit);
        else if (wear == 2) wear_alien(damage, uv, seed, n, col, emission, grit);
        else wear_metal(damage, uv, f, edge, seed, n, holed, col, emission, grit);
    }

    ALBEDO = col;
    ROUGHNESS = mix(painted == 1 ? 0.6 : mix(0.72, 0.38, float(metal) / 3.0), 0.95, grit);
    METALLIC = (painted == 1 ? 0.04 : float(metal) * 0.17) * (1.0 - grit);
    if (glow == 1) {
        emission += to_linear(base) * 3.2;
        ALBEDO = col * 0.4;
    }
    EMISSION = emission;
    AO = 1.0 - v_dark;
}
";
    }

    const string GlassCode = @"
shader_type spatial;
render_mode blend_mix, cull_back, depth_draw_opaque, specular_schlick_ggx;
varying flat float v_flags;
varying float v_plane;
" + Common + @"
void vertex() {
    v_flags = UV2.y;
    v_plane = dot(VERTEX, NORMAL);
}

void fragment() {
    vec2 f = fract(UV);
    float edge = min(min(f.x, 1.0 - f.x), min(f.y, 1.0 - f.y));
    float frame = 1.0 - aastep(0.035, edge);
    float fres = pow(1.0 - clamp(dot(NORMAL, VIEW), 0.0, 1.0), 3.0);
    vec3 tint = to_linear(COLOR.rgb);

    // Glass cracks from a strike point, more spokes and rings as it weakens, then whole shards are gone.
    int damage = (int(v_flags + 0.5) >> 9) & 3;
    float crazed = 0.0;
    if (damage > 0) {
        float seed = block_seed(COLOR.a, UV, v_plane);
        vec2 q = f - (hash2(floor(UV) + seed * 11.0) * 0.5 + 0.25);
        float r = length(q);
        float reach = damage == 1 ? 0.24 : damage == 2 ? 0.6 : 1.1;
        float wobble = noise3(vec3(q * 5.0, seed * 9.0)) * 0.9;
        float spokes = float(4 + damage * 3);
        float a = (atan(q.y, q.x) / 6.2832 + seed + wobble * 0.08) * spokes;
        float spoke = (1.0 - aastep(0.006, abs(fract(a) - 0.5) / spokes * 6.2832 * r)) * (1.0 - aastep(reach, r));
        float ring = 0.0;
        if (damage >= 2) ring = (1.0 - aastep(0.005, abs(fract(r * 4.0 + wobble) - 0.5) * 0.25)) * (1.0 - aastep(reach, r));
        crazed = max(spoke, ring);
        if (damage == 3) {
            // Shards: sectors between the spokes and rings, about half of them fallen out.
            float shard = hash3(vec3(floor(a), floor(r * 4.0 + wobble), seed * 31.0));
            if (shard < 0.5 && edge > 0.04 && crazed < 0.5) discard;
        }
    }

    ALBEDO = mix(mix(tint * 0.25, vec3(0.02, 0.02, 0.03), frame), vec3(0.8, 0.86, 0.9), crazed);
    ALPHA = max(mix(0.22 + fres * 0.55, 1.0, frame), crazed * 0.92);
    ROUGHNESS = mix(mix(0.04, 0.5, frame), 0.6, crazed);
    METALLIC = 0.3;
    SPECULAR = 0.9;
}
";

    const string GhostCode = @"
shader_type spatial;
render_mode unshaded, blend_mix, cull_back, depth_draw_never;
uniform vec4 tint : source_color = vec4(0.4, 1.0, 0.9, 1.0);
uniform float fill = 0.16;
void fragment() {
    vec2 f = fract(UV);
    float edge = min(min(f.x, 1.0 - f.x), min(f.y, 1.0 - f.y));
    float wire = 1.0 - smoothstep(0.02, 0.05, edge);
    float fres = pow(1.0 - clamp(dot(NORMAL, VIEW), 0.0, 1.0), 2.0);
    float scan = 0.5 + 0.5 * sin(UV.y * 18.0 - TIME * 5.0);
    ALBEDO = tint.rgb;
    ALPHA = clamp(fill + fres * fill * 1.8 + wire * 0.8 + scan * fill * 0.4, 0.0, 1.0) * tint.a;
}
";
}
