using System.Collections.Generic;
using System.Linq;
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
            // Each face's finish code and the place's seed (Surfaces.Code), for the place shader; a buffer put
            // together by other code without them draws plain.
            bool coded = buf.Codes.Count == n * 2;
            var codes = coded ? new Vector2[n] : null;
            for (int i = 0; i < n; i++)
            {
                verts[i] = new Vector3(buf.Positions[i * 3], buf.Positions[i * 3 + 1], buf.Positions[i * 3 + 2]);
                normals[i] = new Vector3(buf.Normals[i * 3], buf.Normals[i * 3 + 1], buf.Normals[i * 3 + 2]);
                colors[i] = new Color(buf.Colors[i * 4], buf.Colors[i * 4 + 1], buf.Colors[i * 4 + 2], buf.Colors[i * 4 + 3]);
                if (coded) codes[i] = new Vector2(buf.Codes[i * 2], buf.Codes[i * 2 + 1]);
            }
            var arrays = new Godot.Collections.Array();
            arrays.Resize((int)Mesh.ArrayType.Max);
            arrays[(int)Mesh.ArrayType.Vertex] = verts;
            arrays[(int)Mesh.ArrayType.Normal] = normals;
            arrays[(int)Mesh.ArrayType.Color] = colors;
            if (coded) arrays[(int)Mesh.ArrayType.TexUV] = codes;
            arrays[(int)Mesh.ArrayType.Index] = buf.Indices.ToArray();
            // Uncompressed: the codes are integers up to 2^24 that must arrive exact.
            mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays);
            mesh.SurfaceSetMaterial(mesh.GetSurfaceCount() - 1, materials[s]);
        }
        return mesh;
    }

    /// <summary>How far over a wet cell's floor its water stands: just over a floor's finish.</summary>
    public const float WaterDepth = BlockInfo.FinishTop + 0.03f;

    /// <summary>
    /// The water a place holds by design (<see cref="LayoutExtras.Water"/>), over the floors still under it; null
    /// where it holds none.
    /// </summary>
    public static MeshInstance3D Water(PoiInstance inst)
    {
        if (inst.Layout.Extras.Water.Count == 0) return null;
        return Water(DrawnWater(inst), StillWater());
    }

    /// <summary>
    /// The wet cells whose water is drawn: only over a whole block of floor or the ground (never over a
    /// stair, a ramp, a drop or an opening, where the sheet would hang in the air), and not in a block.
    /// </summary>
    public static IEnumerable<Int3> DrawnWater(PoiInstance inst)
    {
        var nav = inst.Nav();
        return inst.Layout.Extras.Water.Where(c => nav.Solid(c + Int3.Down) && inst.Blocks.Get(c + Int3.Down).Type is var under && (under == BlockType.Air || BlockInfo.IsFullCube(under))
                                                  && !BlockInfo.IsFullCube(inst.Blocks.Get(c).Type));
    }

    /// <summary>
    /// Standing water as a surface a hand deep over each wet cell's floor: a whole cell where it joins another (a
    /// channel, a basin), a puddle's patch where it lies alone. A surface only, never a block: nothing collides with
    /// it and it hides no face, so whatever stands in it stands up out of it. Null where there is none.
    /// </summary>
    public static MeshInstance3D Water(IEnumerable<Int3> cells, Material material)
    {
        var wet = new HashSet<Int3>(cells);
        if (wet.Count == 0) return null;
        var st = new SurfaceTool();
        st.Begin(Mesh.PrimitiveType.Triangles);
        st.SetNormal(Vector3.Up);
        st.SetColor(new Color(1, 1, 1, 1)); // the water shader reads its alpha as the shore: all of it is water
        foreach (Int3 c in wet)
        {
            float x0 = c.X, z0 = c.Z, x1 = c.X + 1, z1 = c.Z + 1, y = c.Y + WaterDepth;
            if (!Dirs.Horizontal.Any(d => wet.Contains(c + d.Vec())))
            {
                uint h = Hash.Cell(c, 57);
                x0 += 0.05f + h % 97 / 97f * 0.2f; z0 += 0.05f + h / 97 % 89 / 89f * 0.2f;
                x1 -= 0.05f + h / 8633 % 83 / 83f * 0.2f; z1 -= 0.05f + h / 688_939 % 79 / 79f * 0.2f;
            }
            Vector3 a = new(x0, y, z0), b = new(x1, y, z0), d = new(x1, y, z1), e = new(x0, y, z1);
            st.AddVertex(a); st.AddVertex(b); st.AddVertex(d);
            st.AddVertex(a); st.AddVertex(d); st.AddVertex(e);
        }
        st.Index();
        return new MeshInstance3D { Name = "Water", Mesh = st.Commit(), MaterialOverride = material, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off };
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
/// The five block materials. Colour lives in the vertices, so a whole place is five materials. Matte
/// and metal are one place shader (<see cref="PlaceShaders.Surface"/>): each face's finish code draws
/// its surface (brick by its bond, render, boards, stone, poured concrete, tile, roofing, plate, the
/// alien materials) and the decay's wear over it, fading to the plain colour with distance. Glass is
/// framed. Glow is an unshaded shader that pushes colour past 1 so the environment's glow picks it up;
/// its energy uniform is what flickers or pulses per state. Flesh is the Hollow Bloom: lit like
/// anything else, wet, and giving off a little light of its own, so it reads as sick pink even in the dark.
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

    // One shader each, shared by every place: a place's materials differ only in their uniforms, so the
    // big surface shader is compiled once, not once a place.
    static Shader _surface, _glass, _glow, _flesh;
    static readonly System.Collections.Generic.List<System.WeakReference<ShaderMaterial>> Drawn = new();
    static bool _detail = true;

    /// <summary>
    /// Every place's surfaces drawn (true) or left as their plain colour (false), at once: the cost
    /// check measures the same frame both ways. Places made while it is off are made off.
    /// </summary>
    public static void Detail(bool on)
    {
        _detail = on;
        lock (Drawn)
        {
            Drawn.RemoveAll(w => !w.TryGetTarget(out _));
            foreach (var w in Drawn)
                if (w.TryGetTarget(out ShaderMaterial m)) m.SetShaderParameter("detail", on ? 1f : 0f);
        }
    }

    static ShaderMaterial Made(Shader shader)
    {
        var m = new ShaderMaterial { Shader = shader };
        m.SetShaderParameter("detail", _detail ? 1f : 0f);
        lock (Drawn) Drawn.Add(new System.WeakReference<ShaderMaterial>(m));
        return m;
    }

    public BlockMaterials(float glowEnergy = 2.2f)
    {
        _surface ??= new Shader { Code = PlaceShaders.Surface };
        _glass ??= new Shader { Code = PlaceShaders.Glass };
        _glow ??= new Shader { Code = GlowShader };
        _flesh ??= new Shader { Code = FleshShader };
        var matte = Made(_surface);
        matte.SetShaderParameter("metal_scale", 0f);
        matte.SetShaderParameter("flat_rough", 0.92f);
        matte.SetShaderParameter("flat_metal", 0f);
        var metal = Made(_surface);
        metal.SetShaderParameter("metal_scale", 1f);
        metal.SetShaderParameter("flat_rough", 0.55f);
        metal.SetShaderParameter("flat_metal", 0.35f);
        Glow = new ShaderMaterial { Shader = _glow };
        Glow.SetShaderParameter("energy", glowEnergy);
        var glass = Made(_glass);
        var flesh = new ShaderMaterial { Shader = _flesh };
        BySurface = new Material[] { matte, metal, Glow, glass, flesh };
    }
}

/// <summary>
/// The place shaders. A face's finish code (UV.x, see <see cref="Surfaces.Code"/>) says what it is
/// finished in and what the years have done to it; its position on its face is worked out from the
/// object position and the normal, in metres, so courses and boards run on across blocks and merged
/// faces alike. Every pattern fades to its colour as it shrinks below a few pixels (the deco shaders'
/// rule), so nothing shimmers far off and a far mesh in the same colour meets it without a jump.
/// </summary>
public static class PlaceShaders
{
    const string Common = """
vec3 to_linear(vec3 c) { return mix(c / 12.92, pow((c + 0.055) / 1.055, vec3(2.4)), step(0.04045, c)); }
float hash3(vec3 p) { p = fract(p * 0.3183099 + 0.1); p *= 17.0; return fract(p.x * p.y * p.z * (p.x + p.y + p.z)); }
float noise3(vec3 x) {
    vec3 i = floor(x); vec3 f = fract(x); f = f * f * (3.0 - 2.0 * f);
    return mix(mix(mix(hash3(i), hash3(i + vec3(1,0,0)), f.x), mix(hash3(i + vec3(0,1,0)), hash3(i + vec3(1,1,0)), f.x), f.y),
               mix(mix(hash3(i + vec3(0,0,1)), hash3(i + vec3(1,0,1)), f.x), mix(hash3(i + vec3(0,1,1)), hash3(i + vec3(1,1,1)), f.x), f.y), f.z);
}
float fbm3(vec3 p) { return 0.55 * noise3(p) + 0.3 * noise3(p * 2.03 + 3.1) + 0.15 * noise3(p * 4.07 + 7.7); }
float aastep(float edge, float x) { float w = max(fwidth(x) * 0.7, 1e-6); return smoothstep(edge - w, edge + w, x); }
// How much of a pattern of this many cycles a metre a pixel this big (metres) can still show: all of it
// up close, none once a cycle is under about three pixels.
float vis(float freq, float px) { return 1.0 - smoothstep(0.18, 0.4, freq * px); }
float luma(vec3 c) { return dot(c, vec3(0.3, 0.59, 0.11)); }
""";

