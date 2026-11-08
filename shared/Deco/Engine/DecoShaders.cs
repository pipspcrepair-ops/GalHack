namespace GalHack.Deco.Engine;

/// <summary>
/// The deco materials' shaders. A part's surface is drawn from what it is made of (the finish its
/// vertices carry, <see cref="DecoFinishes"/>), where each pixel lies on its face (UV, metres) and
/// how big that face is (UV2), so a board shows grain along its length, a cushion its weave and
/// seams, a sink its glaze, a crate's edges catch the light. Wear rides along: dust settles on what
/// faces up, paint chips at the edges, bare metal rusts, cloth fades. Every pattern fades out as it
/// shrinks below a couple of pixels, so nothing shimmers far off. Colour stays in the vertices.
/// </summary>
public static class DecoShaders
{
    /// <summary>Noise and helpers shared by the deco shaders.</summary>
    const string Common = @"
vec3 to_linear(vec3 c) { return mix(c / 12.92, pow((c + 0.055) / 1.055, vec3(2.4)), step(0.04045, c)); }
float hash3(vec3 p) { p = fract(p * 0.3183099 + 0.1); p *= 17.0; return fract(p.x * p.y * p.z * (p.x + p.y + p.z)); }
float noise3(vec3 x) {
    vec3 i = floor(x); vec3 f = fract(x); f = f * f * (3.0 - 2.0 * f);
    return mix(mix(mix(hash3(i), hash3(i + vec3(1,0,0)), f.x), mix(hash3(i + vec3(0,1,0)), hash3(i + vec3(1,1,0)), f.x), f.y),
               mix(mix(hash3(i + vec3(0,0,1)), hash3(i + vec3(1,0,1)), f.x), mix(hash3(i + vec3(0,1,1)), hash3(i + vec3(1,1,1)), f.x), f.y), f.z);
}
float fbm3(vec3 p) { return 0.55 * noise3(p) + 0.3 * noise3(p * 2.03 + 3.1) + 0.15 * noise3(p * 4.07 + 7.7); }
float aastep(float edge, float x) { float w = max(fwidth(x) * 0.7, 1e-6); return smoothstep(edge - w, edge + w, x); }
// How much of a pattern of this many cycles a metre a pixel of this size (metres) can still show:
// all of it up close, none once a cycle is under about three pixels.
float vis(float freq, float px) { return 1.0 - smoothstep(0.18, 0.4, freq * px); }
// Distance to the nearest edge of the face a pixel is on (metres), and which way that edge lies on
// the face. A face with no data (drawn by other code) has no edges.
float face_edge(vec2 uv, vec2 L, out vec2 toward) {
    toward = vec2(0.0);
    if (L.x == 0.0 && L.y == 0.0) return 1.0;
    if (L.y == 0.0) {
        float r = length(uv);
        toward = r > 1e-6 ? uv / r : vec2(0.0);
        return -L.x - r;
    }
    float eu = L.x > 0.0 ? min(uv.x, L.x - uv.x) : 1.0;
    float ev = L.y > 0.0 ? min(uv.y, L.y - uv.y) : 1.0;
    if (eu < ev) { toward = vec2(uv.x < L.x * 0.5 ? -1.0 : 1.0, 0.0); return eu; }
    toward = vec2(0.0, uv.y < L.y * 0.5 ? -1.0 : 1.0);
    return ev;
}
";

