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
    public IWeatherScene Fx;
    public TornadoRules Rules;

    /// <summary>The ground's own colour where it whirls: a dust devil and a tornado's skirt are made of it.</summary>
    public Color Soil = new(0.42f, 0.36f, 0.3f);

    TornadoKind _kind = TornadoKind.Tornado;
    bool _dressed;

    const int DebrisCount = 160, DustCount = 220;

    ShaderMaterial _outer, _inner, _debrisProcess, _dustProcess;
    GpuParticles3D _debris, _dust;
    Vector3 _lastPosition;
    Vector2 _lean;
    MeshInstance3D _outerMesh, _innerMesh, _wall;
    ShaderMaterial _wallMaterial;

    public override void _Ready()
    {
        Shader funnel = GD.Load<Shader>("res://shaders/funnel.gdshader");
        float height = Rules.VisualHeight;
        _outer = FunnelMaterial(funnel, spin: 0.9f, opacity: 0.85f, new Color("4a3e32"), new Color("6a6e66"));
        _inner = FunnelMaterial(funnel, spin: 1.6f, opacity: 0.95f, new Color("261c14"), new Color("3c3e3a"));
        AddChild(_outerMesh = new MeshInstance3D { Mesh = Lathe(height, 1f), MaterialOverride = _outer, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off });
        AddChild(_innerMesh = new MeshInstance3D { Mesh = Lathe(height, 0.62f), MaterialOverride = _inner, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off });

        _wallMaterial = new ShaderMaterial { Shader = GD.Load<Shader>("res://shaders/wallcloud.gdshader") };
        _wallMaterial.SetShaderParameter("noise_tex", Fx.CloudNoise);
        AddChild(_wall = new MeshInstance3D
        {
            Name = "WallCloud", Mesh = new SphereMesh { Radius = 1f, Height = 2f, RadialSegments = 32, Rings = 16 }, MaterialOverride = _wallMaterial,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off, Visible = false,
        });

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

    /// <summary>
    /// Stretches the funnel so its top stands at <paramref name="top"/> metres over the ground: the
    /// dark base of the storm tower above it, whatever height that cloud hangs at. It widens a little as it lengthens.
    /// </summary>
    public void ReachTo(float top)
    {
        if (_outerMesh == null) return;
        float k = Mathf.Max(top, _kind == TornadoKind.DustDevil ? 3f : 20f) / Mathf.Max(Rules.VisualHeight, 1f);
        // A waterspout stays a thin rope however tall; a dust devil is as wide as its whirl, a few metres to some tens.
        float wide = _kind switch
        {
            TornadoKind.Waterspout => Mathf.Clamp(MathF.Sqrt(k) * 0.55f, 0.35f, 0.9f),
            TornadoKind.DustDevil => Mathf.Clamp(k * 1.1f, 0.12f, 1.1f),
            _ => Mathf.Clamp(MathF.Sqrt(k), 0.6f, 2.5f),
        };
        var scale = new Vector3(wide, k, wide);
        _outerMesh.Scale = scale;
        _innerMesh.Scale = scale;
        // Its top enters the storm's own lowered base: the sky draws that bulge, so there is no separate cloud here.
        _wall.Visible = false;
        // The lean and snake are in the funnel's own metres: they stretch with it, so its top still sits over the tower.
    }

    static float Fract(float x) => x - MathF.Floor(x);

    /// <summary>
    /// Where funnel.gdshader puts the centre of its line at height share <paramref name="h"/> (0 the ground,
    /// 1 the cloud), off the node, in the funnel's own metres: the same sums, so tests can hold the drawn
    /// foot to the force's centre. At the ground it is always nothing.
    /// </summary>
    public static Vector2 CentreLine(float h, float time, float seed, float sBend, Vector2 bow, Vector2 lean, float decay)
    {
        float bowed = MathF.Sin(MathF.PI * h) + sBend * (MathF.Sin(MathF.Tau * h + time * 0.2f + seed) - MathF.Sin(time * 0.2f + seed));
        Vector2 at = bow * bowed * (1f + 3f * decay);
        at += new Vector2(MathF.Cos(seed), MathF.Sin(seed)) * decay * 50f * (MathF.Sin(h * 14f + time * 0.8f) - MathF.Sin(time * 0.8f)) * (1f - h);
        float snake = (MathF.Sin(time * 0.7f + h * 5f) - MathF.Sin(time * 0.7f)) * 3.5f * (1f - h) + MathF.Sin(time * 0.31f + h * 2f) * 6f * h;
        return at + lean * h * h + new Vector2(snake, snake * 0.6f);
    }

    /// <summary>The drawn foot of the funnel in the world: the node, plus the shader's line at the ground (always nothing).</summary>
    public Vector3 Foot => GlobalPosition + new Vector3(CentreLine(0f, (float)Time.GetTicksMsec() / 1000f, _seed, _sBend, _bow, _lean, _decay).X, 0f,
        CentreLine(0f, (float)Time.GetTicksMsec() / 1000f, _seed, _sBend, _bow, _lean, _decay).Y);

    float _seed, _sBend = 0.4f, _decay;
    Vector2 _bow;

    /// <summary>
    /// Its looks by kind: a tornado dark with dust and debris; a waterspout a pale rope over a white ring of
    /// spray (there even where the funnel stops short); a dust devil the soil's own colour, a whirling skirt
    /// at its foot and the column thinning out into clear air.
    /// </summary>
    void Dress(TornadoKind kind)
    {
        _kind = kind;
        _dressed = true;
        Color soil = Soil;
        (Color, Color, Color, Color) looks = kind switch
        {
            TornadoKind.Waterspout => (new Color(0.74f, 0.77f, 0.8f), new Color(0.6f, 0.63f, 0.67f), new Color(0.64f, 0.67f, 0.7f), new Color(0.52f, 0.55f, 0.59f)),
            TornadoKind.DustDevil => (soil * 0.95f, soil.Lerp(Colors.White, 0.15f), soil * 0.8f, soil),
            _ => (new Color("4a3e32"), new Color("6a6e66"), new Color("261c14"), new Color("3c3e3a")),
        };
        _outer.SetShaderParameter("color_low", looks.Item1);
        _outer.SetShaderParameter("color_high", looks.Item2);
        _inner.SetShaderParameter("color_low", looks.Item3);
        _inner.SetShaderParameter("color_high", looks.Item4);
        foreach (ShaderMaterial m in new[] { _outer, _inner })
        {
            m.SetShaderParameter("s_bend", _sBend = kind == TornadoKind.Tornado ? 0.4f : kind == TornadoKind.Waterspout ? 1.1f : 0.7f);
            m.SetShaderParameter("top_fade", kind == TornadoKind.DustDevil ? 1f : 0f);
        }
        switch (kind)
        {
            case TornadoKind.Waterspout:
                // The spray ring on the water at its foot.
                Set(_dustProcess, ("radius_min", 5f), ("radius_max", 24f), ("height_max", 7f), ("spin", 1.9f), ("rise", 0.2f), ("size_min", 3f), ("size_max", 7f), ("flare", 0.25f));
                _dustProcess.SetShaderParameter("tint", new Color(0.92f, 0.95f, 0.97f, 0.55f));
                break;
            case TornadoKind.DustDevil:
                // The whirling skirt of lifted soil at its foot, dense and low.
                Set(_dustProcess, ("radius_min", 1.2f), ("radius_max", 9f), ("height_max", 9f), ("spin", 2.6f), ("rise", 0.18f), ("size_min", 1.6f), ("size_max", 4.5f), ("flare", 0.8f));
                _dustProcess.SetShaderParameter("tint", new Color(soil.R, soil.G, soil.B, 0.7f));
                break;
            default:
                Set(_dustProcess, ("radius_min", 5f), ("radius_max", 26f), ("height_max", 16f), ("spin", 1.6f), ("rise", 0.12f), ("size_min", 5f), ("size_max", 11f), ("flare", 0.5f));
                _dustProcess.SetShaderParameter("tint", new Color(0.36f, 0.3f, 0.24f, 0.6f));
                break;
        }
    }

    /// <summary>Called every physics tick, so the node rides physics interpolation like everything else.</summary>
    public void Follow(Tornado t)
    {
        if (!_dressed || t.Kind != _kind) Dress(t.Kind);
        Vector3 moved = t.Position - _lastPosition;
        _lastPosition = t.Position;
        GlobalPosition = t.Position;
        // The top lags behind the base as it travels.
        var target = new Vector2(-moved.X, -moved.Z) * 60f * 3.5f;
        _lean = _lean.Lerp(target.LimitLength(40f), 0.02f);

        float strength = t.Strength;
        float formation = t.Formation;
        // Each tornado has its own shape (rolled with its speed: fast ones ropes and cones, slow ones wedges);
        // it is a thin rope as it forms, matures into that shape and dies as a contorted rope. One the model
        // didn't shape (tests, the debug key) rolls its own here.
        float roll = t.Look >= 0f ? t.Look : Fract(MathF.Sin(t.Id * 12.9898f) * 43758.5453f);
        // Some are smooth pale funnels instead of ragged ones: a clean curving taper, bright in side light.
        float clean = t.Look >= 0f ? (t.Smooth ? 1f : 0f) : Fract(MathF.Sin(t.Id * 7.31f + 1.3f) * 9631.7f) > 0.55f ? 1f : 0f;
        if (clean > 0f && t.Look < 0f) roll = 0.6f + 0.3f * roll;
        // A waterspout is a rope all its life; a dust devil a column widening upward.
        if (_kind == TornadoKind.Waterspout) { roll = 0f; clean = 0f; }
        if (_kind == TornadoKind.DustDevil) { roll = 0.5f; clean = 0f; }
        float form = Math.Clamp((formation - 0.5f) / 0.5f, 0f, 1f);
        float shape = Mathf.Lerp(0f, roll, form * form * (3f - 2f * form));
        float decay = t.Dissipating ? 1f - strength : 0f;
        shape = Mathf.Lerp(shape, 0f, decay);
        float seed = t.Id * 1.7f;
        var bow = new Vector2(MathF.Cos(t.Id * 2.1f), MathF.Sin(t.Id * 2.1f)) * (_kind switch
        {
            TornadoKind.Waterspout => 25f + 50f * Fract(t.Id * 0.618f),
            TornadoKind.DustDevil => 1.5f + 3f * Fract(t.Id * 0.618f),
            _ => Mathf.Lerp(110f, 30f, shape),
        });
        foreach (ShaderMaterial m in new[] { _outer, _inner })
        {
            m.SetShaderParameter("shape", shape);
            m.SetShaderParameter("clean", clean * (1f - decay));
            m.SetShaderParameter("seed", seed);
            (_seed, _bow, _decay) = (seed, bow, decay);
            m.SetShaderParameter("decay", decay);
            m.SetShaderParameter("bow", bow);
            m.SetShaderParameter("formation", Mathf.Lerp(0.15f, 1f, formation));
            m.SetShaderParameter("lean", _lean);
        }
        _outer.SetShaderParameter("opacity", 0.85f * (t.Touchdown ? strength : 0.6f));
        _inner.SetShaderParameter("opacity", 0.95f * (t.Touchdown ? strength : 0.5f * formation));
        _debris.AmountRatio = t.Touchdown && _kind == TornadoKind.Tornado ? strength : 0f;
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
        const int rings = 72, sides = 36;
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
