using System;
using System.Collections.Generic;
using GalHack.Stars;
using Godot;
using Planets.Look;
using PlanetSurface.Skies;

namespace Slice.Orbit;

/// <summary>
/// Space round the start world: planet-generator's space sky (the galaxy's stars and nebulae, from
/// the one catalogue, and the sun's glare), the sun as the one light (hard shadows, so a dead hull's inside is dark), a faint
/// ambient, and the world's own moons and giant from planet-surface's sky system, the same bodies
/// the surface sees overhead, standing far off where its sky shows them at the first crossing point.
///
/// The stars and the sun are fixed to the globe, not the world: when the zone is re-aligned the
/// sky turns with it. A sky's eye directions turn with the sky but its light directions don't, so
/// the sun is two lights: one lights the scene from the sun's direction in the world, and the
/// other, never lighting anything, tells the sky's shader where the sun stands in the sky.
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

    /// <summary>The node the moons stand in (the globe's frame).</summary>
    public Node3D Bodies;

    /// <summary>Toward the sun, in the globe's frame: fixed once, at the first crossing.</summary>
    public Vector3 ToSun { get; private set; } = Vector3.Up;

    /// <summary>The star's own strength out here.</summary>
    public float SunEnergy { get; private set; }

    ShaderMaterial _sky;
    readonly List<(MeshInstance3D node, SkyBody body)> _bodies = new();
    OrbitRules.SpaceRules _rules;
    float _bodiesFade = -1f;

    public void Setup(ArchetypeLook look, SkySystem system, OrbitRules.SpaceRules rules)
    {
        _rules = rules;
        _sky = new ShaderMaterial { Shader = Planets.Shaders.Space };
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
        foreach (SkyBody b in system.Bodies)
        {
            var material = new StandardMaterial3D { AlbedoColor = new Color(b.Color.R, b.Color.G, b.Color.B), Roughness = 1f };
            var node = new MeshInstance3D
            {
                Name = b.Name.Replace(" ", ""), MaterialOverride = material, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
                Mesh = new SphereMesh { Radius = 1f, Height = 2f, RadialSegments = 48, Rings = 24 }, Visible = false,
            };
            (Bodies ?? this).AddChild(node);
            _bodies.Add((node, b));
        }
    }

    /// <summary>
    /// The sun's direction (toward it, in the globe's frame) and each moon and giant, stood off
    /// where the surface sky shows them from a place whose local frame (east, up, south) in the
    /// globe's frame is <paramref name="frame"/>.
    /// </summary>
    public void Place(Vector3 toSun, Basis frame)
    {
        ToSun = toSun.Normalized();
        _skySun.Basis = Facing(ToSun);
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

    static Basis Facing(Vector3 toSun) => Basis.LookingAt(-toSun, MathF.Abs(toSun.Y) > 0.99f ? Vector3.Forward : Vector3.Up);

    /// <summary>The zone is aligned: the sky turns with the globe, and the sun lights the scene from its place in the world.</summary>
    public void Align(Basis turn)
    {
        Sun.Basis = Facing((turn * ToSun).Normalized());
        Environment.SkyRotation = turn.GetEuler();
    }

    /// <summary>The galaxy as it stands from here: the same catalogue, and the same turn of the world, as the surface's sky.</summary>
    public void Galaxy(GalaxySky stars)
    {
        if (!_galaxyBound) stars.Bind(_sky);
        _galaxyBound = true;
        _sky.SetShaderParameter("star_turn", GalaxySky.Basis(stars.FromGlobe));
        GalaxySky.SetPixel(_sky, GetViewport());
    }

    bool _galaxyBound;

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

    /// <summary>The moons and giant, faded in (0 to 1): the surface's sky draws its own until space's takes over.</summary>
    public void FadeBodies(float fade)
    {
        if (Mathf.Abs(fade - _bodiesFade) < 0.01f) return;
        _bodiesFade = fade;
        foreach (var (node, _) in _bodies)
        {
            node.Visible = fade > 0.01f;
            node.Transparency = 1f - fade;
        }
    }

    static Color C(Rgb c) => new(c.R, c.G, c.B);
}