    /// <summary>
    /// Matte and metal: the opaque surfaces. <c>metal_scale</c> is 1 on the metal material and low on
    /// the matte one (bare metal drawn matte has rusted or burnt dull); triangles with no finish
    /// (other code's) keep the old flat look: <c>flat_rough</c>, <c>flat_metal</c>.
    /// </summary>
    public const string Surface = @"
shader_type spatial;
render_mode cull_back, diffuse_burley, specular_schlick_ggx;

uniform float metal_scale = 1.0;
uniform float flat_rough = 0.9;
uniform float flat_metal = 0.0;

varying vec3 v_obj;
varying vec3 v_objn;
varying flat vec2 v_ext;

const int PAINT = 1;
const int STEEL = 2;
const int POLISHED = 3;
const int WOOD = 4;
const int PLASTIC = 5;
const int FABRIC = 6;
const int LEATHER = 7;
const int PAPER = 8;
const int CERAMIC = 9;
const int RUBBER = 10;
const int STONE = 11;
const int SCREEN = 12;
const int GLASS = 13;
const int ORGANIC = 14;
const int PLAIN = 15;
" + Common + @"
void vertex() {
    v_obj = VERTEX;
    v_objn = NORMAL;
    v_ext = UV2;
}

// A few short straight scuffs in each 25 cm square, more the more worn.
float scuffs(vec2 uv, float seed, int wear, float px) {
    float v = vis(500.0, px);
    float m = 0.0;
    if (v > 0.0) {
        vec2 cell = floor(uv * 4.0);
        vec2 q0 = (fract(uv * 4.0) - 0.5) * 0.25;
        for (int i = 0; i < 3; i++) {
            if (i >= wear) break;
            float k = float(i) * 7.3 + hash3(vec3(cell, seed * 13.0)) * 17.0;
            float a = hash3(vec3(k, 1.0, 2.0)) * 3.1416;
            vec2 c = (vec2(hash3(vec3(k, 3.0, 4.0)), hash3(vec3(k, 5.0, 6.0))) - 0.5) * 0.15;
            vec2 dir = vec2(cos(a), sin(a));
            vec2 q = q0 - c;
            float len = 0.015 + 0.05 * hash3(vec3(k, 7.0, 8.0));
            float across = abs(dot(q, vec2(-dir.y, dir.x)));
            m = max(m, (1.0 - smoothstep(0.0003, 0.0009, across)) * (1.0 - smoothstep(len * 0.6, len, abs(dot(q, dir)))));
        }
    }
    return m * v;
}

// Thin lines between cells (Worley borders): cracks in old leather, glaze, rubber and grain.
float crack_lines(vec2 p, float seed, float width) {
    vec2 ip = floor(p); vec2 fp = fract(p); float d1 = 9.0; float d2 = 9.0;
    for (int y = -1; y <= 1; y++) for (int x = -1; x <= 1; x++) {
        vec2 g = vec2(float(x), float(y));
        vec2 o = vec2(hash3(vec3(ip + g, seed * 37.0)), hash3(vec3(ip + g, seed * 37.0 + 5.1)));
        float d = length(g + o - fp);
        if (d < d1) { d2 = d1; d1 = d; } else if (d < d2) { d2 = d; }
    }
    return 1.0 - aastep(width, d2 - d1);
}

void paint(vec2 uv, vec2 L, float e, int wear, float seed, vec3 wp, float px, inout vec3 col, inout float shade, inout float rough, inout vec2 slope) {
    // A coat of paint: a fine orange peel and the faint unevenness of the coat.
    float peel0 = noise3(vec3(uv * 160.0, seed * 9.0));
    shade *= 1.0 + (peel0 - 0.5) * 0.06 * vis(160.0, px);
    shade *= 1.0 + (noise3(wp * 4.0) - 0.5) * 0.07;
    rough = 0.45 + 0.12 * noise3(wp * 9.0 + 3.0) + (peel0 - 0.5) * 0.1 * vis(160.0, px);
    if (L.x > 0.45 && L.y > 0.45) {
        // A wide painted face is made of sheets: a fine seam between them, each sheet its own shade.
        vec2 n = max(vec2(1.0), floor(L / 0.9 + 0.5));
        vec2 cell = L / n;
        vec2 c = fract(uv / cell) - 0.5;
        vec2 dd = (0.5 - abs(c)) * cell;
        float d = min(dd.x, dd.y);
        float sv = vis(12.0, px);
        float seam = (1.0 - aastep(0.0012, d)) * step(0.006, e);
        shade *= 1.0 - seam * 0.5 * sv;
        shade *= 1.0 + (hash3(vec3(floor(uv / cell), seed * 3.0)) - 0.5) * 0.05 * sv;
        float k = (1.0 - smoothstep(0.0012, 0.005, d)) * step(0.006, e);
        slope += (dd.x < dd.y ? vec2(sign(c.x), 0.0) : vec2(0.0, sign(c.y))) * k * 0.6 * sv;
    }
    if (wear > 0) {
        float w = float(wear);
        // Chips along the edges, through to the primer and metal beneath (rust once it's weathered).
        float n = noise3(vec3(uv * 70.0, seed * 5.0 + 1.0));
        float reach = 0.002 + 0.005 * w;
        float chip = (1.0 - smoothstep(reach * 0.5, reach, e + (n - 0.5) * reach * 1.4)) * smoothstep(0.45, 0.6, n) * vis(70.0, px);
        vec3 under = wear >= 3 ? vec3(0.11, 0.045, 0.02) : vec3(0.15, 0.15, 0.16);
        col = mix(col, under, chip);
        rough = mix(rough, 0.75, chip);
        float s = scuffs(uv, seed, wear, px);
        col = mix(col, col * 1.3 + vec3(0.025), s * 0.7);
        rough = mix(rough, 0.85, s);
        if (wear >= 3) {
            // Paint lifting in sheets, rust under it.
            float lift = smoothstep(0.6, 0.63, fbm3(wp * 6.0 + seed * 3.0));
            col = mix(col, vec3(0.12, 0.045, 0.018) * (0.7 + 0.6 * noise3(wp * 40.0)), lift);
            rough = mix(rough, 0.95, lift);
        }
    }
}

void steel(vec2 uv, float e, int wear, float seed, vec3 wp, float low, float px, inout vec3 col, inout float shade, inout float rough, inout float metal) {
    // Brushed along its length: fine streaks across it, and the sheet's own faint cloud.
    float st = noise3(vec3(uv.x * 5.0, uv.y * 240.0, seed * 11.0));
    float sv = vis(240.0, px);
    shade *= 1.0 + (st - 0.5) * 0.2 * sv;
    shade *= 1.0 + (noise3(wp * 3.0 + 5.0) - 0.5) * 0.1;
    rough = 0.3 + 0.16 * mix(0.5, st, sv) + 0.1 * noise3(wp * 6.0);
    metal = 0.85;
    if (wear > 0) {
        float w = float(wear);
        // Handling dulls it in smears; then rust blooms in patches, worst at the edges and low down.
        rough = mix(rough, 0.62, smoothstep(0.45, 0.7, noise3(wp * 7.0 + 9.0)) * 0.6);
        if (wear >= 2) {
            float r = fbm3(wp * 4.5 + seed * 3.0) + (1.0 - smoothstep(0.0, 0.025, e)) * 0.22 + low * 0.15;
            float rust = smoothstep(0.86 - w * 0.13, 0.93 - w * 0.13, r);
            float grain = noise3(vec3(uv * 120.0, seed));
            vec3 rc = mix(vec3(0.15, 0.05, 0.018), vec3(0.32, 0.12, 0.035), mix(0.5, grain, vis(120.0, px)));
            col = mix(col, rc, rust);
            metal = mix(metal, 0.05, rust);
            rough = mix(rough, 0.95, rust);
        }
    }
}

void polished(vec2 uv, float e, int wear, float seed, vec3 wp, float px, inout vec3 col, inout float shade, inout float rough, inout float metal) {
    rough = 0.12 + 0.1 * noise3(wp * 8.0);
    shade *= 1.0 + (noise3(vec3(uv * 120.0, seed)) - 0.5) * 0.06 * vis(120.0, px);
    metal = 0.92;
    if (wear > 0) {
        float w = float(wear);
        // Tarnish in clouds; old brass and bronze go green in the corners.
        float t = smoothstep(0.62 - 0.08 * w, 0.8 - 0.08 * w, fbm3(wp * 5.0 + seed * 2.0)) * w / 3.0;
        col *= mix(1.0, 0.5, t);
        rough = mix(rough, 0.55, t);
        if (wear >= 3 && col.g > col.r * 0.4 && col.r > col.b * 2.0) {
            float p = max(smoothstep(0.55, 0.7, noise3(wp * 9.0 + 4.0)), 1.0 - smoothstep(0.0, 0.02, e));
            col = mix(col, vec3(0.09, 0.2, 0.15), p * 0.8);
            metal = mix(metal, 0.1, p);
            rough = mix(rough, 0.9, p);
        }
    }
}

void wood(vec2 uv, vec2 L, float e, bool endg, int wear, float seed, vec3 wp, float px, inout vec3 col, inout float shade, inout float rough, inout vec2 slope) {
    rough = 0.58;
    float w = float(wear);
    if (endg) {
        // A sawn end: growth rings round a heart off to one side, darker and rougher than the face.
        float size = L.y == 0.0 ? -L.x : max(abs(L.x), abs(L.y));
        vec2 heart = (L.y == 0.0 ? vec2(0.0) : abs(L) * 0.5) + vec2(0.55, -0.35) * size * (0.6 + seed);
        float r = length(uv - heart) * 90.0 + noise3(vec3(uv * 30.0, seed)) * 1.2;
        float ring = smoothstep(0.55, 0.9, abs(fract(r) - 0.5) * 2.0);
        shade *= 0.84 * (1.0 - (ring - 0.3) * 0.25 * vis(90.0, px));
        rough = 0.8;
    } else {
        // Boards across a wide face, each with its own figure and shade.
        float boards = (L.x > 0.0 && L.y > 0.45) ? max(1.0, floor(L.y / 0.16 + 0.5)) : 1.0;
        float bw = abs(L.y) / boards;
        float board = boards > 1.0 ? floor(uv.y / bw) : 0.0;
        float bs = hash3(vec3(board, seed * 7.0, 3.3));
        // Grain along the board: bands that wander, and fine pores.
        float wv = fbm3(vec3(uv.x * 1.4, uv.y * 7.0, bs * 9.0 + seed));
        float g = (uv.y + wv * 0.05 + sin(uv.x * (1.3 + bs * 2.0) + bs * 6.0) * 0.01) * 45.0;
        float late = smoothstep(0.5, 0.9, abs(fract(g) - 0.5) * 2.0);
        shade *= 1.0 - (late - 0.3) * 0.32 * vis(45.0, px);
        float pore = noise3(vec3(uv.x * 25.0, uv.y * 700.0, bs * 5.0));
        shade *= 1.0 - (smoothstep(0.6, 0.85, pore) - 0.1) * 0.14 * vis(700.0, px);
        shade *= 1.0 + (noise3(vec3(uv.x * 3.0, uv.y * 20.0, bs * 3.0 + seed)) - 0.5) * 0.12 * vis(20.0, px);
        shade *= 0.92 + 0.16 * bs;
        if (boards > 1.0) {
            // The joint between boards: a dark hairline and a slight round-over on each side.
            float sd = (fract(uv.y / bw + 0.5) - 0.5) * bw;
            float joint = 1.0 - aastep(0.0009, abs(sd));
            float jv = vis(25.0, px);
            shade *= 1.0 - joint * 0.4 * jv;
            float k = 1.0 - smoothstep(0.0, 0.004, abs(sd));
            slope.y += sign(sd) * k * 0.6 * jv;
        }
        if (wear >= 3) {
            // Weathered: the grain opens into splits along the board.
            float split = 1.0 - aastep(0.0007, abs(noise3(vec3(uv.x * 2.5, uv.y * 55.0, bs * 7.0)) - 0.5) * 0.02);
            shade *= 1.0 - split * 0.6 * vis(55.0 * 4.0, px);
            rough = 0.88;
        }
    }
    if (wear > 0) {
        // Worn edges: the finish rubbed off, paler wood showing.
        float worn = (1.0 - smoothstep(0.0, 0.004 + 0.004 * w, e)) * smoothstep(0.3, 0.6, noise3(vec3(uv * 40.0, seed)));
        col = mix(col, col * 1.25 + vec3(0.02, 0.015, 0.01), worn * 0.7 * vis(40.0, px));
        rough = mix(rough, 0.85, worn);
    }
}

void plastic(vec2 uv, int wear, float seed, vec3 wp, float px, float lum, inout vec3 col, inout float shade, inout float rough) {
    // Moulded: a fine stipple and a soft sheen.
    float st = noise3(vec3(uv * 420.0, seed));
    float sv = vis(420.0, px);
    shade *= 1.0 + (st - 0.5) * 0.06 * sv;
    rough = 0.4 + 0.08 * noise3(wp * 7.0) + (st - 0.5) * 0.2 * sv;
    shade *= 1.0 + (noise3(wp * 5.0) - 0.5) * 0.05;
    if (wear > 0) {
        float w = float(wear);
        // Scuffs: pale stress marks; old plastic goes chalky.
        float s = scuffs(uv, seed + 0.3, wear, px);
        col = mix(col, col * 1.2 + vec3(0.04), s * 0.6);
        rough = mix(rough, 0.8, s);
        if (wear >= 2) {
            float chalk = smoothstep(0.35, 0.75, fbm3(wp * 4.0 + seed)) * (w - 1.0) * 0.3;
            col = mix(col, vec3(lum) * 1.3 + vec3(0.04), chalk);
            rough = mix(rough, 0.85, chalk * 2.0);
        }
    }
}

void fabric(vec2 uv, float e, float small, vec2 toward, int wear, float seed, vec3 wp, float px, float lum, inout vec3 col, inout float shade, inout float rough, inout vec2 slope) {
    rough = 0.95;
    // The weave: threads over and under, then heathered yarn, then the cloth's cloud.
    vec2 t = uv * 2073.45; // 330 a metre
    shade *= 1.0 + sin(t.x) * sin(t.y) * 0.09 * vis(330.0, px);
    shade *= 1.0 + (noise3(vec3(uv.x * 30.0, uv.y * 330.0, seed)) - 0.5) * 0.14 * vis(330.0, px);
    shade *= 1.0 + (noise3(vec3(uv * 110.0, seed + 3.0)) - 0.5) * 0.14 * vis(110.0, px);
    shade *= 1.0 + (noise3(vec3(uv.x * 12.0, uv.y * 60.0, seed + 5.0)) - 0.5) * 0.12 * vis(60.0, px);
    shade *= 1.0 + (noise3(vec3(uv * 30.0, seed + 9.0)) - 0.5) * 0.14 * vis(30.0, px);
    shade *= 1.0 + (noise3(vec3(uv * 9.0, seed + 7.0)) - 0.5) * 0.12;
    // A stitched seam a little in from every edge, and the cloth pulled down into it.
    float inset = clamp(small * 0.1, 0.006, 0.02);
    float seam = 1.0 - aastep(0.001, abs(e - inset));
    float sv = vis(0.5 / inset, px);
    shade *= 1.0 - seam * 0.3 * sv;
    float dip = 1.0 - smoothstep(0.0, 0.005, abs(e - inset));
    slope -= toward * sign(e - inset) * dip * 0.25 * sv;
    if (wear > 0) {
        float w = float(wear);
        // Faded in patches, greyer and paler; then stains, and grime along the seams.
        float fade = smoothstep(0.35, 0.8, fbm3(wp * 3.0 + seed)) * (0.12 + 0.1 * w);
        col = mix(col, vec3(lum) * 1.15 + vec3(0.02), fade);
        if (wear >= 2) {
            float stain = smoothstep(0.6, 0.66, fbm3(wp * 5.0 + vec3(noise3(wp * 11.0) * 0.8) + seed * 5.0)) * (w - 1.0) * 0.3;
            col *= 1.0 - stain * 0.4;
            col *= 1.0 - (1.0 - smoothstep(0.0, inset * 1.6, e)) * 0.14 * (w - 1.0);
        }
    }
}

void leather(vec2 uv, float e, float small, vec2 toward, int wear, float seed, vec3 wp, float px, float lum, inout vec3 col, inout float shade, inout float rough) {
    // Pebbled grain, long soft creases where it was sat in, and a stitched seam.
    float pb = noise3(vec3(uv * 260.0, seed));
    float pv = vis(260.0, px);
    shade *= 1.0 + (pb - 0.5) * 0.12 * pv;
    rough = 0.4 + 0.16 * noise3(wp * 6.0) + (pb - 0.5) * 0.24 * pv;
    float cr = noise3(vec3(uv.x * 8.0, uv.y * 26.0, seed * 3.0));
    float crease = 1.0 - smoothstep(0.0, 0.05, abs(cr - 0.5));
    shade *= 1.0 - crease * 0.2 * vis(26.0 * 5.0, px);
    float inset = clamp(small * 0.1, 0.006, 0.016);
    float along = abs(toward.x) > 0.5 ? uv.y : uv.x;
    float dash = smoothstep(0.35, 0.5, abs(fract(along * 140.0) - 0.5) * 2.0);
    float stitch = (1.0 - aastep(0.0008, abs(e - inset))) * dash;
    shade *= 1.0 - stitch * 0.45 * vis(140.0, px);
    if (wear > 0) {
        float w = float(wear);
        // Scuffed pale at the edges and the high spots; cracked when it has dried out.
        float scuff = (1.0 - smoothstep(0.0, 0.01 + 0.012 * w, e)) * smoothstep(0.35, 0.7, noise3(vec3(uv * 50.0, seed)));
        col = mix(col, col * 1.45 + vec3(0.03), scuff * 0.25 * w);
        rough = mix(rough, 0.8, scuff);
        if (wear >= 2) {
            float cv = vis(45.0 * 3.0, px);
            col *= 1.0 - crack_lines(uv * 45.0, seed, 0.04) * 0.5 * cv * (w - 1.0) * 0.5;
            col = mix(col, vec3(lum) * 1.2, 0.1 * (w - 1.0));
        }
    }
}

void paper(vec2 uv, float e, int wear, float seed, float px, inout vec3 col, inout float shade, inout float rough) {
    rough = 0.92;
    shade *= 1.0 + (noise3(vec3(uv * 650.0, seed)) - 0.5) * 0.07 * vis(650.0, px);
    shade *= 1.0 + (noise3(vec3(uv * 45.0, seed + 2.0)) - 0.5) * 0.06 * vis(45.0, px);
    if (wear >= 2) {
        float w = float(wear);
        // Yellowed toward the edges, foxed in spots.
        float edge = 1.0 - smoothstep(0.0, 0.025, e);
        col = mix(col, col * vec3(0.92, 0.8, 0.6), edge * 0.5);
        float fox = smoothstep(0.73, 0.79, noise3(vec3(uv * 70.0, seed * 3.0))) * vis(70.0, px);
        col = mix(col, col * vec3(0.72, 0.56, 0.38), fox * (w - 1.0) * 0.5);
    }
}

void ceramic(vec2 uv, vec2 L, int wear, float seed, vec3 wp, float px, inout vec3 col, inout float shade, inout float rough, inout vec2 slope) {
    rough = 0.1;
    float w = float(wear);
    if (L.x > 0.28 && L.y > 0.28) {
        // Tiles and grout on a wide face.
        const float ts = 0.15;
        vec2 q = uv / ts;
        vec2 c = fract(q) - 0.5;
        float d = (0.5 - max(abs(c.x), abs(c.y))) * ts;
        float gv = vis(20.0, px);
        float grout = 1.0 - aastep(0.002, d);
        col = mix(col, vec3(0.3, 0.29, 0.27) * (wear >= 2 ? 0.5 : 1.0), grout * gv);
        rough = mix(rough, 0.9, grout * gv);
        shade *= 1.0 + (hash3(vec3(floor(q), seed)) - 0.5) * 0.07 * vis(1.0 / ts, px);
        float k = 1.0 - smoothstep(0.0016, 0.005, d);
        slope += (abs(c.x) > abs(c.y) ? vec2(sign(c.x), 0.0) : vec2(0.0, sign(c.y))) * k * 0.7 * gv;
    }
    // The glaze: deep and even, with the faintest ripple.
    shade *= 1.0 + (noise3(wp * 14.0) - 0.5) * 0.04;
    if (wear >= 2) {
        // Crazing in old glaze, and grime in it.
        float cv = vis(28.0 * 3.0, px);
        col *= 1.0 - crack_lines(uv * 28.0, seed, 0.025) * 0.35 * cv * (w - 1.0) * 0.5;
        rough = mix(rough, 0.4, (w - 1.0) * 0.3);
    }
}

void rubber(vec2 uv, vec2 L, bool tyre, int wear, float seed, float px, float lum, inout vec3 col, inout float shade, inout float rough) {
    rough = 0.9;
    shade *= 1.0 + (noise3(vec3(uv * 380.0, seed)) - 0.5) * 0.1 * vis(380.0, px);
    if (tyre && L.y != 0.0 && (L.x < 0.0 || L.y < 0.0)) {
        // A tyre's tread: blocks across it, staggered, and a groove round the middle.
        bool roundU = L.x < 0.0;
        float around = roundU ? uv.x : uv.y;
        float across = roundU ? uv.y : uv.x;
        float width = roundU ? L.y : L.x;
        float half_ = step(0.5, across / width);
        float gap = abs(fract(around / 0.035 + half_ * 0.5) - 0.5) * 0.035;
        float groove = 1.0 - aastep(0.005, gap);
        float middle = 1.0 - aastep(0.006, abs(across - width * 0.5));
        float shoulder = step(0.12 * width, across) * step(across, 0.88 * width);
        float tread = max(groove, middle) * shoulder;
        col *= 1.0 - tread * 0.7 * vis(28.0, px);
    }
    if (tyre && L.y == 0.0) {
        // A tyre's wall: moulded rings round the hub.
        float r = length(uv);
        float ring = 1.0 - aastep(0.0025, abs(fract(r / 0.035) - 0.5) * 0.035);
        col *= 1.0 - ring * step(-L.x * 0.62, r) * 0.35 * vis(28.0, px);
    }
    if (wear > 0) {
        float w = float(wear);
        col = mix(col, vec3(lum) * 1.5 + vec3(0.03), 0.08 * w);
        rough = mix(rough, 0.97, 0.3 * w);
        if (wear >= 3) {
            float cv = vis(60.0 * 3.0, px);
            col *= 1.0 - crack_lines(uv * 60.0, seed, 0.05) * 0.5 * cv;
        }
    }
}

void stone(vec2 uv, float e, int wear, float seed, vec3 wp, float up, float px, inout vec3 col, inout float shade, inout float rough) {
    rough = 0.94;
    // Mottle, aggregate specks and pits.
    shade *= 0.9 + 0.2 * fbm3(wp * 2.5 + seed);
    float a = hash3(vec3(floor(uv * 170.0), seed));
    shade *= 1.0 + (a > 0.86 ? 0.14 : (a < 0.1 ? -0.14 : 0.0)) * vis(170.0, px);
    shade *= 1.0 + (noise3(vec3(uv * 60.0, seed + 1.0)) - 0.5) * 0.12 * vis(60.0, px);
    float pit = smoothstep(0.78, 0.83, noise3(vec3(uv * 40.0, seed + 4.0)));
    shade *= 1.0 - pit * 0.35 * vis(40.0 * 3.0, px);
    if (wear > 0) {
        float w = float(wear);
        // Chipped arrises; lichen and weather stains on what has stood outside.
        float chip = (1.0 - smoothstep(0.0, 0.004 + 0.004 * w, e)) * smoothstep(0.4, 0.6, noise3(vec3(uv * 50.0, seed)));
        shade *= 1.0 - chip * 0.3 * vis(50.0, px);
        if (wear >= 3) {
            float lichen = smoothstep(0.6, 0.7, fbm3(wp * 5.0 + seed * 4.0)) * (0.4 + 0.6 * up);
            col = mix(col, vec3(0.16, 0.18, 0.1), lichen * 0.6);
        }
    }
}

void screen_dead(vec2 uv, int wear, float px, inout vec3 col, inout float shade, inout float rough) {
    // A dead screen: dark glass with the pixel grid faint under it.
    vec2 t = uv * 2513.27; // 400 a metre
    shade *= 1.0 - (0.5 + 0.5 * sin(t.x) * sin(t.y)) * 0.15 * vis(400.0, px);
    rough = 0.08 + 0.2 * float(wear) / 3.0;
}

void organic(vec2 uv, vec2 L, float seed, vec3 wp, float px, float lum, inout float shade, inout float rough) {
    // Grown: cells and mottle on round growths, fibres along flat ones (straw, stalks, plates of
    // shell); dark shells and resins have a wet gloss.
    float c = noise3(wp * 26.0 + seed);
    shade *= 0.84 + 0.3 * noise3(wp * 7.0 + seed * 3.0) + (c - 0.5) * 0.2 * vis(26.0, px);
    if (L.x > 0.0 && L.y > 0.0) shade *= 1.0 + (noise3(vec3(uv.x * 6.0, uv.y * 140.0, seed)) - 0.5) * 0.3 * vis(140.0, px) + (noise3(vec3(uv.x * 3.0, uv.y * 40.0, seed + 2.0)) - 0.5) * 0.2 * vis(40.0, px);
    rough = mix(0.32, 0.78, smoothstep(0.03, 0.15, lum));
}

void fragment() {
    vec3 col = to_linear(COLOR.rgb);
    int code = int(COLOR.a * 255.0 + 0.5);
    float rough = flat_rough;
    float metal = flat_metal;
    float spec = 0.5;
    float rim = 0.0;
    if (code < 128) {
        int kind = code & 15;
        int wear = (code >> 4) & 3;
        bool flag = (code & 64) != 0;
        vec2 L = v_ext;
        bool face = L.x != 0.0 || L.y != 0.0;
        vec2 uv = UV;
        if (!face) {
            // No face data: the piece's own frame, on the axis the face looks along.
            vec3 an = abs(v_objn);
            uv = an.x > an.y && an.x > an.z ? v_obj.zy : (an.y > an.z ? v_obj.xz : v_obj.xy);
        }
        vec2 toward;
        float e = face_edge(uv, L, toward);
        vec2 aL = abs(L);
        float small = !face ? 1.0 : (L.y == 0.0 ? aL.x * 2.0 : min(aL.x, aL.y));
        vec2 fw = fwidth(uv);
        float px = max(max(fw.x, fw.y), 1e-6);
        // The face's own across and up in view space, from how its coordinates run over the screen
        // (worked out here, before any branch: derivatives need every pixel of a quad to take them).
        vec3 dp1 = dFdx(VERTEX), dp2 = dFdy(VERTEX);
        vec2 du1 = dFdx(uv), du2 = dFdy(uv);
        vec3 r1 = cross(dp2, NORMAL), r2 = cross(NORMAL, dp1);
        vec3 tan_u = r1 * du1.x + r2 * du2.x;
        vec3 tan_v = r1 * du1.y + r2 * du2.y;
        float tan_s = inversesqrt(max(max(dot(tan_u, tan_u), dot(tan_v, tan_v)), 1e-24));
        vec3 inst = NODE_POSITION_WORLD;
        float seed = hash3(floor(inst * 1.7) + vec3(float(kind) * 3.1, 1.3, 7.7));
        vec3 wp = v_obj + inst * 0.31;
        vec3 wn = normalize((INV_VIEW_MATRIX * vec4(NORMAL, 0.0)).xyz);
        float up = clamp(wn.y, 0.0, 1.0);
        float low = 1.0 - smoothstep(0.0, 0.35, v_obj.y);
        float lum = dot(col, vec3(0.3, 0.59, 0.11));
        float shade = 1.0;
        vec2 slope = vec2(0.0);
        metal = 0.0;
        rough = 0.8;

        // Every part's edges are eased, so they catch the light: a hair on hard things, a round on cushions.
        bool soft = kind == FABRIC || kind == LEATHER;
        float bw = soft ? clamp(small * 0.22, 0.004, 0.05) : clamp(small * 0.1, 0.0015, 0.007);
        if (face && kind != PLAIN && kind != ORGANIC && e < bw) {
            float k = 1.0 - e / bw;
            slope += toward * k * k * vis(0.5 / bw, px);
        }

        if (kind == PAINT) paint(uv, L, e, wear, seed, wp, px, col, shade, rough, slope);
        else if (kind == STEEL) steel(uv, e, wear, seed, wp, low, px, col, shade, rough, metal);
        else if (kind == POLISHED) polished(uv, e, wear, seed, wp, px, col, shade, rough, metal);
        else if (kind == WOOD) wood(uv, L, e, flag, wear, seed, wp, px, col, shade, rough, slope);
        else if (kind == PLASTIC) plastic(uv, wear, seed, wp, px, lum, col, shade, rough);
        else if (kind == FABRIC) { fabric(uv, e, small, toward, wear, seed, wp, px, lum, col, shade, rough, slope); rim = 0.4; spec = 0.3; }
        else if (kind == LEATHER) { leather(uv, e, small, toward, wear, seed, wp, px, lum, col, shade, rough); rim = 0.08; }
        else if (kind == PAPER) { paper(uv, e, wear, seed, px, col, shade, rough); spec = 0.35; }
        else if (kind == CERAMIC) { ceramic(uv, L, wear, seed, wp, px, col, shade, rough, slope); spec = 0.7; }
        else if (kind == RUBBER) { rubber(uv, L, flag, wear, seed, px, lum, col, shade, rough); spec = 0.3; }
        else if (kind == STONE) { stone(uv, e, wear, seed, wp, up, px, col, shade, rough); spec = 0.4; }
        else if (kind == SCREEN) { screen_dead(uv, wear, px, col, shade, rough); spec = 0.6; }
        else if (kind == GLASS) { rough = 0.3 + 0.3 * noise3(wp * 9.0); shade *= 0.75 + 0.25 * noise3(wp * 20.0); spec = 0.6; }
        else if (kind == ORGANIC) organic(uv, L, seed, wp, px, lum, shade, rough);
        else rough = flat_rough;
        col *= shade;

        if (wear > 0 && kind != PLAIN) {
            float w = float(wear);
            // Dust settles on what faces up, thicker in drifts; grime gathers low down.
            float d = up * up * (0.04 + 0.07 * w) * (0.4 + 1.2 * fbm3(wp * 5.0 + 11.0));
            d += up * smoothstep(0.6, 0.8, noise3(wp * 60.0)) * 0.08 * w * vis(60.0, px);
            col = mix(col, vec3(0.42, 0.38, 0.31), clamp(d, 0.0, 0.6));
            rough = mix(rough, 0.95, clamp(d * 2.0, 0.0, 1.0));
            metal *= 1.0 - clamp(d * 2.0, 0.0, 1.0);
            if (wear >= 2) col *= 1.0 - low * 0.2 * (w - 1.0) * 0.5 * (0.5 + noise3(wp * 6.0));
        }
        metal *= metal_scale;
        NORMAL = normalize(NORMAL + (tan_u * slope.x + tan_v * slope.y) * tan_s * 0.35);
    }
    ALBEDO = col;
    ROUGHNESS = clamp(rough, 0.04, 1.0);
    METALLIC = metal;
    SPECULAR = spec;
    RIM = rim;
    RIM_TINT = 0.6;
}
";

