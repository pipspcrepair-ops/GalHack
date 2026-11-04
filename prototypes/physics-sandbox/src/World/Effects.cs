using System.Collections.Generic;
using Godot;
using ShipHandling.Handling;

namespace PhysicsSandbox.World;

/// <summary>
/// Thruster flames and hover-pad glow for one vessel, from what its flight model did this tick
/// (the handling model's ship-local thrust). Presentation only: it reads, never moves anything.
/// </summary>
public partial class ShipFx : Node3D
{
    Ship _ship;
    readonly List<(MeshInstance3D flame, Vector3 nozzle, Vector3 exhaust, Vector3 push)> _flames = new();
    readonly List<MeshInstance3D> _pads = new();
    static StandardMaterial3D _flameMaterial, _padMaterial;

    public void Setup(Ship ship)
    {
        _ship = ship;
        _flameMaterial ??= Glow(Palette.Flame, 3f);
        _padMaterial ??= Glow(Palette.HoverGlow, 2f);
        float cell = ship.Structure.Grid.CellSize;
        foreach (ThrusterSpec t in ship.Spec.Thrusters)
        {
            Vector3 push = t.Facing.Normalized(), exhaust = -push;
            // The cone's wide end (+Y) sits on the nozzle; its point trails off downstream.
            var flame = new MeshInstance3D
            {
                Mesh = new CylinderMesh { TopRadius = cell * 0.32f, BottomRadius = 0.02f, Height = 1f, RadialSegments = 10, Rings = 1 },
                MaterialOverride = _flameMaterial,
                CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
                Basis = push.Dot(Vector3.Up) < -0.999f ? new Basis(Vector3.Right, Mathf.Pi) : new Basis(new Quaternion(Vector3.Up, push)),
                Visible = false,
            };
            AddChild(flame);
            _flames.Add((flame, t.Position + exhaust * (cell * 0.5f), exhaust, push));
        }
        foreach (HoverEngineSpec h in ship.Spec.HoverEngines)
        {
            var pad = new MeshInstance3D
            {
                Mesh = new CylinderMesh { TopRadius = cell * 0.42f, BottomRadius = cell * 0.55f, Height = 0.05f },
                MaterialOverride = _padMaterial,
                Position = h.Position + Vector3.Down * 0.04f,
                CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            };
            AddChild(pad);
            _pads.Add(pad);
        }
    }

    static StandardMaterial3D Glow(Color c, float energy) => new()
    {
        AlbedoColor = new Color(c, 0.85f),
        EmissionEnabled = true,
        Emission = c,
        EmissionEnergyMultiplier = energy,
        ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
        Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
        BlendMode = BaseMaterial3D.BlendModeEnum.Add,
        CullMode = BaseMaterial3D.CullModeEnum.Disabled,
    };

    public override void _PhysicsProcess(double delta)
    {
        if (_ship?.Flight == null) return;
        bool live = !_ship.IsWreck && !_ship.Body.Asleep && _ship.Structure.Core != null;
        Vector3 thrust = _ship.Flight.State.ThrustLocal;
        float boost = _ship.Flight.State.Boosting ? 1.5f : 1f;
        foreach (var (flame, nozzle, exhaust, push) in _flames)
        {
            float effort = live ? Mathf.Clamp(thrust.Dot(push) / 10f, 0f, 1f) : 0f;
            float length = effort > 0.03f ? 0.3f + 2.7f * effort * boost : 0f;
            flame.Visible = length > 0.05f;
            flame.Scale = new Vector3(1f, Mathf.Max(length, 0.01f), 1f);
            flame.Position = nozzle + exhaust * (length * 0.5f);
        }
        foreach (MeshInstance3D pad in _pads) pad.Visible = live;
    }
}

