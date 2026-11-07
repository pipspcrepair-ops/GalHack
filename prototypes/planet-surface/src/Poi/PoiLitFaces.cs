using System.Collections.Generic;
using Godot;
using GalHack.Structures;
using PoiGenerator.Gen;

namespace PlanetSurface.Poi;

/// <summary>
/// A place's glowing paint (<see cref="PoiInstance.LitFaces"/>) as thin quads that only glow, animated
/// on the GPU by the same formulas as <see cref="LightSettings"/> and the player's glowing paint. One
/// material per style, shared by every face that wears it.
/// </summary>
public static class PoiLitFaces
{
    static Shader _shader;
    static readonly QuadMesh Quad = new() { Size = Vector2.One };
    static readonly Dictionary<(LightSettings, LampShape), ShaderMaterial> Materials = new();

    /// <summary>Adds a quad per painted face under <paramref name="parent"/>, in the place's own cells.</summary>
    public static int Build(Node3D parent, PoiInstance inst)
    {
        if (inst.LitFaces.Count == 0) return 0;
        _shader ??= new Shader { Code = Code };
        foreach (LitFace f in inst.LitFaces)
        {
            if (!Materials.TryGetValue((f.Style, f.Shape), out ShaderMaterial mat))
            {
                mat = new ShaderMaterial { Shader = _shader };
                mat.SetShaderParameter("c1", Hex(f.Style.Color));
                mat.SetShaderParameter("c2", Hex(f.Style.Color2));
                mat.SetShaderParameter("anim", (int)f.Style.Anim);
                mat.SetShaderParameter("speed", f.Style.Speed);
                mat.SetShaderParameter("phase", (f.Style.Seed >> 1) / 128f);
                mat.SetShaderParameter("shape", (int)f.Shape);
                Materials[(f.Style, f.Shape)] = mat;
            }
            PoiGenerator.Gen.Int3 v = f.Face.Vec();
            var n = new Vector3(v.X, v.Y, v.Z);
            Vector3 up = Mathf.Abs(n.Y) > 0.9f ? Vector3.Forward : Vector3.Up;
            parent.AddChild(new MeshInstance3D
            {
                Name = "Lit", Mesh = Quad, MaterialOverride = mat, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
                Basis = Basis.LookingAt(-n, up),
                Position = new Vector3(f.Cell.X + 0.5f, f.Cell.Y + 0.5f, f.Cell.Z + 0.5f) + n * 0.508f,
            });
        }
        return inst.LitFaces.Count;
    }

    /// <summary>A styled lamp's brightness and colour now: its real light follows its animation.</summary>
    public static (float Energy, Color Colour) At(LightSettings style, float energy, double seconds) =>
        (energy * style.Intensity(seconds), Hex(style.ColorAt(seconds)));

    static Color Hex(uint c) => new(((c >> 16) & 255) / 255f, ((c >> 8) & 255) / 255f, (c & 255) / 255f);

    const string Code = @"
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
