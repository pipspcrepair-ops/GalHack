using Godot;

namespace Slice.Player;

/// <summary>
/// A worn, mottled finish for held items (the survival tool now, every gun later): a soft
/// two-tone mottle, darker grime in the creases and near edges, lighter worn edges with a touch
/// of bare metal, faint scratches, and a little roughness variation. All of it is noise worked
/// out in the shader from the mesh's own local position, so there is no texture, no tiling, and
/// nothing swims when the item moves. Takes a base colour and a wear amount (0 clean, 1 beaten up).
/// </summary>
public static class HeldFinish
{
    const string Code = @"
shader_type spatial;

uniform vec3 base_color : source_color = vec3(0.8);
uniform float wear : hint_range(0.0, 1.0) = 0.5;
uniform float base_rough = 0.6;
uniform float base_metal = 0.1;
uniform vec3 half_size = vec3(0.05);
uniform int shape = 0;            // 0 box, 1 cylinder along Y
uniform float seed = 0.0;

varying vec3 lp;

void vertex() { lp = VERTEX; }

float h31(vec3 p) {
    p = fract(p * 0.3183099 + vec3(0.71, 0.113, 0.419));
    p *= 17.0;
    return fract(p.x * p.y * p.z * (p.x + p.y + p.z));
}

float vnoise(vec3 p) {
    vec3 i = floor(p);
    vec3 f = fract(p);
    f = f * f * (3.0 - 2.0 * f);
    return mix(mix(mix(h31(i), h31(i + vec3(1, 0, 0)), f.x),
                   mix(h31(i + vec3(0, 1, 0)), h31(i + vec3(1, 1, 0)), f.x), f.y),
               mix(mix(h31(i + vec3(0, 0, 1)), h31(i + vec3(1, 0, 1)), f.x),
                   mix(h31(i + vec3(0, 1, 1)), h31(i + vec3(1, 1, 1)), f.x), f.y), f.z);
}

// Metres from the nearest edge or rim of the shape's block.
float edge_distance(vec3 p) {
    if (shape == 1) {
        float rd = half_size.x - length(p.xz);
        float hd = half_size.y - abs(p.y);
        return max(rd, hd);
    }
    vec3 d = max(half_size - abs(p), vec3(0.0));
    float hi = max(d.x, max(d.y, d.z));
    float lo = min(d.x, min(d.y, d.z));
    return d.x + d.y + d.z - hi - lo;   // the middle one: zero on a face's edge
}

void fragment() {
    vec3 p = lp + vec3(seed * 7.13, seed * 3.71, seed * 5.29);
    float mott = vnoise(p * 28.0) * 0.6 + vnoise(p * 71.0) * 0.4;      // soft blotches
    float speck = vnoise(p * 150.0);
    float ed = edge_distance(lp);

    vec3 col = base_color;
    // Mottle: a gentle lightness shift and a slight warm/cool drift.
    col *= 1.0 + (mott - 0.5) * 0.55 * wear;
    col = mix(col, col * vec3(1.06, 1.0, 0.92), smoothstep(0.45, 0.75, vnoise(p * 17.0 + 4.0)) * wear);
    col *= 1.0 + (speck - 0.5) * 0.12 * wear;

    // Grime gathers near edges and in the creases.
    float crease = 1.0 - smoothstep(0.0, 0.028, ed);
    float grime = crease * (0.35 + 0.65 * vnoise(p * 55.0 + 9.0));
    col *= 1.0 - 0.38 * grime * wear;

    // Worn edges: lighter, patchy, and shinier.
    float bare = smoothstep(0.35, 0.62, vnoise(p * 60.0 + 2.0));
    float rub = (1.0 - smoothstep(0.0015, 0.0075, ed)) * bare;
    col = mix(col, col * 1.35 + vec3(0.07), rub * wear);

    // Faint scratches: thin lines in three directions, only in some places.
    float s1 = 1.0 - smoothstep(0.0, 0.035, abs(vnoise(p * vec3(260.0, 14.0, 22.0) + 1.0) - 0.5));
    float s2 = 1.0 - smoothstep(0.0, 0.035, abs(vnoise(p * vec3(18.0, 24.0, 280.0) + 6.0) - 0.5));
    float s3 = 1.0 - smoothstep(0.0, 0.03, abs(vnoise(p * vec3(20.0, 270.0, 16.0) + 3.0) - 0.5));
    float spot = smoothstep(0.45, 0.7, vnoise(p * 9.0 + 5.0));
    float scratch = max(s1, max(s2, s3)) * spot;
    col = mix(col, col * 1.3 + vec3(0.05), scratch * 0.55 * wear);

    ALBEDO = clamp(col, 0.0, 1.0);
    ROUGHNESS = clamp(base_rough + (mott - 0.5) * 0.35 * wear + grime * 0.12 * wear - rub * 0.2 * wear - scratch * 0.12 * wear, 0.05, 1.0);
    METALLIC = clamp(base_metal + rub * 0.35 * wear, 0.0, 1.0);
}
";