/// <summary>
/// A tornado as the weather model sees it: a dusty, swirling funnel that lowers from the cloud
/// base while it forms, thickens at touchdown and fades as it dissipates. Presentation only.
/// </summary>
public partial class TornadoView : Node3D
{
    public Weather.Model.WeatherSim Sim;
    readonly List<(MeshInstance3D funnel, MeshInstance3D dust)> _funnels = new();
    Shader _shader;

    public override void _Process(double delta)
    {
        _shader ??= new Shader { Code = FunnelShader };
        var tornadoes = Sim?.Tornadoes;
        int n = tornadoes?.Count ?? 0;
        while (_funnels.Count < n)
        {
            var funnel = new MeshInstance3D
            {
                Mesh = new CylinderMesh { TopRadius = 46f, BottomRadius = 6f, Height = 1f, RadialSegments = 32, Rings = 16 },
                MaterialOverride = new ShaderMaterial { Shader = _shader },
                CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            };
            // The debris cloud it kicks up where it touches the ground.
            var dust = new MeshInstance3D
            {
                Mesh = new SphereMesh { Radius = 1f, Height = 2f, RadialSegments = 24, Rings = 12 },
                MaterialOverride = new ShaderMaterial { Shader = _shader },
                CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
                Scale = new Vector3(16f, 7f, 16f),
            };
            AddChild(funnel);
            AddChild(dust);
            _funnels.Add((funnel, dust));
        }
        for (int i = 0; i < _funnels.Count; i++)
        {
            var (f, dust) = _funnels[i];
            f.Visible = dust.Visible = i < n;
            if (i >= n) continue;
            var t = tornadoes[i];
            float height = t.Rules.VisualHeight;
            float span = height * Mathf.Max(0.15f, t.Formation); // lowers out of the cloud base while it forms
            f.Scale = new Vector3(1f, span, 1f);
            f.Position = t.Position + Vector3.Up * (height - span * 0.5f);
            float strength = t.Touchdown ? Mathf.Max(0.25f, t.Strength) : 0.35f * t.Formation;
            ((ShaderMaterial)f.MaterialOverride).SetShaderParameter("strength", strength);
            dust.Position = t.Position + Vector3.Up * 2f;
            dust.Visible = t.Touchdown;
            ((ShaderMaterial)dust.MaterialOverride).SetShaderParameter("strength", strength * 0.8f);
        }
    }

    const string FunnelShader = @"
shader_type spatial;
render_mode unshaded, blend_mix, cull_disabled, depth_draw_never, shadows_disabled;
uniform vec4 tint : source_color = vec4(0.42, 0.30, 0.24, 1.0);
uniform float strength = 1.0;
varying vec3 lp;

float hash(vec2 p) { return fract(sin(dot(p, vec2(127.1, 311.7))) * 43758.5453); }
float noise(vec2 p) {
    vec2 i = floor(p), f = fract(p);
    f = f * f * (3.0 - 2.0 * f);
    return mix(mix(hash(i), hash(i + vec2(1, 0)), f.x), mix(hash(i + vec2(0, 1)), hash(i + vec2(1, 1)), f.x), f.y);
}

void vertex() { lp = VERTEX; }

void fragment() {
    float a = atan(lp.z, lp.x);
    float h = lp.y;
    // Spiral bands that wind up the funnel and race round it.
    float n = noise(vec2(a * 2.0 + h * 28.0 - TIME * 4.0, h * 34.0 - TIME * 0.8)) * 0.6
            + noise(vec2(a * 6.0 + h * 60.0 - TIME * 7.0, h * 90.0)) * 0.4;
    float rim = 1.0 - abs(dot(normalize(NORMAL), normalize(VIEW)));
    float streak = smoothstep(0.35, 0.75, n);
    ALBEDO = tint.rgb * (0.55 + 0.75 * n);
    float low = 1.0 - smoothstep(-0.5, 0.3, h); // denser near the ground, where the debris is
    ALPHA = clamp(tint.a * strength * (0.3 + 0.7 * streak) * (0.6 + 0.4 * rim) * (0.75 + 0.5 * low), 0.0, 1.0);
}
";
}