    /// <summary>Matte and metal: every opaque face of a place.</summary>
    public const string Surface = """
shader_type spatial;
render_mode cull_back, diffuse_burley, specular_schlick_ggx;

uniform float metal_scale = 0.0;
uniform float flat_rough = 0.92;
uniform float flat_metal = 0.0;
uniform float detail = 1.0;

varying flat vec2 v_code;
varying vec3 v_obj;
varying vec3 v_objn;

""" + Common + """

void vertex() {
    v_code = UV;
    v_obj = VERTEX;
    v_objn = NORMAL;
}

// Worley cells: the nearest and second-nearest distances and the nearest cell's own number.
vec3 worley(vec2 p, float seed) {
    vec2 ip = floor(p); vec2 fp = fract(p); float d1 = 9.0; float d2 = 9.0; float id = 0.0;
    for (int y = -1; y <= 1; y++) for (int x = -1; x <= 1; x++) {
        vec2 g = vec2(float(x), float(y));
        vec2 o = vec2(hash3(vec3(ip + g, seed)), hash3(vec3(ip + g, seed + 17.3)));
        float d = length(g + o - fp);
        if (d < d1) { d2 = d1; d1 = d; id = hash3(vec3(ip + g, seed + 5.1)); } else if (d < d2) { d2 = d; }
    }
    return vec3(d1, d2, id);
}
// Thin lines between cells: cracks that branch and meet.
float cracks(vec2 p, float seed, float width) { vec3 w = worley(p, seed); return 1.0 - aastep(width, w.y - w.x); }

// A unit of a size leaning out toward its edges over its last w: its pillowed or bevelled face.
vec2 bevel(vec2 q, vec2 size, float w) {
    vec2 d = min(q, size - q);
    vec2 s = vec2(q.x < size.x * 0.5 ? -1.0 : 1.0, q.y < size.y * 0.5 ? -1.0 : 1.0);
    vec2 k = 1.0 - smoothstep(vec2(0.0), vec2(w), d);
    return d.x < d.y ? vec2(s.x * k.x, 0.0) : vec2(0.0, s.y * k.y);
}
// A running bond: which unit (column, row), where in it (metres), each odd row shifted a share of a unit.
vec2 bond_unit(vec2 uv, vec2 size, float off, out vec2 id) {
    float row = floor(uv.y / size.y);
    float x = uv.x / size.x + mod(row, 2.0) * off;
    id = vec2(floor(x), row);
    return vec2(fract(x) * size.x, uv.y - row * size.y);
}
// Units along a row whose joints wander (rubble courses, flags, shakes): the unit's number and where in it.
vec2 wander_unit(vec2 uv, float L, float H, float amp, float seed, out vec2 id, out vec2 size) {
    float row = floor(uv.y / H);
    float x = uv.x / L + hash3(vec3(row, seed, 3.0));
    float i = floor(x); float t = x - i;
    float jl = (hash3(vec3(i, row, seed)) - 0.5) * amp, jr = (hash3(vec3(i + 1.0, row, seed)) - 0.5) * amp;
    float lo = jl; float hi = 1.0 + jr; float k = i;
    if (t < jl) { lo = (hash3(vec3(i - 1.0, row, seed)) - 0.5) * amp - 1.0; hi = jl; k = i - 1.0; }
    else if (t > 1.0 + jr) { lo = 1.0 + jr; hi = 2.0 + (hash3(vec3(i + 2.0, row, seed)) - 0.5) * amp; k = i + 1.0; }
    id = vec2(k, row);
    size = vec2((hi - lo) * L, H);
    return vec2((t - lo) * L, uv.y - row * H);
}
float edge_of(vec2 q, vec2 size) { vec2 d = min(q, size - q); return min(d.x, d.y); }

// ---- Finishes ---------------------------------------------------------------------------------------
// Each takes the face's position (uv, metres: across and up), the pixel's size (px, metres) and the
// block's seed, and works on the colour (linear), roughness/metal/specular (S.yzw) and the normal's lean
// across the face (slope). J.x comes back as the joint (mortar, grout, gaps), where dirt and moss gather.

void f_brick(vec2 uv, int bond, float px, float seed, inout vec3 col, inout vec4 S, inout vec2 slope, inout vec2 J) {
    float ch = 0.075;
    float row = floor(uv.y / ch);
    float odd = mod(row, 2.0);
    vec2 size = vec2(0.225, ch); vec2 q; vec2 id;
    if (bond == 1) {
        // Flemish: header and stretcher by turns along each course, each header over a stretcher's middle.
        float P = 0.3375;
        float x = uv.x + odd * 0.16875;
        float k = floor(x / P); float t = x - k * P;
        bool header = t >= 0.225;
        size = vec2(header ? 0.1125 : 0.225, ch);
        q = vec2(header ? t - 0.225 : t, uv.y - row * ch);
        id = vec2(k * 2.0 + (header ? 1.0 : 0.0), row);
    } else if (bond == 2) {
        // English: a course of stretchers, a course of headers.
        size = vec2(odd > 0.5 ? 0.1125 : 0.225, ch);
        q = bond_unit(uv, size, odd > 0.5 ? 0.25 : 0.0, id);
    } else q = bond_unit(uv, size, bond == 3 ? 0.0 : 0.5, id);
    float v = vis(1.0 / ch, px);
    float h = hash3(vec3(id, seed * 3.1 + 1.7));
    float mortar = (1.0 - aastep(0.005, edge_of(q, size))) * v;
    float tone = 0.84 + 0.28 * h - (h > 0.94 ? 0.3 : 0.0);
    vec3 bc = col * mix(1.0, tone, v);
    bc *= 1.0 + (noise3(vec3(uv * 90.0, seed)) - 0.5) * 0.18 * vis(90.0, px);
    bc *= mix(vec3(1.0), vec3(1.08, 0.97, 0.9), (hash3(vec3(id, 9.1)) - 0.5) * v);
    vec3 mc = mix(col * 1.3, vec3(0.3, 0.29, 0.26), 0.6);
    col = mix(bc, mc, mortar);
    S.y = mix(0.88, 0.96, mortar);
    slope += bevel(q, size, 0.012) * 0.55 * v;
    J.x = mortar;
}

void f_render(vec3 wp, float px, float seed, bool painted, inout vec3 col, inout vec4 S, inout vec2 slope) {
    // Trowelled: soft swirls of the float, then the sand in it.
    float t = fbm3(wp * 3.2 + seed);
    col *= 1.0 + (t - 0.5) * 0.12 + (noise3(wp * 150.0) - 0.5) * 0.14 * vis(150.0, px);
    col *= 1.0 + (fbm3(wp * 0.35 + seed * 2.0) - 0.5) * 0.16;
    slope += (vec2(noise3(wp * 55.0 + 3.0), noise3(wp * 55.0 + 9.0)) - 0.5) * 0.7 * vis(55.0, px);
    S.y = painted ? 0.8 : 0.93;
}

void f_plaster(vec3 wp, float px, float seed, float above, bool indoors, inout vec3 col, inout vec4 S, inout vec2 slope) {
    col *= 1.0 + (fbm3(wp * 2.0 + seed) - 0.5) * 0.05 + (noise3(wp * 220.0) - 0.5) * 0.05 * vis(220.0, px);
    S.y = 0.82;
    if (indoors && above < 0.14) {
        // A skirting board along the floor: darker paint, its top edge rounded over.
        float k = 1.0 - aastep(0.12, above);
        col = mix(col, col * 0.62, k);
        float top = 1.0 - smoothstep(0.0, 0.012, abs(above - 0.11));
        slope.y += top * 0.8 * vis(40.0, px);
        S.y = mix(S.y, 0.55, k);
    }
}

void f_weatherboard(vec2 uv, int kind, float px, float seed, bool painted, inout vec3 col, inout vec4 S, inout vec2 slope, inout vec2 J) {
    if (kind == 2) {
        // Board and batten: wide boards up the wall, a narrow batten over each joint.
        float w = 0.3; float x = uv.x / w; float b = fract(x) * w; float id = floor(x + 0.5);
        float db = min(b, w - b);
        float v = vis(1.0 / w, px);
        float bat = 1.0 - aastep(0.028, db);
        col *= mix(1.0, 0.93 + 0.12 * hash3(vec3(floor(x), 2.0, seed)), v);
        col *= 1.0 - (1.0 - aastep(0.004, abs(db - 0.03))) * 0.45 * v;
        slope.x += bat * (b < w * 0.5 ? 1.0 : -1.0) * 0.25 * v;
        J.x = (1.0 - aastep(0.003, abs(db - 0.03))) * v;
        col *= 1.0 + (noise3(vec3(uv.x * 140.0, uv.y * 3.0, seed + id)) - 0.5) * (painted ? 0.05 : 0.2) * vis(140.0, px);
    } else {
        // Lap boards (or shiplap): each board a course, its lower edge lapping the one below in a shadow line.
        float bh = kind == 1 ? 0.15 : 0.18;
        float r = floor(uv.y / bh); float t = fract(uv.y / bh);
        float v = vis(1.0 / bh, px);
        float lip = aastep(0.06, t);
        col *= mix(1.0, mix(kind == 1 ? 0.6 : 0.42, 1.0, lip) * (0.9 + 0.12 * t), v);
        slope.y -= (kind == 0 ? 0.3 : 0.0) * v;
        J.x = (1.0 - lip) * v;
        float L = 2.4; float x = uv.x / L + hash3(vec3(r, seed, 4.0)); float fx = fract(x);
        col *= 1.0 - (1.0 - aastep(0.003, min(fx, 1.0 - fx) * L)) * 0.45 * vis(20.0, px);
        col *= mix(1.0, 0.95 + 0.1 * hash3(vec3(r, floor(x), seed)), v);
        col *= 1.0 + (noise3(vec3(uv.x * 3.0, uv.y * 140.0, seed + r)) - 0.5) * (painted ? 0.05 : 0.22) * vis(140.0, px);
    }
    S.y = painted ? 0.72 : 0.86;
}

void f_logs(vec2 uv, float px, float seed, inout vec3 col, inout vec4 S, inout vec2 slope, inout vec2 J) {
    float lh = 0.25; float r = floor(uv.y / lh); float t = fract(uv.y / lh);
    float c = (t - 0.5) * 2.0;
    float v = vis(1.0 / lh, px);
    float rnd = sqrt(max(1.0 - c * c, 0.0));
    col *= mix(1.0, 0.5 + 0.5 * rnd, v);
    slope.y += c * 0.9 * v;
    float chink = 1.0 - aastep(0.03, min(t, 1.0 - t) * lh);
    col = mix(col, mix(col * 0.6, vec3(0.2, 0.18, 0.14), 0.5), chink * v);
    J.x = chink * v;
    col *= mix(1.0, 0.86 + 0.24 * hash3(vec3(r, seed, 1.0)), v);
    col *= 1.0 + (noise3(vec3(uv.x * 2.5, uv.y * 70.0, seed + r * 3.0)) - 0.5) * 0.26 * vis(70.0, px);
    // Knots: a dark ring here and there along the log.
    vec3 k = worley(vec2(uv.x * 1.6, r * 5.0 + t * 0.5), seed + r);
    col *= 1.0 - (1.0 - smoothstep(0.03, 0.06, k.x)) * step(0.7, k.z) * 0.45 * vis(30.0, px);
    S.y = 0.84;
}

void f_stone(vec2 uv, int kind, float px, float seed, inout vec3 col, inout vec4 S, inout vec2 slope, inout vec2 J) {
    float H = kind == 2 ? 0.6 : kind == 1 ? 0.3 : 0.26;
    float L = kind == 2 ? 1.1 : kind == 1 ? 0.6 : 0.42;
    vec2 id; vec2 size;
    vec2 q = wander_unit(uv, L, H, kind == 0 ? 0.6 : 0.0, seed, id, size);
    float v = vis(1.0 / H, px);
    float e = edge_of(q, size);
    float jw = kind == 1 ? 0.004 : 0.011;
    float mortar = (1.0 - aastep(jw, e)) * v;
    float h = hash3(vec3(id, seed + 2.3));
    vec3 sc = col * mix(1.0, 0.8 + 0.36 * h, v) * mix(vec3(1.0), vec3(1.05, 1.0, 0.93), (hash3(vec3(id, 4.4)) - 0.5) * v);
    sc *= 1.0 + (fbm3(vec3(uv * 9.0, seed + h * 7.0)) - 0.5) * 0.24 * vis(9.0, px);
    sc *= 1.0 + (noise3(vec3(uv * 80.0, seed)) - 0.5) * 0.14 * vis(80.0, px);
    sc *= mix(1.0, 0.86 + 0.14 * smoothstep(0.0, 0.06, e), v * (kind == 1 ? 0.4 : 1.0));
    col = mix(sc, mix(col * 1.2, vec3(0.28, 0.27, 0.24), 0.55) * 0.9, mortar);
    slope += bevel(q, size, kind == 1 ? 0.01 : 0.06) * (kind == 1 ? 0.3 : 0.6) * v;
    S.y = 0.92;
    J.x = mortar;
}

void f_rubble(vec2 uv, float px, float seed, inout vec3 col, inout vec4 S, inout vec2 slope, inout vec2 J) {
    vec3 w = worley(uv * 3.4, seed);
    float v = vis(3.4, px);
    float gap = w.y - w.x;
    float mortar = (1.0 - aastep(0.14, gap)) * v;
    vec3 sc = col * mix(1.0, 0.76 + 0.42 * w.z, v) * (1.0 + (noise3(vec3(uv * 40.0, seed)) - 0.5) * 0.2 * vis(40.0, px));
    sc *= mix(1.0, 0.8 + 0.2 * smoothstep(0.14, 0.4, gap), v);
    col = mix(sc, mix(col * 1.1, vec3(0.26, 0.25, 0.22), 0.6) * 0.85, mortar);
    S.y = 0.94;
    J.x = mortar;
}

void f_poured(vec2 uv, vec3 wp, int kind, float px, float seed, inout vec3 col, inout vec4 S, inout vec2 slope) {
    col *= 1.0 + (fbm3(wp * 1.2 + seed) - 0.5) * 0.18;
    float agg = hash3(vec3(floor(wp * 70.0)));
    col *= 1.0 + (agg > 0.9 ? 0.09 : agg < 0.08 ? -0.09 : 0.0) * vis(70.0, px);
    if (kind == 0) {
        // Board-formed: the print of each form board, its grain, the lines where boards met.
        float b = uv.y / 0.1; float fb = fract(b); float idb = floor(b);
        col *= 1.0 - (1.0 - aastep(0.003, min(fb, 1.0 - fb) * 0.1)) * 0.12 * vis(10.0, px);
        col *= 1.0 + (hash3(vec3(idb, seed, 2.0)) - 0.5) * 0.1 * vis(10.0, px);
        col *= 1.0 + (noise3(vec3(uv.x * 4.0, uv.y * 90.0, seed + idb)) - 0.5) * 0.12 * vis(90.0, px);
    } else {
        // Panel-formed: a joint round each form panel, tie holes in rows.
        vec2 P = vec2(1.2, 0.6); vec2 q = mod(uv, P);
        col *= 1.0 - (1.0 - aastep(0.004, edge_of(q, P))) * 0.3 * vis(1.7, px);
        col *= 1.0 + (hash3(vec3(floor(uv / P), seed)) - 0.5) * 0.08 * vis(1.7, px);
        vec2 hv = mod(uv, vec2(0.6, 0.3)) - vec2(0.3, 0.15);
        float hole = 1.0 - aastep(0.012, length(hv));
        col *= 1.0 - hole * 0.6 * vis(30.0, px);
        slope -= hole * hv / 0.012 * 0.5 * vis(30.0, px);
    }
    // Where one pour met the next: a line every metre and a half up a wall.
    float lift = uv.y / 1.5 + (noise3(vec3(uv.x * 1.5, 0.0, seed)) - 0.5) * 0.05;
    col *= 1.0 - (1.0 - aastep(0.012, abs(fract(lift) - 0.5) * 2.0 * 0.75)) * 0.12 * vis(2.0, px);
    S.y = 0.9;
}

void f_precast(vec2 uv, float px, float seed, inout vec3 col, inout vec4 S, inout vec2 slope, inout vec2 J) {
    vec2 P = vec2(3.0, 1.5); vec2 q = mod(uv, P);
    float v = vis(1.0 / 1.5, px);
    float e = edge_of(q, P);
    float joint = (1.0 - aastep(0.011, e)) * v;
    col *= mix(1.0, 0.94 + 0.12 * hash3(vec3(floor(uv / P), seed)), v);
    col *= 1.0 + (noise3(vec3(uv * 110.0, seed)) - 0.5) * 0.08 * vis(110.0, px);
    col = mix(col, col * 0.3, joint);
    slope += bevel(q, P, 0.02) * 0.5 * v;
    J.x = joint;
    S.y = 0.78;
}

void f_block(vec2 uv, float px, float seed, bool painted, inout vec3 col, inout vec4 S, inout vec2 slope, inout vec2 J) {
    vec2 size = vec2(0.4, 0.2); vec2 id;
    vec2 q = bond_unit(uv, size, 0.5, id);
    float v = vis(5.0, px);
    float e = edge_of(q, size);
    float joint = (1.0 - aastep(0.006, e)) * v;
    col *= mix(1.0, 0.95 + 0.1 * hash3(vec3(id, seed)), v * (painted ? 0.3 : 1.0));
    col *= 1.0 + (noise3(vec3(uv * 160.0, seed)) - 0.5) * (painted ? 0.07 : 0.16) * vis(160.0, px);
    col *= 1.0 - joint * (painted ? 0.16 : 0.3);
    slope += bevel(q, size, 0.008) * 0.4 * v;
    J.x = joint;
    S.y = painted ? 0.75 : 0.93;
}

void f_corrugated(vec2 uv, int kind, float px, float seed, inout vec3 col, inout vec4 S, inout vec2 slope, inout vec2 J) {
    // Ribs up the sheet; sheets 0.85 m wide lapped side by side, 2.4 m long lapped end over end, screwed at the laps.
    float P = 0.076;
    float a = uv.x / P * 6.2832;
    float v = vis(1.0 / P, px);
    col *= mix(1.0, 0.8 + 0.2 * (0.5 + 0.5 * sin(a)), v);
    slope.x += cos(a) * 0.65 * v;
    vec2 sheet = vec2(0.85, 2.4);
    vec2 sid = floor(uv / sheet); vec2 q = mod(uv, sheet);
    col *= mix(1.0, 0.92 + 0.14 * hash3(vec3(sid, seed)), vis(1.2, px));
    float lap = 1.0 - aastep(0.006, min(q.y, sheet.y - q.y));
    col *= 1.0 - lap * 0.5 * vis(10.0, px);
    vec2 sc = vec2(mod(uv.x, P * 2.0) - P, min(q.y, sheet.y - q.y) - 0.04);
    float screw = (1.0 - aastep(0.006, length(sc))) * vis(60.0, px);
    col *= 1.0 - screw * 0.5;
    J.x = max(lap, screw);
    S.y = kind == 1 ? 0.75 : 0.42; S.z = kind == 1 ? 0.25 : 0.75;
}

void f_seam(vec2 uv, float px, float seed, inout vec3 col, inout vec4 S, inout vec2 slope, inout vec2 J) {
    // Standing seams up the roof every 45 cm, the trays between them faintly rippled.
    float P = 0.45; float x = mod(uv.x, P); float d = min(x, P - x);
    float v = vis(1.0 / P, px);
    float rib = 1.0 - aastep(0.012, d);
    col *= 1.0 + (rib * (x < P * 0.5 ? 0.25 : -0.3)) * v;
    slope.x += rib * (x < P * 0.5 ? -0.6 : 0.6) * v;
    col *= 1.0 + (noise3(vec3(uv.x * 6.0, uv.y * 1.2, seed)) - 0.5) * 0.1;
    J.x = rib * v;
    S.y = 0.38; S.z = 0.8;
}

void f_plate(vec2 uv, float px, float seed, bool painted, inout vec3 col, inout vec4 S, inout vec2 slope, inout vec2 J) {
    // Welded plates a metre square, each its own tone, brushed, with a row of rivets round its edge.
    vec2 f = fract(uv);
    float v = vis(1.0, px);
    float e = min(min(f.x, 1.0 - f.x), min(f.y, 1.0 - f.y));
    float seam = (1.0 - aastep(0.006, e)) * v;
    col *= mix(1.0, 0.93 + 0.12 * hash3(vec3(floor(uv), seed)), v);
    col *= 1.0 - seam * 0.5;
    slope += bevel(f, vec2(1.0), 0.03) * 0.4 * v;
    col *= 1.0 + (noise3(vec3(uv.x * 3.0, uv.y * 120.0, seed)) - 0.5) * (painted ? 0.03 : 0.12) * vis(120.0, px);
    vec2 g = (floor(f * 8.0) + 0.5) / 8.0;
    vec2 ra = vec2(f.x < 0.5 ? 0.04 : 0.96, g.y), rb = vec2(g.x, f.y < 0.5 ? 0.04 : 0.96);
    vec2 rc = length(f - ra) < length(f - rb) ? ra : rb;
    float rv = 1.0 - aastep(0.014, length(f - rc));
    float rvv = vis(60.0, px);
    col *= 1.0 + rv * 0.14 * rvv;
    slope += rv * (f - rc) / 0.014 * 0.6 * rvv;
    J.x = max(seam, rv * rvv);
    S.y = painted ? 0.55 : 0.4; S.z = painted ? 0.15 : 0.8;
}

void f_planks(vec2 uv, int kind, bool wall, float px, float seed, bool painted, inout vec3 col, inout vec4 S, inout vec2 slope, inout vec2 J) {
    // Boards 14 cm wide along their run, butt joints staggered, grain along each, a gap between.
    vec2 r = (wall || kind == 1) ? vec2(uv.y, uv.x) : uv;
    float w = wall ? 0.16 : 0.14;
    float b = floor(r.y / w); float t = fract(r.y / w);
    float L = 1.2 + 1.2 * hash3(vec3(b, seed, 7.0));
    float x = r.x / L + hash3(vec3(b, seed, 3.0)); float fx = fract(x);
    float v = vis(1.0 / w, px);
    float gap = max(1.0 - aastep(0.003, min(t, 1.0 - t) * w), 1.0 - aastep(0.002, min(fx, 1.0 - fx) * L)) * v;
    float bs = hash3(vec3(b, floor(x), seed));
    col *= mix(1.0, 0.86 + 0.26 * bs, v * (painted ? 0.3 : 1.0));
    float g = (r.y + fbm3(vec3(r.x * 1.3, r.y * 6.0, bs * 9.0)) * 0.05) * 45.0;
    col *= 1.0 - (smoothstep(0.5, 0.9, abs(fract(g) - 0.5) * 2.0) - 0.3) * (painted ? 0.04 : 0.24) * vis(45.0, px);
    col *= 1.0 + (noise3(vec3(r.x * 30.0, r.y * 600.0, bs * 5.0)) - 0.5) * (painted ? 0.03 : 0.12) * vis(600.0, px);
    col *= 1.0 - gap * 0.6;
    J.x = gap;
    S.y = painted ? 0.7 : 0.62;
}

void f_parquet(vec2 uv, float px, float seed, inout vec3 col, inout vec4 S, inout vec2 slope, inout vec2 J) {
    // Basket weave: squares of three strips, each square turned to its neighbours.
    float s = 0.21;
    vec2 cid = floor(uv / s); vec2 q = mod(uv, s);
    bool turn = mod(cid.x + cid.y, 2.0) > 0.5;
    vec2 r = turn ? q.yx : q;
    float strip = floor(r.y / 0.07); float t = fract(r.y / 0.07);
    float v = vis(1.0 / 0.07, px);
    float gap = max(1.0 - aastep(0.0015, min(t, 1.0 - t) * 0.07), 1.0 - aastep(0.0015, edge_of(q, vec2(s)))) * v;
    float bs = hash3(vec3(cid, strip + seed));
    col *= mix(1.0, 0.85 + 0.28 * bs, v);
    float g = (r.y + noise3(vec3(r.x * 9.0, bs * 7.0, 1.0)) * 0.01) * 300.0;
    col *= 1.0 - (smoothstep(0.5, 0.9, abs(fract(g) - 0.5) * 2.0) - 0.3) * 0.18 * vis(300.0, px);
    col *= 1.0 - gap * 0.5;
    J.x = gap;
    S.y = 0.4; S.w = 0.6;
}

void f_tile(vec2 uv, int kind, float px, float seed, inout vec3 col, inout vec4 S, inout vec2 slope, inout vec2 J) {
    float s = kind == 2 ? 0.05 : kind == 3 ? 0.6 : 0.3;
    vec2 id = floor(uv / s); vec2 q = mod(uv, s);
    float v = vis(1.0 / s, px);
    float gw = kind == 2 ? 0.0015 : 0.003;
    float grout = (1.0 - aastep(gw, edge_of(q, vec2(s)))) * v;
    vec3 tc = col;
    if (kind == 1 && mod(id.x + id.y, 2.0) > 0.5) tc = luma(col) > 0.18 ? col * 0.18 : vec3(0.55, 0.54, 0.5);
    if (kind == 2) tc *= 0.85 + 0.3 * hash3(vec3(id, seed));
    tc *= mix(1.0, 0.95 + 0.1 * hash3(vec3(id, seed + 1.0)), v);
    tc *= 1.0 + (noise3(vec3(uv * 9.0, seed)) - 0.5) * 0.05;
    col = mix(tc, vec3(0.3, 0.29, 0.27), grout);
    slope += bevel(q, vec2(s), 0.004) * 0.5 * v;
    J.x = grout;
    S.y = mix(0.22, 0.9, grout); S.w = 0.6;
}

void f_lino(vec2 uv, vec3 wp, float px, float seed, inout vec3 col, inout vec4 S, inout vec2 slope, inout vec2 J) {
    // Sheet lino two metres wide: a marbled fleck, the welded seam between sheets.
    float m = fbm3(vec3(uv.x * 2.0, uv.y * 7.0, seed) + vec3(fbm3(wp * 3.0) * 1.5));
    col *= 1.0 + (m - 0.5) * 0.18 * vis(7.0, px);
    float fleck = hash3(vec3(floor(uv * 160.0), seed));
    col *= 1.0 + (fleck > 0.9 ? 0.12 : fleck < 0.06 ? -0.12 : 0.0) * vis(160.0, px);
    float x = mod(uv.x, 2.0);
    float seam = (1.0 - aastep(0.0015, min(x, 2.0 - x))) * vis(20.0, px);
    col *= 1.0 - seam * 0.4;
    J.x = seam;
    S.y = 0.42; S.w = 0.55;
}

void f_carpet(vec2 uv, int kind, float px, float seed, inout vec3 col, inout vec4 S, inout vec2 slope) {
    col *= 1.0 + (noise3(vec3(uv * 420.0, seed)) - 0.5) * 0.22 * vis(420.0, px);
    col *= 1.0 + (noise3(vec3(uv * 60.0, seed + 3.0)) - 0.5) * 0.1 * vis(60.0, px);
    if (kind == 1) {
        // Carpet tiles half a metre square, the pile of each laid across its neighbours'.
        vec2 id = floor(uv / 0.5);
        col *= 1.0 + (mod(id.x + id.y, 2.0) - 0.5) * 0.08 * vis(2.0, px);
        col *= 1.0 - (1.0 - aastep(0.002, edge_of(mod(uv, 0.5), vec2(0.5)))) * 0.25 * vis(10.0, px);
    }
    S.y = 1.0; S.w = 0.25;
}

void f_rooftile(vec2 uv, int kind, float px, float seed, inout vec3 col, inout vec4 S, inout vec2 slope, inout vec2 J) {
    // Rows up the slope, each lapping the row below; the row's lower edge throws a shadow.
    float g = kind == 0 ? 0.3 : kind == 1 ? 0.11 : 0.15;
    float w = kind == 0 ? 0.25 : kind == 1 ? 0.165 : 0.16;
    float row = floor(uv.y / g); float t = fract(uv.y / g);
    float v = vis(1.0 / g, px);
    float x = uv.x / w + (kind == 0 ? 0.0 : mod(row, 2.0) * 0.5);
    vec2 id = vec2(floor(x), row); float fx = fract(x);
    float tone = hash3(vec3(id, seed));
    col *= mix(1.0, 0.82 + 0.3 * tone, v);
    float lip = aastep(kind == 0 ? 0.08 : 0.12, t);
    if (kind == 2) {
        // Scalloped: each tile's tail a round end.
        vec2 c = vec2(fx - 0.5, t * g / w);
        lip = aastep(0.5, length(c * vec2(1.0, 0.9)));
        lip = max(lip, aastep(0.45, t));
    }
    col *= mix(1.0, mix(0.4, 1.0, lip) * (0.88 + 0.16 * t), v);
    slope.y -= 0.25 * v;
    if (kind == 0) {
        // Pantiles: the S-wave across the tile.
        float a = fx * 6.2832;
        col *= mix(1.0, 0.82 + 0.18 * (0.5 + 0.5 * sin(a)), vis(1.0 / w, px));
        slope.x += cos(a) * 0.7 * vis(1.0 / w, px);
    } else {
        float side = 1.0 - aastep(0.004, min(fx, 1.0 - fx) * w);
        col *= 1.0 - side * 0.5 * vis(1.0 / w, px);
    }
    col *= 1.0 + (noise3(vec3(uv * 60.0, seed)) - 0.5) * 0.12 * vis(60.0, px);
    J.x = (1.0 - lip) * v;
    S.y = 0.8;
}

void f_slate(vec2 uv, float px, float seed, inout vec3 col, inout vec4 S, inout vec2 slope, inout vec2 J) {
    float g = 0.2; float w = 0.3;
    float row = floor(uv.y / g);
    float x = uv.x / w + mod(row, 2.0) * 0.5;
    vec2 id = vec2(floor(x), row); float fx = fract(x);
    float sk = (hash3(vec3(id, seed + 3.0)) - 0.5) * 0.15;
    float t = fract(uv.y / g + sk * (fx - 0.5));
    float v = vis(1.0 / g, px);
    float h = hash3(vec3(id, seed));
    col *= mix(vec3(1.0), (0.82 + 0.3 * h) * mix(vec3(1.0), vec3(0.94, 1.0, 1.08), hash3(vec3(id, 7.0))), v);
    float lip = aastep(0.05, t);
    col *= mix(1.0, mix(0.35, 1.0, lip), v);
    col *= 1.0 - (1.0 - aastep(0.0025, min(fx, 1.0 - fx) * w)) * 0.5 * vis(1.0 / w, px);
    col *= 1.0 + (noise3(vec3(uv.x * 40.0, uv.y * 12.0, seed + h)) - 0.5) * 0.1 * vis(40.0, px);
    slope.y -= 0.18 * v;
    J.x = (1.0 - lip) * v;
    S.y = 0.62; S.w = 0.55;
}

void f_shingle(vec2 uv, int kind, float px, float seed, inout vec3 col, inout vec4 S, inout vec2 slope, inout vec2 J) {
    float g = 0.14;
    vec2 id; vec2 size;
    vec2 q;
    if (kind == 0) q = wander_unit(uv, 0.18, g, 0.7, seed, id, size);
    else { size = vec2(0.33, g); q = bond_unit(uv, size, 0.5, id); }
    float v = vis(1.0 / g, px);
    float t = q.y / g;
    float lip = aastep(0.07, t);
    float side = 1.0 - aastep(kind == 0 ? 0.004 : 0.003, min(q.x, size.x - q.x));
    col *= mix(1.0, 0.8 + 0.32 * hash3(vec3(id, seed)), v);
    col *= mix(1.0, mix(0.38, 1.0, lip) * (0.9 + 0.12 * t), v);
    col *= 1.0 - side * 0.55 * vis(1.0 / 0.18, px);
    if (kind == 0) col *= 1.0 + (noise3(vec3(uv.x * 160.0, uv.y * 4.0, seed + id.x)) - 0.5) * 0.25 * vis(160.0, px);
    else col *= 1.0 + (hash3(vec3(floor(uv * 300.0), seed)) - 0.5) * 0.3 * vis(300.0, px);
    slope.y -= 0.22 * v;
    J.x = max(1.0 - lip, side) * v;
    S.y = 0.9;
}

void f_thatch(vec2 uv, float px, float seed, inout vec3 col, inout vec4 S, inout vec2 slope, inout vec2 J) {
    // Straw laid down the slope: fine strands, the bundles they were tied in, a course every 35 cm.
    float st = noise3(vec3(uv.x * 110.0, uv.y * 3.0, seed));
    col *= 1.0 + (st - 0.5) * 0.4 * vis(110.0, px);
    col *= 1.0 + (noise3(vec3(uv.x * 400.0, uv.y * 8.0, seed + 2.0)) - 0.5) * 0.25 * vis(400.0, px);
    float clump = fbm3(vec3(uv.x * 4.0, uv.y * 1.5, seed + 5.0));
    col *= 0.85 + 0.3 * clump;
    float t = fract(uv.y / 0.35 + (noise3(vec3(uv.x * 3.0, 0.0, seed)) - 0.5) * 0.2);
    col *= mix(1.0, 0.7 + 0.3 * smoothstep(0.0, 0.25, t), vis(3.0, px));
    slope.x += (st - 0.5) * 0.8 * vis(110.0, px);
    slope.y -= 0.2;
    J.x = (1.0 - smoothstep(0.0, 0.15, t)) * vis(3.0, px);
    S.y = 1.0; S.w = 0.3;
}

void f_tar(vec2 uv, vec3 wp, float px, float seed, inout vec3 col, inout vec4 S, inout vec2 slope, inout vec2 J) {
    // A felted flat roof spread with gravel: the stones, the laps of the rolls, ponding stains.
    vec3 w = worley(uv * 55.0, seed);
    float stone = (1.0 - aastep(0.32, w.x)) * vis(55.0, px);
    vec3 gc = mix(vec3(0.24, 0.22, 0.19), vec3(0.4, 0.38, 0.34), w.z);
    col = mix(col * (0.9 + 0.2 * mix(0.5, noise3(wp * 9.0), vis(9.0, px))), gc, stone * 0.75);
    slope += stone * (vec2(hash3(vec3(w.z, 1.0, 2.0)), hash3(vec3(w.z, 3.0, 4.0))) - 0.5) * 0.6;
    float lap = mod(uv.y, 1.0);
    col *= 1.0 - (1.0 - aastep(0.004, abs(lap - 0.5))) * 0.3 * vis(4.0, px);
    float pond = smoothstep(0.55, 0.7, fbm3(wp * 0.6 + seed));
    col *= 1.0 - pond * 0.25;
    S.y = mix(0.92, 0.5, pond);
    J.x = 1.0 - stone;
}

void f_walltile(vec2 uv, int kind, float px, float seed, inout vec3 col, inout vec4 S, inout vec2 slope, inout vec2 J) {
    vec2 s = kind == 3 ? vec2(0.3, 0.15) : vec2(0.15);
    vec2 id; vec2 q = bond_unit(uv, s, kind == 3 ? 0.5 : 0.0, id);
    float v = vis(1.0 / s.y, px);
    float grout = (1.0 - aastep(0.0022, edge_of(q, s))) * v;
    vec3 tc = col * mix(1.0, 0.96 + 0.08 * hash3(vec3(id, seed)), v);
    tc *= 1.0 + (noise3(vec3(uv * 25.0, seed)) - 0.5) * 0.05 * vis(25.0, px);
    col = mix(tc, vec3(0.5, 0.49, 0.46), grout);
    slope += bevel(q, s, 0.006) * 0.5 * v;
    slope += (vec2(noise3(vec3(uv * 14.0, seed)), noise3(vec3(uv * 14.0, seed + 4.0))) - 0.5) * 0.12 * vis(14.0, px);
    J.x = grout;
    S.y = mix(0.12, 0.85, grout); S.w = 0.7;
}

void f_deck(vec2 uv, float px, float seed, inout vec3 col, inout vec4 S, inout vec2 slope, inout vec2 J) {
    // Tread plate: raised lozenges, each turned to its neighbours; plates welded every 1.2 m.
    vec2 g = uv / 0.035; vec2 c = fract(g) - 0.5;
    bool alt = mod(floor(g.x) + floor(g.y), 2.0) > 0.5;
    vec2 r = alt ? vec2(c.x + c.y, c.x - c.y) : vec2(c.x - c.y, c.x + c.y);
    r *= 0.7071;
    float loz = 1.0 - aastep(0.13, length(r * vec2(1.0, 3.2)));
    float lv = vis(28.0, px);
    col *= 1.0 + loz * 0.18 * lv;
    slope += loz * r * 3.0 * lv;
    vec2 q = mod(uv, 1.2);
    float weld = (1.0 - aastep(0.008, edge_of(q, vec2(1.2)))) * vis(2.0, px);
    col *= mix(1.0, 0.9 + 0.15 * hash3(vec3(floor(uv / 1.2), seed)), vis(1.0, px));
    col *= 1.0 - weld * 0.4;
    J.x = max(weld, (1.0 - loz) * lv * 0.5);
    S.y = mix(0.45, 0.32, loz * lv); S.z = 0.8;
}

void f_hull(vec2 uv, float px, float seed, inout vec3 col, inout vec4 S, inout vec2 slope, inout vec2 J) {
    // Panels two metres by one, each its own tone, a recessed seam round each and a fastener at its corners.
    vec2 P = vec2(2.0, 1.0); vec2 id; vec2 q = bond_unit(uv, P, 0.5, id);
    float v = vis(1.0, px);
    float e = edge_of(q, P);
    float seam = (1.0 - aastep(0.007, e)) * v;
    col *= mix(1.0, 0.93 + 0.12 * hash3(vec3(id, seed)), v);
    col *= 1.0 - seam * 0.55;
    slope += bevel(q, P, 0.02) * 0.45 * v;
    vec2 cq = vec2(q.x < P.x * 0.5 ? 0.05 : P.x - 0.05, q.y < P.y * 0.5 ? 0.05 : P.y - 0.05);
    float fast = (1.0 - aastep(0.011, length(q - cq))) * vis(50.0, px);
    col *= 1.0 - fast * 0.35;
    col *= 1.0 + (noise3(vec3(uv * 6.0, seed)) - 0.5) * 0.05;
    J.x = max(seam, fast);
    S.y = 0.46; S.z = 0.7;
}

void f_paving(vec2 uv, int kind, float px, float seed, inout vec3 col, inout vec4 S, inout vec2 slope, inout vec2 J) {
    vec2 id; vec2 size; vec2 q;
    if (kind == 1) q = wander_unit(uv, 0.55, 0.45, 0.5, seed, id, size);
    else { size = vec2(0.6); q = bond_unit(uv, size, 0.0, id); }
    float v = vis(1.0 / size.y, px);
    float joint = (1.0 - aastep(0.006, edge_of(q, size))) * v;
    col *= mix(1.0, 0.86 + 0.24 * hash3(vec3(id, seed)), v);
    col *= 1.0 + (fbm3(vec3(uv * 6.0, seed)) - 0.5) * 0.18 * vis(6.0, px);
    col *= 1.0 + (noise3(vec3(uv * 90.0, seed)) - 0.5) * 0.12 * vis(90.0, px);
    col = mix(col, col * 0.4, joint);
    slope += bevel(q, size, 0.012) * 0.4 * v;
    J.x = joint;
    S.y = 0.92;
}

void f_chitin(vec2 uv, vec3 wp, float px, float seed, float fres, inout vec3 col, inout vec4 S, inout vec2 slope, inout vec2 J) {
    // Overlapping carapace plates: each a round-tailed scale with growth lines, a dark rim, a wet sheen that shifts with the eye.
    float g = 0.32; float w = 0.42;
    float row = floor(uv.y / g);
    float x = uv.x / w + mod(row, 2.0) * 0.5;
    vec2 id = vec2(floor(x), row); float fx = fract(x); float t = fract(uv.y / g);
    vec2 c = vec2(fx - 0.5, (t - 0.85) * g / w);
    float r = length(c * vec2(1.0, 1.25));
    float v = vis(1.0 / g, px);
    float rim = (1.0 - aastep(0.035, abs(r - 0.5))) * v;
    float inside = 1.0 - aastep(0.5, r);
    col *= mix(1.0, 0.8 + 0.35 * hash3(vec3(id, seed)), v);
    col *= 1.0 + sin(r * 60.0 + hash3(vec3(id, 3.0)) * 6.0) * 0.08 * inside * vis(30.0, px);
    col = mix(col, col * 0.3, rim);
    slope += normalize(c + 1e-4) * smoothstep(0.2, 0.5, r) * inside * 0.7 * v;
    col *= 1.0 + fres * vec3(-0.1, 0.25, 0.35) * 1.4;
    J.x = rim;
    S.y = 0.22; S.w = 0.75;
}

void f_resin(vec2 uv, vec3 wp, float px, float seed, inout vec3 col, inout vec4 S, inout vec2 slope, inout vec2 J) {
    // Poured resin, hardened as it ran: flow lines down it, bubbles caught in it, glossy.
    vec3 warp = vec3(fbm3(wp * 1.5 + seed), fbm3(wp * 1.5 + seed + 4.0), 0.0);
    float flow = noise3(vec3(uv.x * 9.0 + warp.x * 3.0, uv.y * 1.6 + warp.y, seed));
    col *= 0.8 + 0.4 * flow;
    vec3 b = worley(uv * 22.0, seed + 2.0);
    float bub = (1.0 - aastep(0.12, b.x)) * step(0.7, b.z) * vis(22.0, px);
    col = mix(col, col * 1.8 + vec3(0.02), bub * 0.6);
    slope += vec2(noise3(vec3(uv * 7.0, seed)) - 0.5, flow - 0.5) * 0.4 * vis(7.0, px);
    J.x = 1.0 - flow;
    S.y = 0.14; S.w = 0.7;
}

void f_bone(vec2 uv, vec3 wp, float px, float seed, inout vec3 col, inout vec4 S, inout vec2 slope, inout vec2 J) {
    // Grown bone: ridges that wander round the wall, pores, yellowed in the hollows.
    float rid = sin((uv.y + fbm3(vec3(uv * 1.6, seed)) * 1.1) * 14.0);
    float v = vis(3.5, px);
    col *= 1.0 + rid * 0.06 * v;
    slope.y += cos((uv.y + fbm3(vec3(uv * 1.6, seed)) * 1.1) * 14.0) * 0.35 * v;
    vec3 p = worley(uv * 18.0, seed + 1.0);
    float pore = (1.0 - aastep(0.12, p.x)) * step(0.72, p.z) * vis(18.0, px);
    col = mix(col, col * vec3(0.55, 0.45, 0.3), pore * 0.8);
    float hollow = smoothstep(0.3, -0.6, rid);
    col = mix(col, col * vec3(0.92, 0.82, 0.62), hollow * 0.5 * v);
    J.x = max(pore, hollow * 0.5);
    S.y = 0.55; S.w = 0.5;
}

void f_fused(vec2 uv, vec3 wp, float px, float seed, inout vec3 col, inout vec4 S, inout vec2 slope, inout vec2 J) {
    // Fused glass: a melt that ran and set, glossy black-green, with a craze of light caught under the skin.
    float m = fbm3(vec3(uv.x * 2.0, uv.y * 0.6, seed) + vec3(fbm3(wp * 2.0) * 2.0));
    col *= 0.7 + 0.6 * m;
    float crz = cracks(uv * 3.0 + m, seed, 0.02) * vis(6.0, px);
    col = mix(col, col * 2.4 + vec3(0.02, 0.04, 0.03), crz * 0.5);
    slope += (vec2(noise3(vec3(uv * 4.0, seed)), m) - 0.5) * 0.6;
    J.x = 0.0;
    S.y = 0.06; S.w = 0.9;
}

void f_basalt(vec2 uv, bool floor_, float px, float seed, inout vec3 col, inout vec4 S, inout vec2 slope, inout vec2 J) {
    float joint;
    if (floor_) {
        // Hexagonal flags: the columns' tops.
        vec2 s = vec2(1.0, 1.7320508) * 0.5;
        vec2 a = mod(uv, s) - s * 0.5; vec2 b = mod(uv - s * 0.5, s) - s * 0.5;
        vec2 gv = dot(a, a) < dot(b, b) ? a : b;
        vec2 hid = uv - gv;
        vec2 ag = abs(gv);
        float d = 0.25 - max(dot(ag, normalize(vec2(1.0, 1.7320508))), ag.x);
        joint = (1.0 - aastep(0.004, d)) * vis(2.0, px);
        col *= mix(1.0, 0.85 + 0.3 * hash3(vec3(floor(hid * 10.0), seed)), vis(2.0, px));
    } else {
        // Honed blocks, close-jointed.
        vec2 size = vec2(0.9, 0.45); vec2 id; vec2 q = bond_unit(uv, size, 0.5, id);
        joint = (1.0 - aastep(0.0025, edge_of(q, size))) * vis(2.2, px);
        col *= mix(1.0, 0.86 + 0.28 * hash3(vec3(id, seed)), vis(2.2, px));
    }
    float sp = hash3(vec3(floor(uv * 260.0), seed));
    col *= 1.0 + (sp > 0.92 ? 0.35 : 0.0) * vis(260.0, px);
    col *= 1.0 - joint * 0.6;
    J.x = joint;
    S.y = 0.32; S.w = 0.6;
}

void f_bronze(vec2 uv, float px, float seed, inout vec3 col, inout vec4 S, inout vec2 slope, inout vec2 J) {
    // Cast panels with a raised border; verdigris gathers in the recess.
    vec2 P = vec2(0.8); vec2 q = mod(uv, P);
    float e = edge_of(q, P);
    float v = vis(1.25, px);
    float border = (1.0 - aastep(0.05, e)) * v;
    float groove = (1.0 - aastep(0.008, abs(e - 0.055))) * v;
    col *= mix(1.0, 0.9 + 0.15 * hash3(vec3(floor(uv / P), seed)), v);
    col *= 1.0 + border * 0.12 - groove * 0.4;
    slope += bevel(q, P, 0.05) * 0.5 * border;
    col = mix(col, vec3(0.06, 0.16, 0.12), groove * 0.7);
    col *= 1.0 + (noise3(vec3(uv * 40.0, seed)) - 0.5) * 0.12 * vis(40.0, px);
    J.x = groove;
    S.y = 0.35; S.z = 0.85;
}

void f_rockface(vec2 uv, vec3 wp, float px, float seed, inout vec3 col, inout vec4 S, inout vec2 slope, inout vec2 J) {
    // Living rock: bedding bands that wander, fractures, a rough mottle.
    float band = fbm3(vec3(uv.x * 0.4, uv.y * 3.0 + fbm3(wp * 0.7) * 1.5, seed));
    col *= 0.8 + 0.4 * band;
    col *= 1.0 + (fbm3(wp * 6.0 + seed) - 0.5) * 0.25 * vis(6.0, px);
    float fr = cracks(uv * 1.4, seed, 0.03) * vis(4.0, px);
    col *= 1.0 - fr * 0.5;
    slope += (vec2(fbm3(wp * 3.0), fbm3(wp * 3.0 + 5.0)) - 0.5) * 1.2;
    J.x = fr;
    S.y = 0.96;
}

void f_soil(vec2 uv, vec3 wp, float px, float seed, inout vec3 col, inout vec4 S, inout vec2 slope, inout vec2 J) {
    vec3 w = worley(uv * 12.0, seed);
    col *= mix(1.0, 0.8 + 0.4 * w.z, vis(12.0, px));
    float peb = hash3(vec3(floor(uv * 90.0), seed));
    col = mix(col, vec3(0.3, 0.28, 0.24), (peb > 0.95 ? 0.6 : 0.0) * vis(90.0, px));
    col *= 1.0 + (fbm3(wp * 2.0) - 0.5) * 0.25;
    J.x = 1.0 - aastep(0.1, w.y - w.x);
    S.y = 1.0; S.w = 0.3;
}

void f_ceilingtile(vec2 uv, int wear, float px, float seed, inout vec3 col, inout vec4 S, inout vec2 slope, inout vec2 J) {
    // A suspended ceiling: 60 cm tiles in a light grid; fissured tiles; old ones stained and some gone.
    vec2 id = floor(uv / 0.6); vec2 q = mod(uv, 0.6);
    float v = vis(1.0 / 0.6, px);
    float bar = (1.0 - aastep(0.012, edge_of(q, vec2(0.6)))) * v;
    float h = hash3(vec3(id, seed));
    vec3 tile = col * (1.0 + (noise3(vec3(uv * 120.0, seed)) - 0.5) * 0.2 * vis(120.0, px));
    tile *= 1.0 - (1.0 - aastep(0.25, worley(uv * 70.0, seed).x)) * 0.25 * vis(70.0, px);
    if (wear >= 2 && h < 0.18 * float(wear - 1)) tile = vec3(0.012, 0.011, 0.01);
    else if (wear >= 1 && h > 0.7) {
        // A water ring: brown, darkest at its rim.
        float r = length(q - vec2(0.3)) / 0.3 + (noise3(vec3(uv * 8.0, seed)) - 0.5) * 0.4;
        float ring = smoothstep(1.0, 0.6, r) * (0.4 + 0.6 * smoothstep(0.4, 0.95, r));
        tile = mix(tile, tile * vec3(0.7, 0.55, 0.36), ring * 0.7);
    }
    col = mix(tile, vec3(0.6, 0.6, 0.58), bar);
    J.x = bar;
    S.y = mix(1.0, 0.5, bar);
}

void f_armour(vec2 uv, float px, float seed, inout vec3 col, inout vec4 S, inout vec2 slope, inout vec2 J) {
    // Hardened plate: two heavy plates a block, wide bevelled seams, a bolt in each plate's corners.
    vec2 q = vec2(fract(uv.x), fract(uv.y * 2.0) * 0.5);
    vec2 size = vec2(1.0, 0.5);
    float v = vis(2.0, px);
    float e = edge_of(q, size);
    col *= mix(1.0, mix(0.42, 1.0, aastep(0.013, e)), v);
    slope += bevel(q, size, 0.085) * 0.7 * v;
    col *= 1.0 + (hash3(vec3(floor(uv.x), floor(uv.y * 2.0), seed)) - 0.5) * 0.1 * v;
    vec2 bc = vec2(q.x < 0.5 ? 0.085 : 0.915, q.y < 0.25 ? 0.085 : 0.415);
    float bolt = (1.0 - aastep(0.026, length(q - bc))) * vis(30.0, px);
    col *= 1.0 + bolt * 0.12;
    slope += bolt * (q - bc) / 0.026 * 0.8;
    J.x = 1.0 - aastep(0.013, e);
    S.y = 0.45; S.z = 0.7;
}

void f_patched(vec2 uv, float px, float seed, inout vec3 col, inout vec4 S, inout vec2 slope, inout vec2 J) {
    // Sheets of whatever was to hand: corrugated one way or the other, flat plate, each its own tone, screwed at its edges.
    vec2 P = vec2(1.1, 0.9);
    float row = floor(uv.y / P.y);
    float x = uv.x / P.x + hash3(vec3(row, seed, 1.0));
    vec2 id = vec2(floor(x), row);
    vec2 q = vec2(fract(x) * P.x, uv.y - row * P.y);
    float h = hash3(vec3(id, seed + 0.7));
    float v = vis(1.0, px);
    col *= mix(1.0, 0.72 + 0.5 * hash3(vec3(id, seed + 3.3)), v);
    col *= mix(vec3(1.0), vec3(1.1, 0.95, 0.85), (hash3(vec3(id, 5.5)) - 0.5) * v);
    if (h < 0.4 || h > 0.75) {
        float a = (h < 0.4 ? uv.x : uv.y) / 0.076 * 6.2832;
        float rv = vis(13.0, px);
        col *= mix(1.0, 0.8 + 0.2 * (0.5 + 0.5 * sin(a)), rv);
        if (h < 0.4) slope.x += cos(a) * 0.6 * rv; else slope.y += cos(a) * 0.6 * rv;
    }
    float e = edge_of(q, P);
    float lap = (1.0 - aastep(0.006, e)) * v;
    col *= 1.0 - lap * 0.6;
    vec2 sq = vec2(mod(q.x + 0.075, 0.15) - 0.075, mod(q.y + 0.075, 0.15) - 0.075);
    float screw = (1.0 - aastep(0.007, length(sq))) * (1.0 - aastep(0.04, e)) * vis(60.0, px);
    col *= 1.0 - screw * 0.5;
    J.x = max(lap, screw);
    S.y = 0.6; S.z = 0.5;
}

void f_cloth(vec2 uv, float px, float seed, inout vec3 col, inout vec4 S, inout vec2 slope) {
    vec2 t = uv * 1570.8;
    col *= 1.0 + sin(t.x) * sin(t.y) * 0.08 * vis(250.0, px);
    col *= 1.0 + (noise3(vec3(uv.x * 20.0, uv.y * 250.0, seed)) - 0.5) * 0.12 * vis(250.0, px);
    col *= 1.0 + (fbm3(vec3(uv * 3.0, seed)) - 0.5) * 0.15;
    S.y = 0.96; S.w = 0.3;
}

// What lies under a coat, drawn in a colour of its own where the coat has gone.
void f_under(int under, vec2 uv, vec3 wp, float px, float seed, inout vec3 col, inout vec4 S, inout vec2 slope) {
    vec2 J = vec2(0.0);
    if (under == 1) { col = vec3(0.23, 0.075, 0.045); f_brick(uv, 0, px, seed, col, S, slope, J); }
    else if (under == 2) { col = vec3(0.17, 0.165, 0.155); f_block(uv, px, seed, false, col, S, slope, J); }
    else if (under == 3) { col = vec3(0.16, 0.14, 0.11); f_rubble(uv, px, seed, col, S, slope, J); }
    else if (under == 4) {
        // Lath: thin strips across, gaps between them black.
        col = vec3(0.13, 0.085, 0.05);
        float t = fract(uv.y / 0.035);
        col *= mix(1.0, aastep(0.2, t), vis(28.0, px));
        S.y = 0.9;
    }
    else { col = vec3(0.16, 0.155, 0.145); f_poured(uv, wp, 1, px, seed, col, S, slope); }
}

void fragment() {
    vec3 col = to_linear(COLOR.rgb);
    int code = int(v_code.x + 0.5);
    int kind = code & 63;
    float rough = flat_rough;
    float metal = flat_metal;
    float spec = 0.5;
    if (kind != 0 && detail > 0.5) {
        int param = (code >> 6) & 3;
        int wear = (code >> 8) & 3;
        int hgt = (code >> 10) & 3;
        bool indoors = ((code >> 12) & 1) == 1;
        bool damp = ((code >> 13) & 1) == 1;
        int scorch = (code >> 14) & 3;
        bool painted = ((code >> 16) & 1) == 1;
        int under = (code >> 17) & 7;
        int edges = (code >> 20) & 15;
        float pseed = v_code.y;

        // The face's own frame: across and up, in metres (floors and flat roofs: east and south).
        vec3 n = normalize(v_objn);
        vec3 U; vec3 V;
        bool flat_ = abs(n.y) > 0.9;
        if (flat_) { U = vec3(1.0, 0.0, 0.0); V = vec3(0.0, 0.0, 1.0); }
        else { V = normalize(vec3(0.0, 1.0, 0.0) - n * n.y); U = normalize(cross(V, n)); }
        vec3 wp = v_obj;
        vec2 uv = vec2(dot(wp, U), dot(wp, V));
        // Derivatives before any branch: a pixel's size, and the face's frame in view space for the normal.
        float px = max(max(length(dFdx(wp)), length(dFdy(wp))), 1e-5);
        vec3 Uv = normalize((VIEW_MATRIX * MODEL_MATRIX * vec4(U, 0.0)).xyz);
        vec3 Vv = normalize((VIEW_MATRIX * MODEL_MATRIX * vec4(V, 0.0)).xyz);
        vec3 cell = floor(wp - n * 0.5);
        float seed = fract(hash3(cell * 0.37 + pseed * 0.013) + pseed * 0.618);
        float above = flat_ ? 0.0 : float(hgt) + fract(wp.y);
        float fres = pow(1.0 - clamp(dot(NORMAL, VIEW), 0.0, 1.0), 3.0);
        float up = clamp(n.y, 0.0, 1.0);
        bool wall = !flat_ && abs(n.y) < 0.35;
        vec4 S = vec4(1.0, 0.9, 0.0, 0.5);
        vec2 slope = vec2(0.0);
        vec2 J = vec2(0.0);

        if (kind == 1) f_brick(uv, param, px, pseed, col, S, slope, J);
        else if (kind == 2) f_render(wp, px, pseed, painted, col, S, slope);
        else if (kind == 3) f_plaster(wp, px, pseed, above, indoors, col, S, slope);
        else if (kind == 4) f_weatherboard(uv, param, px, pseed, painted, col, S, slope, J);
        else if (kind == 5) f_logs(uv, px, pseed, col, S, slope, J);
        else if (kind == 6) f_stone(uv, param, px, pseed, col, S, slope, J);
        else if (kind == 7) f_rubble(uv, px, pseed, col, S, slope, J);
        else if (kind == 8) f_poured(uv, wp, param, px, pseed, col, S, slope);
        else if (kind == 9) f_precast(uv, px, pseed, col, S, slope, J);
        else if (kind == 10) f_block(uv, px, pseed, painted, col, S, slope, J);
        else if (kind == 11) f_corrugated(uv, param, px, pseed, col, S, slope, J);
        else if (kind == 12) f_seam(uv, px, pseed, col, S, slope, J);
        else if (kind == 13) f_plate(uv, px, pseed, painted, col, S, slope, J);
        else if (kind == 14) f_planks(uv, param, !flat_, px, pseed, painted, col, S, slope, J);
        else if (kind == 15) f_parquet(uv, px, pseed, col, S, slope, J);
        else if (kind == 16) f_tile(uv, param, px, pseed, col, S, slope, J);
        else if (kind == 17) f_lino(uv, wp, px, pseed, col, S, slope, J);
        else if (kind == 18) f_carpet(uv, param, px, pseed, col, S, slope);
        else if (kind == 19) f_rooftile(uv, param, px, pseed, col, S, slope, J);
        else if (kind == 20) f_slate(uv, px, pseed, col, S, slope, J);
        else if (kind == 21) f_shingle(uv, param, px, pseed, col, S, slope, J);
        else if (kind == 22) f_thatch(uv, px, pseed, col, S, slope, J);
        else if (kind == 23) f_tar(uv, wp, px, pseed, col, S, slope, J);
        else if (kind == 24) f_walltile(uv, param, px, pseed, col, S, slope, J);
        else if (kind == 25) f_deck(uv, px, pseed, col, S, slope, J);
        else if (kind == 26) f_hull(uv, px, pseed, col, S, slope, J);
        else if (kind == 27) f_paving(uv, param, px, pseed, col, S, slope, J);
        else if (kind == 28) f_chitin(uv, wp, px, pseed, fres, col, S, slope, J);
        else if (kind == 29) f_resin(uv, wp, px, pseed, col, S, slope, J);
        else if (kind == 30) f_bone(uv, wp, px, pseed, col, S, slope, J);
        else if (kind == 31) f_fused(uv, wp, px, pseed, col, S, slope, J);
        else if (kind == 32) f_basalt(uv, flat_, px, pseed, col, S, slope, J);
        else if (kind == 33) f_bronze(uv, px, pseed, col, S, slope, J);
        else if (kind == 34) f_rockface(uv, wp, px, pseed, col, S, slope, J);
        else if (kind == 35) f_soil(uv, wp, px, pseed, col, S, slope, J);
        else if (kind == 36) f_ceilingtile(uv, wear, px, pseed, col, S, slope, J);
        else if (kind == 37) f_armour(uv, px, pseed, col, S, slope, J);
        else if (kind == 38) f_patched(uv, px, pseed, col, S, slope, J);
        else if (kind == 39) f_cloth(uv, px, pseed, col, S, slope);
        else if (kind == 40) { col *= 0.9 + 0.2 * fbm3(wp * 7.0); S.y = 1.0; }

        // ---- The years ------------------------------------------------------------------------
        float W = float(wear) / 3.0;
        bool mineral = kind <= 10 && kind != 4 && kind != 5 || kind == 16 || kind == 20 || kind == 24 || kind == 27 || kind == 32 || kind == 34 || kind == 19;
        bool metallic = kind == 11 || kind == 12 || kind == 13 || kind == 25 || kind == 26 || kind == 37 || kind == 38 || kind == 33;
        bool woody = kind == 4 || kind == 5 || kind == 14 || kind == 15 || kind == 21 || kind == 22;
        float lum = luma(col);

        // A coat coming away: paint flakes off first, then whole patches fall and show what is under.
        if ((painted || kind == 2 || kind == 3) && wear >= 1) {
            float m = fbm3(wp * 6.0 + pseed * 1.3) * 0.75 + noise3(wp * 26.0) * 0.25;
            float th = 0.75 - 0.055 * float(wear);
            float flake = smoothstep(th, th + 0.012, m) * (painted ? 1.0 : 0.0) * vis(6.0, px);
            vec3 bare = kind == 3 ? mix(col, vec3(0.42, 0.41, 0.38), 0.75)
                : kind == 4 || kind == 14 ? col * 0.35 + vec3(0.06, 0.05, 0.04)
                : kind == 13 || kind == 11 ? vec3(0.13, 0.08, 0.05)
                : kind == 26 ? vec3(0.3, 0.31, 0.32)
                : mix(col, vec3(0.26, 0.255, 0.24), 0.75);
            col = mix(col, bare * (0.9 + 0.2 * mix(0.5, noise3(wp * 40.0), vis(40.0, px))), flake);
            col *= 1.0 - (1.0 - smoothstep(0.0, 0.01, abs(m - th))) * 0.3 * vis(60.0, px) * (painted ? 1.0 : 0.0);
            if (wear >= 2 && under != 0 && (kind == 2 || kind == 3)) {
                float m2 = fbm3(wp * 1.8 + pseed * 2.1 + 9.0) + (wall ? (1.0 - smoothstep(0.0, 1.2, above)) * 0.08 : 0.0);
                float gone = smoothstep(0.72 - 0.06 * float(wear), 0.73 - 0.06 * float(wear), m2 + (noise3(wp * 22.0) - 0.5) * 0.08);
                if (gone > 0.0) {
                    vec3 uc = col; vec4 us = S; vec2 usl = vec2(0.0);
                    f_under(under, uv, wp, px, pseed, uc, us, usl);
                    col = mix(col, uc, gone);
                    S = mix(S, us, gone);
                    slope = mix(slope, usl, gone);
                }
                float lipg = 1.0 - smoothstep(0.0, 0.01, abs(m2 - (0.725 - 0.06 * float(wear))));
                col *= 1.0 - lipg * 0.5 * vis(50.0, px);
            }
        }

        if (!indoors) {
            // Weather: rain streaks down walls, worst under a sill or the wall's top; splash and dirt at the foot.
            if (wall) {
                float streak = mix(0.55, noise3(vec3(uv.x * 9.0, uv.y * 0.35, pseed)), vis(9.0, px));
                float under_sill = (edges & 1) != 0 ? 1.0 - smoothstep(0.0, 1.0, 1.0 - fract(wp.y)) * 0.6 : 0.3;
                float s = smoothstep(0.55, 0.85, streak) * (0.25 + 0.75 * W) * under_sill;
                col *= 1.0 - s * 0.35;
                float foot = (1.0 - smoothstep(0.0, 0.5, above)) * (0.3 + 0.7 * W);
                col = mix(col, col * vec3(0.62, 0.58, 0.5), foot * 0.5);
            }
            // Moss in the joints and on what faces the sky, the more the older and wetter; wood greys.
            if ((mineral || woody) && wear >= 1) {
                float wet = damp ? 1.0 : 0.65;
                float base = fbm3(wp * 3.1 + pseed) * 0.7 + noise3(wp * 11.0) * 0.3;
                float m = base + J.x * 0.45 + up * 0.12 + (wall ? (1.0 - smoothstep(0.0, 0.6, above)) * 0.12 - 0.15 : 0.0);
                float th = 0.86 - 0.08 * float(wear) * wet;
                float moss = smoothstep(th, th + 0.1, m) * mix(0.75, 0.5 + 0.5 * noise3(wp * 40.0), vis(40.0, px));
                vec3 mc = mix(vec3(0.045, 0.06, 0.02), vec3(0.09, 0.1, 0.035), mix(0.5, noise3(wp * 25.0), vis(25.0, px)));
                col = mix(col, mc, moss * 0.75);
                S.y = mix(S.y, 1.0, moss);
            }
            if (woody && kind != 22) col = mix(col, vec3(lum) * 1.15 + vec3(0.015), W * 0.55);
        } else {
            // Indoors: grime low on the walls; dust settles on what faces up.
            if (wall) col *= 1.0 - (1.0 - smoothstep(0.0, 0.6, above)) * 0.18 * W;
            if (up > 0.9) {
                float d = (0.05 + 0.12 * W) * (0.4 + 1.2 * fbm3(wp * 1.2 + 11.0));
                col = mix(col, vec3(0.22, 0.2, 0.16), clamp(d * W * 2.0, 0.0, 0.6));
                S.y = mix(S.y, 0.95, W * 0.6);
            }
        }

        // Damp climbing from the floor: darker and browner below a tide line, mould at the line and in the corners.
        if (wall && (damp || wear >= 2) && (mineral || kind == 3)) {
            float rise = (damp ? 0.6 : 0.25) + 0.7 * W * (damp ? 1.0 : 0.5) + (noise3(vec3(uv.x * 2.5, 0.0, pseed)) - 0.5) * 0.3;
            float below = 1.0 - smoothstep(rise - 0.04, rise + 0.02, above);
            col = mix(col, col * vec3(0.66, 0.62, 0.52), below * 0.7);
            float tide = 1.0 - smoothstep(0.0, 0.035, abs(above - rise));
            col = mix(col, col * vec3(0.5, 0.42, 0.3), tide * 0.6);
            if (damp) {
                vec3 mw = worley(uv * 9.0, pseed + 3.0);
                float spot = (1.0 - aastep(0.22, mw.x)) * step(0.45, mw.z) * smoothstep(rise + 0.4, rise - 0.1, above) * vis(9.0, px);
                spot *= (edges & 4) != 0 ? 1.0 : 0.55;
                col = mix(col, vec3(0.025, 0.035, 0.018), spot * 0.85);
            }
        }

        // Cracks, the older the more and the wider; a broken corner under an opening.
        if (mineral && wear >= 2 && kind != 34) {
            // A few cracks where the structure worked, not a crazed net over every face.
            float cw = wear == 2 ? 0.01 : 0.018;
            float c = cracks(uv * 0.75 + vec2(pseed * 3.0), pseed + 1.3, cw) * smoothstep(0.56, 0.7, fbm3(wp * 0.45 + 4.0));
            col *= 1.0 - c * 0.5 * vis(4.0, px);
        }

        // Alloy and armour don't rust: they dull in chalky patches and darken where water ran.
        if ((kind == 26 || kind == 37) && wear >= 1) {
            float ox = smoothstep(0.62 - 0.06 * float(wear), 0.8 - 0.06 * float(wear), fbm3(wp * 1.6 + pseed * 2.0));
            col = mix(col, vec3(luma(col)) * 1.25 + vec3(0.02), ox * 0.35 * W);
            S.y = mix(S.y, 0.75, ox * W);
            S.z *= 1.0 - ox * 0.4 * W;
        }
        // Rust: bare steel blooms orange-brown, worst at the laps and fixings, and runs down the wall.
        if (metallic && wear >= 1 && kind != 33 && kind != 26 && kind != 37) {
            float r = fbm3(wp * 3.5 + pseed * 3.0) + J.x * 0.3 + (1.0 - up) * 0.05 + (param == 1 && kind == 11 ? 0.25 : 0.0);
            float rust = smoothstep(0.9 - 0.08 * float(wear), 0.97 - 0.08 * float(wear), r);
            float run = wall ? smoothstep(0.6, 0.9, noise3(vec3(uv.x * 14.0, uv.y * 0.5, pseed + 2.0))) * W * vis(14.0, px) : 0.0;
            vec3 rc = mix(vec3(0.16, 0.05, 0.016), vec3(0.32, 0.12, 0.035), mix(0.5, noise3(vec3(uv * 50.0, pseed)), vis(50.0, px)));
            col = mix(col, rc, clamp(rust + run * 0.5, 0.0, 1.0));
            S.y = mix(S.y, 0.95, rust);
            S.z = mix(S.z, 0.05, clamp(rust + run * 0.5, 0.0, 1.0));
        }

        // Fire: soot in blooms, thickest over the openings it poured out of; charring at its worst.
        if (scorch > 0) {
            float s = float(scorch) / 3.0;
            float m = fbm3(wp * 0.9 + pseed + 21.0);
            float soot = smoothstep(0.62 - 0.25 * s, 0.75 - 0.25 * s, m);
            if ((edges & 2) != 0 && wall) soot = max(soot, (1.0 - smoothstep(0.0, 1.0, fract(wp.y))) * s * (0.6 + 0.4 * noise3(vec3(uv.x * 3.0, uv.y, pseed))));
            col = mix(col, vec3(0.012, 0.011, 0.01), soot * (0.55 + 0.4 * s));
            S.y = mix(S.y, 1.0, soot);
            S.z *= 1.0 - soot;
            if (scorch == 3 && woody) {
                vec3 ch = worley(uv * 14.0, pseed);
                col = mix(col, vec3(0.006), (1.0 - aastep(0.06, ch.y - ch.x)) * soot);
            }
        }

        rough = clamp(S.y, 0.04, 1.0);
        metal = S.z * metal_scale;
        spec = S.w;
        NORMAL = normalize(NORMAL + (Uv * slope.x + Vv * slope.y) * 0.35);
    }
    ALBEDO = col;
    ROUGHNESS = rough;
    METALLIC = metal;
    SPECULAR = spec;
}
""";

