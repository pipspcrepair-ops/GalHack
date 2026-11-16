using System;
using System.Collections.Generic;
using Godot;
using NV3 = System.Numerics.Vector3;

namespace Slice.Vessels;

/// <summary>
/// The worms sticking out of a Husk ship's meat (DESIGN, Husks; owner 16 Nov: "The Husk ships should have warms sticking
/// out of the meat, which reach out to the ship they're going to ram once they set a heading"): every worm its form
/// rooted in a lump of its flesh, of its own length and thickness, writhing slowly while idle; given a target (the ship
/// it means to ram) they stretch and strain toward it together, and relax again after. One mesh for all of a ship's
/// worms, each a tube in its rest pose with its root, heading, length, thickness and phase in its vertices: the shader
/// bends every one of them each frame, so a ship's worms cost one draw and nothing on the CPU; gone past
/// <see cref="Range"/> metres. The hook the ram behaviour drives: <see cref="ReachToward"/> and <see cref="Relax"/>.
/// </summary>
public partial class HuskWorms : MeshInstance3D
{
    /// <summary>Drawn out to this distance (m), no further.</summary>
    public const float Range = 900f;
    /// <summary>How long a reach or a relax takes to come on (s).</summary>
    const float Ease = 2.5f;

    Vector3? _target;
    float _reach;

    /// <summary>A Husk ship's worms on its node (structure frame, metres), or null when it has none.</summary>
    public static HuskWorms Grow(Node3D ship, IReadOnlyList<(NV3 At, NV3 Out, float Length, float Thick)> worms, int seed)
    {
        if (worms.Count == 0) return null;
        var node = new HuskWorms { Name = "HuskWorms", Mesh = Tubes(worms, seed), MaterialOverride = Material, VisibilityRangeEnd = Range, CastShadow = ShadowCastingSetting.On };
        float far = 0f;
        foreach (var w in worms) far = MathF.Max(far, w.At.Length() + w.Length * 1.6f);
        node.CustomAabb = new Aabb(-Vector3.One * far, Vector3.One * far * 2f);
        ship.AddChild(node);
        return node;
    }

    /// <summary>Set a heading to ram: every worm stretches and strains toward this point (world space) until <see cref="Relax"/>.</summary>
    public void ReachToward(Vector3 target) => _target = target;

    /// <summary>Back to writhing where they are.</summary>
    public void Relax() => _target = null;

    /// <summary>How far they are reaching now: 0 idle, 1 strained toward the target.</summary>
    public float Reaching => _reach;

    public override void _Process(double delta)
    {
        _reach = Mathf.MoveToward(_reach, _target != null ? 1f : 0f, (float)delta / Ease);
        SetInstanceShaderParameter("reach", Mathf.SmoothStep(0f, 1f, _reach));
        if (_target is Vector3 t) SetInstanceShaderParameter("target", ToLocal(t));
    }

    const int Around = 8, Along = 18;

    /// <summary>Every worm a tube in its rest pose: its ring and place along it in UV, its root and length, heading and phase, thickness and wildness in the custom channels.</summary>
    static ArrayMesh Tubes(IReadOnlyList<(NV3 At, NV3 Out, float Length, float Thick)> worms, int seed)
    {
        var rng = new Random(seed);
        int per = (Around + 1) * (Along + 1);
        var verts = new Vector3[worms.Count * per];
        var normals = new Vector3[verts.Length];
        var uv = new Vector2[verts.Length];
        var c0 = new float[verts.Length * 4];
        var c1 = new float[verts.Length * 4];
        var c2 = new float[verts.Length * 4];
        var index = new List<int>(worms.Count * Around * Along * 6);
        for (int w = 0; w < worms.Count; w++)
        {
            var (at, dir, length, thick) = worms[w];
            float phase = (float)rng.NextDouble() * 100f, wild = 0.2f + (float)rng.NextDouble() * 0.35f, speed = 0.5f + (float)rng.NextDouble() * 0.6f;
            for (int j = 0; j <= Along; j++)
                for (int i = 0; i <= Around; i++)
                {
                    int k = w * per + j * (Around + 1) + i;
                    float s = j / (float)Along;
                    // (A rough rest pose, for the bounds and the editor: the shader places every vertex itself.)
                    verts[k] = new Vector3(at.X, at.Y, at.Z) + new Vector3(dir.X, dir.Y, dir.Z) * length * s;
                    normals[k] = new Vector3(dir.X, dir.Y, dir.Z);
                    uv[k] = new Vector2(i / (float)Around, s);
                    (c0[k * 4], c0[k * 4 + 1], c0[k * 4 + 2], c0[k * 4 + 3]) = (at.X, at.Y, at.Z, length);
                    (c1[k * 4], c1[k * 4 + 1], c1[k * 4 + 2], c1[k * 4 + 3]) = (dir.X, dir.Y, dir.Z, phase);
                    (c2[k * 4], c2[k * 4 + 1], c2[k * 4 + 2], c2[k * 4 + 3]) = (thick, wild, speed, 0f);
                }
            for (int j = 0; j < Along; j++)
                for (int i = 0; i < Around; i++)
                {
                    int a = w * per + j * (Around + 1) + i, b = a + Around + 1;
                    index.AddRange(new[] { a, b, a + 1, a + 1, b, b + 1 });
                }
        }
        var arrays = new Godot.Collections.Array();
        arrays.Resize((int)Mesh.ArrayType.Max);
        arrays[(int)Mesh.ArrayType.Vertex] = verts;
        arrays[(int)Mesh.ArrayType.Normal] = normals;
        arrays[(int)Mesh.ArrayType.TexUV] = uv;
        arrays[(int)Mesh.ArrayType.Custom0] = c0;
        arrays[(int)Mesh.ArrayType.Custom1] = c1;
        arrays[(int)Mesh.ArrayType.Custom2] = c2;
        arrays[(int)Mesh.ArrayType.Index] = index.ToArray();
        var format = (Mesh.ArrayFormat)((long)Mesh.ArrayCustomFormat.RgbaFloat << (int)Mesh.ArrayFormat.FormatCustom0Shift
                                      | (long)Mesh.ArrayCustomFormat.RgbaFloat << (int)Mesh.ArrayFormat.FormatCustom1Shift
                                      | (long)Mesh.ArrayCustomFormat.RgbaFloat << (int)Mesh.ArrayFormat.FormatCustom2Shift);
        var mesh = new ArrayMesh();
        mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays, null, null, format);
        return mesh;
    }

    static ShaderMaterial _material;

    /// <summary>The worms' one material: the shader that writhes and reaches them, flesh banded in rings, glossy and darker at the root.</summary>
    static ShaderMaterial Material => _material ??= new ShaderMaterial { Shader = new Shader { Code = Code } };

    const string Code = @"
