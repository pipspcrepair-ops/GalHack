using Godot;

namespace PhysicsSandbox.World;

/// <summary>
/// The sky over the sandbox: a teal zenith warming to a peach horizon, a low warm sun, lavender
/// shadows, a little height haze over the basin, and a ringed neighbour planet hanging over the
/// northern ridge. Above the atmosphere it is space instead: black, a scatter of stars, the
/// same sun and the same ringed giant, and no haze. Under the lake it is teal murk. Presentation only.
/// </summary>
public sealed class Sky
{
    public DirectionalLight3D Sun { get; private set; }
    public bool InSpace { get; private set; }

    Godot.Environment _env;
    Godot.Sky _air, _space;

    public static Sky Build(Node3D parent)
    {
        var result = new Sky();
        var sky = new ProceduralSkyMaterial
        {
            SkyTopColor = Palette.SkyTop,
            SkyHorizonColor = Palette.SkyHorizon,
            SkyCurve = 0.12f,
            GroundBottomColor = Palette.GroundHorizon.Darkened(0.4f),
            GroundHorizonColor = Palette.GroundHorizon,
            SunAngleMax = 8f,
            SunCurve = 0.08f,
        };
        var env = new Godot.Environment
        {
            BackgroundMode = Godot.Environment.BGMode.Sky,
            Sky = new Godot.Sky { SkyMaterial = sky },
            AmbientLightSource = Godot.Environment.AmbientSource.Color,
            AmbientLightColor = Palette.Shadow.Lightened(0.25f),
            AmbientLightEnergy = 0.55f,
            TonemapMode = Godot.Environment.ToneMapper.Filmic,
            TonemapExposure = 1.05f,
            FogEnabled = true,
            FogLightColor = Palette.Haze,
            FogDensity = 0.0022f,
            FogHeight = 30f,
            FogHeightDensity = 0.02f,
            FogSkyAffect = 0.1f,
            GlowEnabled = true,
            GlowIntensity = 0.6f,
            GlowBloom = 0.05f,
        };
        parent.AddChild(new WorldEnvironment { Environment = env });
        result._env = env;
        result._air = env.Sky;
        result._space = new Godot.Sky { SkyMaterial = new ShaderMaterial { Shader = new Shader { Code = SpaceShader } } };

        var sun = new DirectionalLight3D
        {
            Name = "Sun",
            LightColor = Palette.Sun,
            LightEnergy = 1.25f,
            ShadowEnabled = true,
            DirectionalShadowMaxDistance = 260f,
            RotationDegrees = new Vector3(-28f, -140f, 0f),
        };
        parent.AddChild(sun);
        parent.AddChild(RingedPlanet());
        result.Sun = sun;

        // The tint over the view under water (under the HUD).
        var layer = new CanvasLayer { Name = "UnderwaterTint", Layer = 5 };
        result._tint = new ColorRect { Color = new Color(Palette.Water, 0.22f), Visible = false, MouseFilter = Control.MouseFilterEnum.Ignore };
        result._tint.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        layer.AddChild(result._tint);
        parent.AddChild(layer);
        return result;
    }

    /// <summary>Space when the camera is above the atmosphere's ceiling, sky below it.</summary>
    public void Follow(bool inSpace)
    {
        if (inSpace == InSpace) return;
        InSpace = inSpace;
        _env.Sky = inSpace ? _space : _air;
        _env.FogEnabled = !inSpace;
        _env.AmbientLightColor = inSpace ? Palette.Shadow.Darkened(0.35f) : Palette.Shadow.Lightened(0.25f);
        _env.AmbientLightEnergy = inSpace ? 0.4f : 0.55f;
        Sun.LightEnergy = inSpace ? 1.5f : 1.25f;
    }

    public bool Underwater { get; private set; }
    ColorRect _tint;

    /// <summary>
    /// Under water the view closes in: teal murk thickening with depth instead of the sky, and a
    /// tint over everything. Above it, the air again exactly as it was.
    /// </summary>
    public void Submerge(bool under, float depth)
    {
        if (under)
        {
            if (!Underwater)
            {
                Underwater = true;
                _env.BackgroundMode = Godot.Environment.BGMode.Color;
                _env.BackgroundColor = Palette.DeepWater;
                _env.FogEnabled = true;
                _env.FogLightColor = Palette.DeepWater.Lightened(0.15f);
                _env.FogSkyAffect = 1f;
                _env.FogHeightDensity = 0f;
                _env.AmbientLightColor = Palette.Water;
                if (_tint != null) _tint.Visible = true;
            }
            _env.FogDensity = 0.045f + 0.004f * depth; // murkier the deeper you go
            return;
        }
        if (!Underwater) return;
        Underwater = false;
        _env.BackgroundMode = Godot.Environment.BGMode.Sky;
        _env.FogEnabled = !InSpace;
        _env.FogLightColor = Palette.Haze;
        _env.FogDensity = 0.0022f;
        _env.FogSkyAffect = 0.1f;
        _env.FogHeightDensity = 0.02f;
        _env.AmbientLightColor = Palette.Shadow.Lightened(0.25f);
        if (_tint != null) _tint.Visible = false;
    }

