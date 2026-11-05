using System;
using Godot;
using Weather.Model;
using PlanetSurface.World;

namespace PlanetSurface.WeatherView;

/// <summary>
/// Ported from the weather prototype. One tornado's visuals: two nested funnel shells (a shader on a lathed trumpet mesh), a swarm
/// of debris and a dust skirt orbiting the base (particles moved by a particle shader). While
/// the tornado forms, the funnel lowers out of the cloud base: the visible warning before it
/// touches down.
/// </summary>
public partial class TornadoFx : Node3D
{
    public WeatherRig Fx;
    public TornadoRules Rules;

    const int DebrisCount = 160, DustCount = 220;

    ShaderMaterial _outer, _inner, _debrisProcess, _dustProcess;
    GpuParticles3D _debris, _dust;
    Vector3 _lastPosition;
    Vector2 _lean;

    public override void _Ready()
    {
        Shader funnel = GD.Load<Shader>("res://shaders/funnel.gdshader");
        float height = Rules.VisualHeight;
        _outer = FunnelMaterial(funnel, spin: 0.9f, opacity: 0.85f, new Color("4a3e32"), new Color("6a6e66"));
        _inner = FunnelMaterial(funnel, spin: 1.6f, opacity: 0.95f, new Color("261c14"), new Color("3c3e3a"));
        AddChild(new MeshInstance3D { Mesh = Lathe(height, 1f), MaterialOverride = _outer, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off });
        AddChild(new MeshInstance3D { Mesh = Lathe(height, 0.62f), MaterialOverride = _inner, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off });

        Shader orbit = GD.Load<Shader>("res://shaders/vortex_particles.gdshader");
        _debrisProcess = new ShaderMaterial { Shader = orbit };
        Set(_debrisProcess, ("radius_min", 3f), ("radius_max", 14f), ("height_max", 45f), ("spin", 2.6f), ("rise", 0.09f),
            ("size_min", 0.25f), ("size_max", 1.1f), ("flare", 0.9f));
        _debrisProcess.SetShaderParameter("tint", new Color(0.24f, 0.2f, 0.16f, 1f));
        _debris = Particles(DebrisCount, _debrisProcess, new BoxMesh { Size = new Vector3(1f, 0.25f, 0.6f) },
            new StandardMaterial3D { VertexColorUseAsAlbedo = true, Roughness = 0.9f, Transparency = BaseMaterial3D.TransparencyEnum.Alpha });

        _dustProcess = new ShaderMaterial { Shader = orbit };
        Set(_dustProcess, ("radius_min", 5f), ("radius_max", 26f), ("height_max", 16f), ("spin", 1.6f), ("rise", 0.12f),
            ("size_min", 5f), ("size_max", 11f), ("flare", 0.5f));
        _dustProcess.SetShaderParameter("tint", new Color(0.36f, 0.3f, 0.24f, 0.6f));
        _dust = Particles(DustCount, _dustProcess, new QuadMesh { Size = Vector2.One },
            new StandardMaterial3D
            {
                ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded, Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
                BillboardMode = BaseMaterial3D.BillboardModeEnum.Enabled, BillboardKeepScale = true,
                VertexColorUseAsAlbedo = true, AlbedoTexture = Fx.SoftDot, DepthDrawMode = BaseMaterial3D.DepthDrawModeEnum.Disabled,
            });
    }

    ShaderMaterial FunnelMaterial(Shader shader, float spin, float opacity, Color low, Color high)
    {
        var m = new ShaderMaterial { Shader = shader };
        m.SetShaderParameter("noise_tex", Fx.CloudNoise);
        m.SetShaderParameter("spin", spin);
        m.SetShaderParameter("opacity", opacity);
        m.SetShaderParameter("color_low", low);
        m.SetShaderParameter("color_high", high);
        m.SetShaderParameter("to_sun", Fx.ToSun);
        m.SetShaderParameter("height", Rules.VisualHeight);
        return m;
    }

