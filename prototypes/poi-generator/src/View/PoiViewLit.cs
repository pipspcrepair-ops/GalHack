using System.Collections.Generic;
using Godot;
using PoiGenerator.Gen;

namespace PoiGenerator;

public partial class PoiView
{
    /// <summary>The glowing paint on faces (<see cref="PoiInstance.LitFaces"/>): a thin quad per face that only glows. Same formulas as LightSettings and the slice's block shader.</summary>
    public readonly List<MeshInstance3D> LitQuads = new();

    static Shader _litShader;

    void BuildLitFaces()
    {
        _litShader ??= new Shader { Code = LitCode };
        var quad = new QuadMesh { Size = Vector2.One };
        foreach (LitFace f in Poi.LitFaces)
        {
            var st = f.Style;
            var mat = new ShaderMaterial { Shader = _litShader };
            mat.SetShaderParameter("c1", HexColor(st.Color));
            mat.SetShaderParameter("c2", HexColor(st.Color2));
            mat.SetShaderParameter("anim", (int)st.Anim);
            mat.SetShaderParameter("speed", st.Speed);
            mat.SetShaderParameter("phase", (st.Seed >> 1) / 128f);
            mat.SetShaderParameter("shape", (int)f.Shape);
            Vector3 n = Look.ToVector(f.Face);
            Vector3 up = Mathf.Abs(n.Y) > 0.9f ? Vector3.Forward : Vector3.Up;
            var q = new MeshInstance3D
            {
                Name = "Lit", Mesh = quad, MaterialOverride = mat, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
                Basis = Basis.LookingAt(-n, up),
                Position = new Vector3(f.Cell.X + 0.5f, f.Cell.Y + 0.5f, f.Cell.Z + 0.5f) + n * 0.508f,
            };
            AddChild(q);
            LitQuads.Add(q);
        }
    }

    static Color HexColor(uint c) => new(((c >> 16) & 255) / 255f, ((c >> 8) & 255) / 255f, (c & 255) / 255f);

    const string LitCode = @"
shader_type spatial;
render_mode blend_mix, depth_draw_never, cull_back, unshaded;
uniform vec3 c1 : source_color;
uniform vec3 c2 : source_color;
uniform int anim = 0;
uniform float speed = 0.5;
uniform float phase = 0.0;
uniform int shape = 1;
float lamp_hash(float p) { return fract(sin(p * 127.1) * 43758.5453); }
void fragment() {
    vec2 f = UV;
    float edge = min(min(f.x, 1.0 - f.x), min(f.y, 1.0 - f.y));
    float gm = 1.0;
    if (shape == 2) gm = smoothstep(0.38, 0.42, f.y) * (1.0 - smoothstep(0.58, 0.62, f.y));
    else if (shape == 3) gm = smoothstep(0.16, 0.2, f.x) * (1.0 - smoothstep(0.8, 0.84, f.x)) * smoothstep(0.16, 0.2, f.y) * (1.0 - smoothstep(0.8, 0.84, f.y));
    else if (shape == 4) gm = (1.0 - smoothstep(0.12, 0.16, edge)) * smoothstep(0.03, 0.06, edge);
    float x = TIME * speed + phase;
    float k = 1.0;
    vec3 col = c1;
    if (anim == 1) k = 0.12 + 0.88 * (0.5 + 0.5 * sin(6.2831853 * x));
    else if (anim == 2) {
        float v = lamp_hash(floor(x * 7.0)) * 0.6 + lamp_hash(floor(x * 3.1) + 31.0) * 0.4;
        k = v < 0.28 ? 0.06 : 0.45 + 0.55 * v;
    }
    else if (anim == 3) k = fract(x * 2.0) < 0.14 ? 1.0 : 0.03;
    else if (anim == 4) col = mix(c1, c2, 0.5 + 0.5 * sin(6.2831853 * x));
    ALBEDO = col * k * 2.2;
    ALPHA = gm;
}
";
}