    /// <summary>
    /// Glass: framed. Each pane of a window has a frame round the window's edge and a thin bar between its
    /// panes (the mesher says which of a pane's neighbours are glass); old glass is filmed with dirt, the
    /// oldest cracked or gone. Panes without a code (other code's) stay as they were.
    /// </summary>
    public const string Glass = """
shader_type spatial;
render_mode blend_mix, cull_disabled, depth_draw_opaque, specular_schlick_ggx;
uniform float detail = 1.0;
varying flat vec2 v_code;
varying vec3 v_obj;
varying vec3 v_objn;
""" + Common + """
void vertex() { v_code = UV; v_obj = VERTEX; v_objn = NORMAL; }
void fragment() {
    vec3 tint = to_linear(COLOR.rgb);
    float a = COLOR.a;
    int code = int(v_code.x + 0.5);
    ALBEDO = tint; ALPHA = a; ROUGHNESS = 0.08; METALLIC = 0.2; SPECULAR = 0.5;
    if ((code & 63) == 41 && detail > 0.5) {
        int wear = (code >> 8) & 3;
        int edges = (code >> 20) & 15;
        int param = (code >> 6) & 3;
        vec3 n = normalize(v_objn);
        vec3 U; vec3 V;
        if (abs(n.y) > 0.9) { U = vec3(1.0, 0.0, 0.0); V = vec3(0.0, 0.0, 1.0); }
        else { V = normalize(vec3(0.0, 1.0, 0.0) - n * n.y); U = normalize(cross(V, n)); }
        vec2 uv = vec2(dot(v_obj, U), dot(v_obj, V));
        vec2 f = fract(uv + 1e-4);
        float px = max(max(length(dFdx(v_obj)), length(dFdy(v_obj))), 1e-5);
        float wl = (edges & 1) != 0 ? 0.016 : 0.055, wr = (edges & 2) != 0 ? 0.016 : 0.055;
        float wb = (edges & 4) != 0 ? 0.016 : 0.07, wt = (edges & 8) != 0 ? 0.016 : 0.055;
        float frame = max(max(1.0 - aastep(wl, f.x), 1.0 - aastep(wr, 1.0 - f.x)), max(1.0 - aastep(wb, f.y), 1.0 - aastep(wt, 1.0 - f.y)));
        frame *= vis(4.0, px) * 0.85 + 0.15;
        float fres = pow(1.0 - clamp(abs(dot(NORMAL, VIEW)), 0.0, 1.0), 3.0);
        float seed = hash3(floor(v_obj - n * 0.5) + 0.37);
        float W = float(wear) / 3.0;
        // Old glass: filmed with dirt, thickest low and in the corners; the oldest cracked from a strike, or gone.
        float corner = 1.0 - smoothstep(0.0, 0.18, min(min(f.x, 1.0 - f.x), min(f.y, 1.0 - f.y)));
        float dirt = clamp(W * (0.18 + 0.3 * fbm3(v_obj * 3.0)) + corner * W * 0.3 + (1.0 - f.y) * W * 0.12, 0.0, 0.75);
        float crazed = 0.0;
        if (wear >= 2 && seed < 0.35 * W) {
            vec2 q = f - vec2(0.3 + 0.4 * hash3(vec3(seed, 1.0, 2.0)), 0.3 + 0.4 * hash3(vec3(seed, 3.0, 4.0)));
            float r = length(q);
            float sp = (atan(q.y, q.x) / 6.2832 + seed) * 9.0;
            crazed = (1.0 - aastep(0.006, abs(fract(sp) - 0.5) / 9.0 * 6.2832 * r)) * (1.0 - aastep(0.5 + 0.4 * W, r));
            if (wear == 3 && seed < 0.12 && frame < 0.5 && crazed < 0.5 && noise3(vec3(f * 6.0, seed * 9.0)) > 0.35) discard;
        }
        vec3 fc = param == 1 ? vec3(0.03, 0.032, 0.035) : param == 2 ? vec3(0.12, 0.08, 0.05) : vec3(0.5, 0.49, 0.46);
        fc *= 1.0 - W * 0.4;
        vec3 pane = mix(tint * 0.3, vec3(0.2, 0.18, 0.14), dirt);
        ALBEDO = mix(mix(pane, vec3(0.75, 0.8, 0.82), crazed), fc, frame);
        ALPHA = mix(clamp(a * 0.75 + fres * 0.4 + dirt * 0.8 + crazed * 0.6, 0.0, 1.0), 1.0, frame);
        ROUGHNESS = mix(mix(0.04, 0.6, dirt), 0.55, frame);
        METALLIC = param == 1 ? mix(0.2, 0.6, frame) : mix(0.2, 0.0, frame);
        SPECULAR = 0.7;
    }
}
""";
}