    GpuParticles3D Particles(int amount, ShaderMaterial process, Mesh mesh, Material draw)
    {
        var p = new GpuParticles3D
        {
            Amount = amount, Lifetime = 8f, Explosiveness = 0f, LocalCoords = true,
            ProcessMaterial = process, DrawPass1 = mesh, MaterialOverride = draw, AmountRatio = 0f,
            VisibilityAabb = new Aabb(new Vector3(-60, -5, -60), new Vector3(120, 70, 120)),
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        };
        AddChild(p);
        return p;
    }

    static void Set(ShaderMaterial m, params (string name, float value)[] values)
    {
        foreach (var (name, value) in values) m.SetShaderParameter(name, value);
    }

    public int ActiveParticles() => (int)(_debris.Amount * _debris.AmountRatio) + (int)(_dust.Amount * _dust.AmountRatio);

    /// <summary>Called every physics tick, so the node rides physics interpolation like everything else.</summary>
    public void Follow(Tornado t)
    {
        Vector3 moved = t.Position - _lastPosition;
        _lastPosition = t.Position;
        GlobalPosition = t.Position;
        // The top lags behind the base as it travels.
        var target = new Vector2(-moved.X, -moved.Z) * 60f * 3.5f;
        _lean = _lean.Lerp(target.LimitLength(40f), 0.02f);

        float strength = t.Strength;
        float formation = t.Formation;
        foreach (ShaderMaterial m in new[] { _outer, _inner })
        {
            m.SetShaderParameter("formation", Mathf.Lerp(0.15f, 1f, formation));
            m.SetShaderParameter("lean", _lean);
        }
        _outer.SetShaderParameter("opacity", 0.85f * (t.Touchdown ? strength : 0.6f));
        _inner.SetShaderParameter("opacity", 0.95f * (t.Touchdown ? strength : 0.5f * formation));
        _debris.AmountRatio = t.Touchdown ? strength : 0f;
        _dust.AmountRatio = t.Touchdown ? strength : 0f;
        _debrisProcess.SetShaderParameter("strength", strength);
        _dustProcess.SetShaderParameter("strength", strength);
    }

    /// <summary>
    /// A trumpet: a narrow rope at the ground flaring into the cloud base. UV.x runs around,
    /// UV.y from 0 at the ground to 1 at the top.
    /// </summary>
    static ArrayMesh Lathe(float height, float scale)
    {
        const int rings = 28, sides = 28;
        var vertices = new Vector3[(rings + 1) * (sides + 1)];
        var normals = new Vector3[vertices.Length];
        var uvs = new Vector2[vertices.Length];
        for (int r = 0; r <= rings; r++)
        {
            float h = r / (float)rings;
            float radius = (3.2f + 38f * MathF.Pow(h, 2.3f)) * scale;
            float slope = 38f * 2.3f * MathF.Pow(MathF.Max(h, 0.001f), 1.3f) * scale / height;
            for (int s = 0; s <= sides; s++)
            {
                float a = s / (float)sides * MathF.Tau;
                var dir = new Vector3(MathF.Cos(a), 0f, MathF.Sin(a));
                int i = r * (sides + 1) + s;
                vertices[i] = dir * radius + Vector3.Up * h * height;
                normals[i] = (dir - Vector3.Up * slope).Normalized();
                uvs[i] = new Vector2(s / (float)sides, h);
            }
        }
        var indices = new int[rings * sides * 6];
        int k = 0;
        for (int r = 0; r < rings; r++)
        for (int s = 0; s < sides; s++)
        {
            int a = r * (sides + 1) + s, b = a + 1, c = a + sides + 1, d = c + 1;
            indices[k++] = a; indices[k++] = c; indices[k++] = b;
            indices[k++] = b; indices[k++] = c; indices[k++] = d;
        }
        var arrays = new Godot.Collections.Array();
        arrays.Resize((int)Mesh.ArrayType.Max);
        arrays[(int)Mesh.ArrayType.Vertex] = vertices;
        arrays[(int)Mesh.ArrayType.Normal] = normals;
        arrays[(int)Mesh.ArrayType.TexUV] = uvs;
        arrays[(int)Mesh.ArrayType.Index] = indices;
        var mesh = new ArrayMesh();
        mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays);
        return mesh;
    }
}
