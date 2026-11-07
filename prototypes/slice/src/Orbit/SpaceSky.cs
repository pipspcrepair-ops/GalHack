using System;
using System.Collections.Generic;
using Godot;
using Planets.Look;
using PlanetSurface.Skies;

namespace Slice.Orbit;

/// <summary>
/// Space round the start world: planet-generator's space sky (stars, the world's nebula, the sun's
/// glare), the sun as the one light (hard shadows, so a dead hull's inside is dark), a faint
/// ambient, and every body in the system, read from the same <see cref="StarSystem"/> as the
/// surface's sky at the same moment, so nothing moves or changes at the top of the band.
///
/// The bodies are drawn at their true place when it is near, and otherwise along the true direction
/// at the distance the camera reaches, at the true apparent size. Each is lit by the star at its own
/// place, so it shows the same phase it shows from the ground.
///
/// A sky's eye directions turn with the sky but its light directions don't, so the sun is two
/// lights: one lights the scene from the sun's direction in the world, and the other, never
/// lighting anything, tells the sky's shader where the sun stands in the star field.
///
/// Its environment is only active over the top of the band; below that the surface's own
/// sky is what you see. The tone mapping and glow are the surface's, so nothing shifts when one
/// sky gives way to the other.
/// </summary>
public partial class SpaceSky : Node3D
{
    public WorldEnvironment World { get; private set; }
    public Godot.Environment Environment { get; private set; }

    /// <summary>The sun as it lights the scene.</summary>
    public DirectionalLight3D Sun { get; private set; }

    /// <summary>The sun as the sky's shader sees it: lights nothing.</summary>
    DirectionalLight3D _skySun;

    /// <summary>Toward the sun in the globe's frame, as it is now.</summary>
    public Vector3 ToSun { get; private set; } = Vector3.Up;

    /// <summary>The star's own strength out here.</summary>
    public float SunEnergy { get; private set; }

    ShaderMaterial _sky;
    readonly List<(MeshInstance3D node, Orbiter body, ShaderMaterial material)> _bodies = new();
    OrbitRules.SpaceRules _rules;
    bool _bodiesShown;
    Vector3 _fieldSun = Vector3.Zero;

    /// <summary>What is drawn, for checking against the model: each body, its direction in the world and its apparent radius.</summary>
    public IEnumerable<(Orbiter Body, Vector3 Direction, float Angular)> Drawn
    {
        get
        {
            Vector3 eye = _eye;
            foreach (var (node, body, _) in _bodies)
            {
                Vector3 to = node.GlobalPosition - eye;
                yield return (body, to.Normalized(), MathF.Atan2(node.Scale.X, to.Length()));
            }
        }
    }

    Vector3 _eye;

    /// <summary>A world or moon: bands on a giant, a plain rock otherwise, lit exactly as the sky's shader lights them.</summary>
    const string BodySource = """
        shader_type spatial;
        render_mode unshaded, shadows_disabled, fog_disabled;
        uniform vec3 color : source_color;
        uniform vec3 band : source_color;
        uniform vec3 to_light = vec3(0.0, 1.0, 0.0);
        uniform vec3 axis = vec3(0.0, 1.0, 0.0);
        uniform bool banded = false;
        varying vec3 normal_w;
        void vertex() { normal_w = (MODEL_MATRIX * vec4(NORMAL, 0.0)).xyz; }
        void fragment() {
            vec3 n = normalize(normal_w);
            float lit = clamp(dot(n, to_light), 0.0, 1.0);
            vec3 surface = color;
            if (banded) {
                float lat = dot(n, axis);
                surface = mix(color, band, (sin(lat * 19.0 + sin(lat * 7.0) * 1.6) * 0.5 + 0.5) * 0.7);
            }
            ALBEDO = surface * (banded ? 0.03 + 0.97 * lit : 0.025 + 1.1 * lit);
        }
        """;