    /// <summary>Space: near-black with a faint dusty band, hashed stars of a few sizes, and the sun's disc.</summary>
    internal const string SpaceShader = @"
shader_type sky;

float hash(vec3 p) {
    return fract(sin(dot(p, vec3(12.9898, 78.233, 37.719))) * 43758.5453);
}

// Stars: points scattered through a thin shell of a 3D lattice, `scale` cells out, each drawn where
// the view ray passes within its radius. Every star is placed, and kept or dropped, by itself
// (where it lies in its cell, and whether that is inside the shell), never by where the ray cuts a
// cell, so the field is equally dense in every direction. (Cutting the lattice with the sky's
// sphere left a star-free hole ringed by bands at each of the six axis directions.) A star that
// close to the ray lies within half a cell of the ray's own point each way, so the two cells
// round that point on each axis hold it.
float stars(vec3 dir, float scale, float density) {
    vec3 p = dir * scale;
    vec3 base = floor(p + 0.28) - 1.0;
    float sum = 0.0;
    for (int i = 0; i < 8; i++) {
        vec3 cell = base + vec3(float(i & 1), float((i >> 1) & 1), float((i >> 2) & 1));
        if (hash(cell) < 1.0 - density) continue;
        vec3 star = cell + 0.5 + (vec3(hash(cell + 1.7), hash(cell + 4.1), hash(cell + 9.3)) - 0.5) * 0.4;
        if (abs(length(star) - scale) > 0.35) continue;
        float off = length(star - dir * dot(star, dir));   // from the ray, in cells
        sum += smoothstep(0.3, 0.05, off) * (0.4 + 0.6 * hash(cell + 2.3));
    }
    return sum;
}

void sky() {
    vec3 d = normalize(EYEDIR);
    float band = pow(1.0 - abs(dot(d, normalize(vec3(0.3, 0.9, 0.2)))), 6.0);
    vec3 c = vec3(0.008, 0.009, 0.022) + vec3(0.05, 0.035, 0.07) * band;
    c += vec3(1.0, 0.95, 0.9) * (stars(d, 240.0, 0.03) + 0.6 * stars(d, 520.0, 0.05));
    if (LIGHT0_ENABLED) {
        float s = dot(d, LIGHT0_DIRECTION);
        c += LIGHT0_COLOR * (smoothstep(0.99985, 0.99995, s) * 6.0 + pow(max(s, 0.0), 600.0) * 0.6);
    }
    COLOR = c;
}
";

    /// <summary>A banded giant with a tilted ring, far off over the northern ridge.</summary>
    static Node3D RingedPlanet()
    {
        var root = new Node3D { Name = "RingedPlanet", Position = new Vector3(-900f, 520f, -2200f) };
        var body = new StandardMaterial3D
        {
            AlbedoColor = Palette.Planet,
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            DisableFog = true,
        };
        root.AddChild(new MeshInstance3D { Mesh = new SphereMesh { Radius = 260f, Height = 520f, RadialSegments = 48, Rings = 24 }, MaterialOverride = body });
        var band = new StandardMaterial3D { AlbedoColor = Palette.PlanetBand, ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded, DisableFog = true };
        root.AddChild(new MeshInstance3D
        {
            Mesh = new CylinderMesh { TopRadius = 262f, BottomRadius = 262f, Height = 40f, RadialSegments = 48 },
            MaterialOverride = band,
            Position = new Vector3(0f, 60f, 0f),
        });
        var ring = new StandardMaterial3D
        {
            AlbedoColor = new Color(Palette.Dust, 0.55f),
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            CullMode = BaseMaterial3D.CullModeEnum.Disabled,
            DisableFog = true,
        };
        root.AddChild(new MeshInstance3D
        {
            Mesh = new TorusMesh { InnerRadius = 340f, OuterRadius = 520f, Rings = 64, RingSegments = 4 },
            MaterialOverride = ring,
            Scale = new Vector3(1f, 0.02f, 1f),
            RotationDegrees = new Vector3(18f, 0f, -12f),
        });
        return root;
    }
}