    /// <summary>
    /// Glow: unshaded and pushed past 1 so the environment's glow catches it; a per-instance
    /// <c>energy</c> lets one screen flicker while the others hold. A lit screen shows scan lines and
    /// a darker border; a lamp's diffuser is brightest in the middle.
    /// </summary>
    public const string Glow = @"
shader_type spatial;
render_mode unshaded, cull_back;
instance uniform float energy = 1.0;
uniform float base_energy = 2.0;
" + Common + @"
void fragment() {
    vec3 c = COLOR.rgb;
    c = c * c;
    float k = 1.0;
    int code = int(COLOR.a * 255.0 + 0.5);
    vec2 L = UV2;
    if (code < 128 && (L.x != 0.0 || L.y != 0.0)) {
        vec2 toward;
        float e = face_edge(UV, L, toward);
        float px = max(length(fwidth(UV)), 1e-6);
        if ((code & 15) == 12) {
            k *= 0.5 + 0.5 * smoothstep(0.0, 0.03, e);
            k *= 1.0 - 0.14 * (0.5 + 0.5 * sin(UV.y * 1570.8)) * vis(250.0, px);
            k *= 0.92 + 0.16 * noise3(vec3(floor(UV.y * 28.0), floor(UV.x * 5.0), 1.7));
        } else {
            k *= 0.72 + 0.28 * smoothstep(0.0, 0.035, e);
        }
    }
    ALBEDO = c * base_energy * energy * k;
}";