    /// <summary>A giant's rings, as the sky's shader draws them.</summary>
    const string GiantRingSource = """
        shader_type spatial;
        render_mode unshaded, blend_mix, cull_disabled, depth_draw_never, shadows_disabled, fog_disabled;
        uniform vec3 color : source_color;
        uniform vec3 to_light = vec3(0.0, 1.0, 0.0);
        uniform vec3 axis = vec3(0.0, 1.0, 0.0);
        void fragment() {
            float rr = mix(1.45, 2.42, UV.x);
            float ring = smoothstep(1.45, 1.5, rr) * (1.0 - smoothstep(2.35, 2.42, rr));
            ring *= 0.6 + 0.4 * sin(rr * 41.0) * sin(rr * 13.0);
            ring *= 1.0 - (smoothstep(1.93, 1.97, rr) - smoothstep(2.03, 2.07, rr)) * 0.85;
            ALBEDO = color * (0.6 + 0.4 * clamp(abs(dot(axis, to_light)) * 1.5, 0.0, 1.0));
            ALPHA = ring * 0.85;
        }
        """;

    public void Setup(ArchetypeLook look, SkySystem system, StarSystem model, OrbitRules.SpaceRules rules)
    {
        _rules = rules;
        _sky = new ShaderMaterial { Shader = Planets.Shaders.Space };
        _sky.SetShaderParameter("nebula", C(look.Nebula));
        _sky.SetShaderParameter("nebula_b", C(look.Atmosphere * 0.6f));
        _sky.SetShaderParameter("sun_radius", model.See(model.Star, 0.0, new D3(0, model.Home.Radius, 0)).Angular);
        Environment = new Godot.Environment
        {
            BackgroundMode = Godot.Environment.BGMode.Sky,
            Sky = new Sky { SkyMaterial = _sky, RadianceSize = Sky.RadianceSizeEnum.Size32 },
            AmbientLightSource = Godot.Environment.AmbientSource.Color,
            AmbientLightColor = new Color(rules.Ambient[0], rules.Ambient[1], rules.Ambient[2]),
            AmbientLightEnergy = 1f,
            ReflectedLightSource = Godot.Environment.ReflectionSource.Disabled,
            // The surface's own tone mapping and glow (planet-surface's SkyRig).
            TonemapMode = Godot.Environment.ToneMapper.Filmic,
            TonemapWhite = 6f,
            TonemapExposure = 1f,
            GlowEnabled = true,
            GlowIntensity = 0.5f,
            GlowBloom = 0.02f,
            GlowHdrThreshold = 2f,
        };
        World = new WorldEnvironment { Name = "Space" }; // no environment until space is what you see
        AddChild(World);
        Rgb star = system.StarColor;
        var colour = new Color(star.R, star.G, star.B);
        SunEnergy = rules.SunEnergy;
        Sun = new DirectionalLight3D
        {
            Name = "Sun", LightColor = colour, LightEnergy = 0f, ShadowEnabled = true,
            DirectionalShadowMaxDistance = 320f, DirectionalShadowMode = DirectionalLight3D.ShadowMode.Parallel4Splits,
            SkyMode = DirectionalLight3D.SkyModeEnum.LightOnly,
        };
        AddChild(Sun);
        _skySun = new DirectionalLight3D { Name = "SkySun", LightColor = colour, SkyMode = DirectionalLight3D.SkyModeEnum.SkyOnly, Visible = false };
        AddChild(_skySun);
        var sphere = new SphereMesh { Radius = 1f, Height = 2f, RadialSegments = 48, Rings = 24 };
        var bodyShader = new Shader { Code = BodySource };
        foreach (Orbiter b in model.Bodies)
        {
            if (b.Kind is OrbiterKind.Star or OrbiterKind.Belt || b == model.Home) continue;
            var material = new ShaderMaterial { Shader = bodyShader };
            material.SetShaderParameter("color", C(b.Color));
            material.SetShaderParameter("band", C(b.Band));
            material.SetShaderParameter("banded", b.Kind == OrbiterKind.GasGiant);
            var node = new MeshInstance3D
            {
                Name = b.Name.Replace(" ", ""), MaterialOverride = material, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
                Mesh = sphere, Visible = false, CustomAabb = new Aabb(new Vector3(-3f, -3f, -3f), new Vector3(6f, 6f, 6f)),
            };
            AddChild(node);
            _bodies.Add((node, b, material));
            if (!b.Rings) continue;
            var ring = new ShaderMaterial { Shader = new Shader { Code = GiantRingSource }, RenderPriority = 3 };
            ring.SetShaderParameter("color", C(b.RingColor));
            node.AddChild(new MeshInstance3D
            {
                Name = "Rings", Mesh = Globe.RingMesh(1.45f, 2.42f, 160), MaterialOverride = ring,
                CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            });
        }
    }