shader_type spatial;
render_mode cull_back;

instance uniform float reach = 0.0;
instance uniform vec3 target = vec3(0.0, 0.0, 1000.0);
uniform vec3 flesh : source_color = vec3(0.45, 0.16, 0.22);
uniform vec3 pale : source_color = vec3(0.78, 0.47, 0.5);

varying float along;
varying float rings;

// A worm's middle line at s (0 its root, 1 its tip), at time t.
vec3 spine(vec3 root, vec3 dir, float len, float phase, float wild, float speed, float s, float t, vec3 aim, float r) {
    vec3 side = normalize(cross(dir, abs(dir.y) < 0.9 ? vec3(0.0, 1.0, 0.0) : vec3(1.0, 0.0, 0.0)));
    vec3 up = cross(dir, side);
    // Idle: out from the meat, curling slowly about its heading, the more toward its tip.
    float a = t * 0.35 * speed + phase;
    vec3 sway = side * sin(a + s * 3.1) + up * cos(a * 0.8 + phase * 1.7 + s * 2.6);
    vec3 idle = root + dir * len * s * (1.0 - 0.25 * s) + sway * wild * len * s * s;
    // Reaching: a curve from its heading round to the target, stretched half again, straining with a fast tremor.
    vec3 bend = root + dir * len * 0.35;
    vec3 tip = root + aim * len * 1.5;
    vec3 strained = mix(mix(root, bend, s), mix(bend, tip, s), s);
    strained += (side * sin(t * 9.0 + phase + s * 11.0) + up * cos(t * 7.0 + phase + s * 9.0)) * 0.03 * len * s;
    return mix(idle, strained, r);
}

void vertex() {
    vec3 root = CUSTOM0.xyz; float len = CUSTOM0.w;
    vec3 dir = normalize(CUSTOM1.xyz); float phase = CUSTOM1.w;
    float thick = CUSTOM2.x, wild = CUSTOM2.y, speed = CUSTOM2.z;
    float s = UV.y, t = TIME;
    vec3 to = target - root;
    vec3 aim = length(to) > 0.01 ? normalize(to) : dir;
    vec3 p = spine(root, dir, len, phase, wild, speed, s, t, aim, reach);
    vec3 q = spine(root, dir, len, phase, wild, speed, min(s + 0.02, 1.0), t, aim, reach);
    vec3 b = spine(root, dir, len, phase, wild, speed, max(s - 0.02, 0.0), t, aim, reach);
    vec3 tangent = normalize(q - b + dir * 1e-4);
    vec3 n1 = normalize(cross(tangent, abs(tangent.y) < 0.9 ? vec3(0.0, 1.0, 0.0) : vec3(1.0, 0.0, 0.0)));
    vec3 n2 = cross(tangent, n1);
    float ang = UV.x * 6.2831853;
    vec3 out_ = n1 * cos(ang) + n2 * sin(ang);
    // Thick at the root, tapering to a blunt tip, thinner as it stretches.
    float r = thick * (1.0 - 0.75 * s) * (1.0 - 0.3 * reach) * (1.0 + 0.12 * sin(s * len * 2.2 + phase));
    VERTEX = p + out_ * r;
    NORMAL = out_;
    along = s;
    rings = s * len;
}

void fragment() {
    float band = 0.82 + 0.18 * sin(rings * 6.0);
    ALBEDO = mix(flesh, pale, along * 0.6) * band;
    ROUGHNESS = 0.35;
    SPECULAR = 0.6;
}
";
}
