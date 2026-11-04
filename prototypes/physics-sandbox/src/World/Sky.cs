using Godot;

namespace PhysicsSandbox.World;

/// <summary>
/// The sky over the sandbox: a teal zenith warming to a peach horizon, a low warm sun, lavender
/// shadows, a little height haze over the basin, and a ringed neighbour planet hanging over the
/// northern ridge. Presentation only.
/// </summary>
public static class Sky
{
    public static DirectionalLight3D Build(Node3D parent)
    {
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
        return sun;
    }

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