    /// <summary>
    /// Glass: see-through, its clearness from the vertices (a deco pane's alpha carries its wear:
    /// a dirtier pane is less clear). Smudges, a dark gasket round each pane, dirt that gathers low
    /// and in the corners with age.
    /// </summary>
    public const string Glass = @"
shader_type spatial;
render_mode blend_mix, cull_disabled, depth_draw_opaque, specular_schlick_ggx;
varying vec3 v_obj;
" + Common + @"
void vertex() { v_obj = VERTEX; }
void fragment() {
    vec3 tint = to_linear(COLOR.rgb);
    float a = COLOR.a;
    bool deco = a > 0.34 && a < 0.5;
    float wear = deco ? clamp(floor((a - 0.36) / 0.04 + 0.5), 0.0, 3.0) : 0.0;
    float alpha = deco ? 0.38 : a;
    vec2 L = UV2;
    vec2 toward;
    float e = face_edge(UV, L, toward);
    bool face = L.x != 0.0 || L.y != 0.0;
    float px = max(length(fwidth(face ? UV : v_obj.xy)), 1e-6);
    float fres = pow(1.0 - clamp(abs(dot(NORMAL, VIEW)), 0.0, 1.0), 3.0);
    float smudge = smoothstep(0.55, 0.85, noise3(vec3((face ? UV : v_obj.xy) * 16.0, 3.0))) * vis(16.0, px);
    float gasket = deco && face ? (1.0 - aastep(0.005, e)) * vis(1.0 / 0.008, px) : 0.0;
    float dirt = wear * (0.06 + 0.08 * fbm3(v_obj * 6.0)) + (1.0 - smoothstep(0.0, 0.05, e)) * wear * 0.08 + (1.0 - smoothstep(0.0, 0.5, v_obj.y)) * wear * 0.05;
    dirt = clamp(dirt, 0.0, 0.7);
    ALBEDO = mix(mix(tint, vec3(0.3, 0.27, 0.22), dirt), vec3(0.02), gasket);
    ALPHA = clamp(alpha + fres * 0.35 + dirt * 0.7 + gasket, 0.0, 1.0);
    ROUGHNESS = mix(0.06 + 0.16 * smudge, 0.75, dirt);
    METALLIC = 0.2;
    SPECULAR = 0.7;
}";

    /// <summary>The Hollow Bloom's flesh: wet, faintly glowing, mottled with veins.</summary>
    public const string Flesh = @"
shader_type spatial;
render_mode cull_back;
varying vec3 v_obj;
" + Common + @"
void vertex() { v_obj = VERTEX; }
void fragment() {
    vec3 c = COLOR.rgb * COLOR.rgb;
    float m = noise3(v_obj * 18.0 + NODE_POSITION_WORLD * 0.3);
    float vein = 1.0 - smoothstep(0.0, 0.06, abs(noise3(v_obj * 7.0 + 2.0) - 0.5));
    c *= 0.85 + 0.3 * m;
    c = mix(c, c * vec3(0.55, 0.3, 0.45), vein * 0.5);
    ALBEDO = c;
    EMISSION = c * 0.35;
    ROUGHNESS = 0.3 + 0.2 * m;
    SPECULAR = 0.7;
}";
}
