using System;
using System.Collections.Generic;
using Godot;
using Planets.Look;
using PlanetSurface.Skies;

namespace Slice.Orbit;

/// <summary>
/// Space round the start world: planet-generator's space sky (stars, the world's nebula, the sun's
/// glare), the sun as the one light (hard shadows, so a dead hull's inside is dark), a faint
/// ambient, and the world's own moons and giant from planet-surface's sky system, the same bodies
/// the surface sees overhead, standing far off where its sky shows them at the crossing point.
/// </summary>
public partial class SpaceSky : Node3D
{
    public WorldEnvironment World { get; private set; }
    public DirectionalLight3D Sun { get; private set; }
    public Vector3 ToSun { get; private set; } = Vector3.Up;

    ShaderMaterial _sky;
    readonly List<(MeshInstance3D node, SkyBody body)> _bodies = new();
    OrbitRules.SpaceRules _rules;

    public void Setup(ArchetypeLook look, SkySystem system, OrbitRules.SpaceRules rules)
    {
        _rules = rules;
        _sky = new ShaderMaterial { Shader = Planets.Shaders.Space };
        _sky.SetShaderParameter("nebula", C(look.Nebula));
        _sky.SetShaderParameter("nebula_b", C(look.Atmosphere * 0.6f));
        World = new WorldEnvironment
        {
            Name = "Space",
            Environment = new Godot.Environment
            {
                BackgroundMode = Godot.Environment.BGMode.Sky,
                Sky = new Sky { SkyMaterial = _sky, RadianceSize = Sky.RadianceSizeEnum.Size32 },
                AmbientLightSource = Godot.Environment.AmbientSource.Color,
                AmbientLightColor = new Color(rules.Ambient[0], rules.Ambient[1], rules.Ambient[2]),
                AmbientLightEnergy = 1f,
                ReflectedLightSource = Godot.Environment.ReflectionSource.Disabled,
                TonemapMode = Godot.Environment.ToneMapper.Filmic,
                TonemapWhite = 6f,
                GlowEnabled = true,
                GlowIntensity = 0.7f,
                GlowBloom = 0.05f,
                GlowHdrThreshold = 1.0f,
            },
        };
        AddChild(World);
        Rgb star = system.StarColor;
        Sun = new DirectionalLight3D
        {
            Name = "Sun", LightColor = new Color(star.R, star.G, star.B), LightEnergy = rules.SunEnergy, ShadowEnabled = true,
            DirectionalShadowMaxDistance = 300f, DirectionalShadowMode = DirectionalLight3D.ShadowMode.Parallel4Splits,
        };
        AddChild(Sun);
        foreach (SkyBody b in system.Bodies)
        {
            var material = new StandardMaterial3D { AlbedoColor = new Color(b.Color.R, b.Color.G, b.Color.B), Roughness = 1f };
            var node = new MeshInstance3D
            {
                Name = b.Name.Replace(" ", ""), MaterialOverride = material, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
                Mesh = new SphereMesh { Radius = 1f, Height = 2f, RadialSegments = 48, Rings = 24 },
            };
            AddChild(node);
            _bodies.Add((node, b));
        }
    }

    /// <summary>
    /// Points the sun along a direction (toward it), and stands each moon and giant off where the
    /// surface sky shows it from a place whose local frame (east, up, south) is <paramref name="frame"/>.
    /// </summary>
    public void Place(Vector3 toSun, Basis frame)
    {
        ToSun = toSun.Normalized();
        Sun.Basis = Basis.LookingAt(-ToSun, MathF.Abs(ToSun.Y) > 0.99f ? Vector3.Forward : Vector3.Up);
        float d = _rules.MoonDistance;
        foreach (var (node, b) in _bodies)
        {
            float elevation = Mathf.Clamp(b.Kind == PlanetSurface.Skies.BodyKind.GasGiant ? b.Inclination : b.Inclination + 0.25f, 0.2f, 1.25f);
            var local = new Vector3(MathF.Sin(b.Azimuth) * MathF.Cos(elevation), MathF.Sin(elevation), -MathF.Cos(b.Azimuth) * MathF.Cos(elevation));
            Vector3 dir = (frame * local).Normalized();
            float size = d * MathF.Tan(MathF.Max(b.Radius, 0.004f));
            node.Position = dir * d;
            node.Scale = Vector3.One * size;
        }
    }

    static Color C(Rgb c) => new(c.R, c.G, c.B);
}