    /// <summary>The farthest a body is drawn, m: further off it stands here at its true apparent size.</summary>
    const float DrawnAt = 50000f;

    /// <summary>
    /// Everything where the system has it this moment, as seen from <paramref name="eye"/> (in the
    /// world) at <paramref name="eyeGlobe"/> (the same point in the globe's frame, m).
    /// <paramref name="turn"/> takes the globe's directions to the world's.
    /// </summary>
    public void Track(StarSystem model, double days, Basis turn, D3 eyeGlobe, Vector3 eye)
    {
        _eye = eye;
        StarSystem.Orientation o = model.Orient(days);
        Vector3 W(D3 d) => turn * new Vector3((float)d.X, (float)d.Y, (float)d.Z);
        ToSun = V(model.See(model.Star, days, eyeGlobe, o).Direction);
        Sun.Basis = Facing((turn * ToSun).Normalized());
        // The star field stands in the system's frame: the world turns under it with the day.
        Environment.SkyRotation = (turn * new Basis(V(o.X), V(o.Y), V(o.Z)).Transposed()).GetEuler();
        Vector3 field = V(model.Position(model.Star, days) - model.Position(model.Home, days)).Normalized();
        if (field.Dot(_fieldSun) < 0.99995f) // the sky's shader is worked again whenever its light moves
        {
            _fieldSun = field;
            _skySun.Basis = Facing(field);
        }
        if (!_bodiesShown) return;
        foreach (var (node, body, material) in _bodies)
        {
            Sighting s = model.See(body, days, eyeGlobe, o);
            float d = MathF.Min((float)s.Distance, DrawnAt);
            Vector3 dir = W(s.Direction).Normalized(), light = W(s.ToItsLight).Normalized();
            node.GlobalPosition = eye + dir * d;
            node.Scale = Vector3.One * d * MathF.Tan(s.Angular);
            material.SetShaderParameter("to_light", light);
            if (body.Kind != OrbiterKind.GasGiant) continue;
            Vector3 axis = W(o.ToGlobe(body.RingNormal)).Normalized();
            material.SetShaderParameter("axis", axis);
            if (body.Rings && node.GetChild(0) is MeshInstance3D ring)
            {
                ring.Basis = new Basis(new Quaternion(Vector3.Up, axis));
                var m = (ShaderMaterial)ring.MaterialOverride;
                m.SetShaderParameter("to_light", light);
                m.SetShaderParameter("axis", axis);
            }
        }
    }

    static Vector3 V(D3 d) => new((float)d.X, (float)d.Y, (float)d.Z);

    static Basis Facing(Vector3 toSun) => Basis.LookingAt(-toSun, MathF.Abs(toSun.Y) > 0.99f ? Vector3.Forward : Vector3.Up);

    /// <summary>The star's share of the light, 0 to 1 (the surface's sun has the rest).</summary>
    public void Light(float share) => Sun.LightEnergy = SunEnergy * share;

    /// <summary>Space's sky on or off (only one environment at a time), and how bright its sky is, 0 to 1.</summary>
    public void Show(bool active, float brightness)
    {
        World.Environment = active ? Environment : null;
        _skySun.Visible = active;
        Environment.BackgroundEnergyMultiplier = brightness;
        Environment.AmbientLightEnergy = brightness;
    }

    /// <summary>The bodies drawn here or not: under the top of the band the surface's sky is drawing them, at the same places.</summary>
    public void ShowBodies(bool shown)
    {
        if (shown == _bodiesShown) return;
        _bodiesShown = shown;
        foreach (var (node, _, _) in _bodies) node.Visible = shown;
    }

    static Color C(Rgb c) => new(c.R, c.G, c.B);
}