    const string EmitterCode = @"
shader_type spatial;
render_mode blend_mix, cull_back;

uniform vec3 tint : source_color = vec3(0.18, 0.77, 0.71);
uniform vec3 half_size = vec3(0.05);
uniform int shape = 0;            // 0 box lens (long axis Z), 1 round lens (disc facing Y)
uniform float glow = 1.0;

varying vec3 lp;
void vertex() { lp = VERTEX; }

void fragment() {
    vec2 q = lp.xz / half_size.xz;
    float d = shape == 1 ? length(q) : max(abs(q.x), abs(q.y) * 0.8);
    float centre = clamp(1.0 - d, 0.0, 1.0);
    // Faint moulded ribs across the lens, and a fine cell grid.
    float ribs = shape == 1 ? abs(fract(length(lp.xz) * 170.0) - 0.5) : abs(fract(lp.z * 120.0) - 0.5);
    float rib = 1.0 - smoothstep(0.0, 0.12, ribs);
    float cell = shape == 1 ? abs(fract(atan(lp.z, lp.x) * 1.9099) - 0.5) : abs(fract(lp.x * 400.0) - 0.5);
    float grid = (1.0 - smoothstep(0.0, 0.07, cell)) * 0.5;
    float fres = pow(1.0 - clamp(dot(NORMAL, VIEW), 0.0, 1.0), 3.0);
    float edge = smoothstep(0.0, 0.25, 1.0 - d);   // dimmer toward the bezel
    vec3 e = tint * (0.45 + 1.6 * pow(centre, 1.4)) * edge;
    e *= 1.0 - 0.28 * rib - 0.18 * grid;
    e += mix(tint, vec3(1.0), 0.6) * pow(centre, 4.0) * 0.5;      // hot core
    e += mix(tint, vec3(1.0), 0.7) * fres * 0.5;                  // soft rim highlight
    ALBEDO = tint * 0.3;
    EMISSION = e * glow;
    ROUGHNESS = 0.12;
    SPECULAR = 0.9;
    ALPHA = clamp(0.78 + 0.2 * fres, 0.0, 1.0);
}
";

    static Shader _shader, _emitterShader;

    /// <summary>A moulded translucent plastic emitter lens (a glowing panel or tip), brightest at its centre; tint it per mode.</summary>
    public static ShaderMaterial Emitter(Mesh mesh, Color tint)
    {
        _emitterShader ??= new Shader { Code = EmitterCode };
        Vector3 half = new(0.05f, 0.01f, 0.05f);
        int shape = 0;
        switch (mesh)
        {
            case BoxMesh b: half = b.Size * 0.5f; break;
            case CylinderMesh c: shape = 1; half = new Vector3(c.TopRadius, c.Height * 0.5f, c.TopRadius); break;
        }
        var m = new ShaderMaterial { Shader = _emitterShader };
        m.SetShaderParameter("tint", tint);
        m.SetShaderParameter("half_size", half);
        m.SetShaderParameter("shape", shape);
        return m;
    }

    /// <summary>A worn material for a box or a cylinder mesh (its edges are the ones that wear).</summary>
    public static ShaderMaterial Make(Mesh mesh, Color baseColor, float wear = 0.6f, float roughness = 0.6f, float metallic = 0.1f, float seed = 0f)
    {
        _shader ??= new Shader { Code = Code };
        Vector3 half = new(0.05f, 0.05f, 0.05f);
        int shape = 0;
        switch (mesh)
        {
            case BoxMesh b: half = b.Size * 0.5f; break;
            case CylinderMesh c:
                shape = 1;
                float r = Mathf.Max(c.TopRadius, c.BottomRadius);
                half = new Vector3(r, c.Height * 0.5f, r);
                break;
        }
        var m = new ShaderMaterial { Shader = _shader };
        m.SetShaderParameter("base_color", baseColor);
        m.SetShaderParameter("wear", wear);
        m.SetShaderParameter("base_rough", roughness);
        m.SetShaderParameter("base_metal", metallic);
        m.SetShaderParameter("half_size", half);
        m.SetShaderParameter("shape", shape);
        m.SetShaderParameter("seed", seed);
        return m;
    }
}
