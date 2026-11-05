using Godot;
using Planets.Look;
using PlanetSurface.Skies;
using PlanetWorld = PlanetSurface.World.PlanetWorld;

namespace CapitalBase;

/// <summary>
/// Sky, sun and air for a planet, from planet-surface's sky data (its star's light, its air's
/// colours and haze), on Godot's own procedural sky. A fixed afternoon sun.
/// </summary>
public partial class Scenery : Node3D
{
    public DirectionalLight3D Sun { get; private set; }
    public WorldEnvironment Environment { get; private set; }

    static Color C(Rgb c, float a = 1f) => new(c.R, c.G, c.B, a);

    public void Setup(PlanetWorld planet, float sunHeight = 34f, float sunTurn = -38f)
    {
        SkySystem sky = planet.Sky;
        var material = new ProceduralSkyMaterial
        {
            SkyTopColor = C(sky.Zenith),
            SkyHorizonColor = C(sky.Horizon),
            GroundHorizonColor = C(sky.Haze),
            GroundBottomColor = C(sky.Haze) * 0.55f,
            SunAngleMax = 24f,
            SkyCurve = 0.12f,
        };
        var env = new Godot.Environment
        {
            BackgroundMode = Godot.Environment.BGMode.Sky,
            Sky = new Sky { SkyMaterial = material },
            AmbientLightSource = Godot.Environment.AmbientSource.Sky,
            AmbientLightEnergy = 0.9f,
            TonemapMode = Godot.Environment.ToneMapper.Filmic,
            TonemapExposure = 1.05f,
            FogEnabled = true,
            FogLightColor = C(sky.Haze),
            FogDensity = 0.0009f * sky.Density,
            FogAerialPerspective = 0.6f,
            FogSkyAffect = 0.25f,
            GlowEnabled = true,
            GlowIntensity = 0.5f,
            GlowBloom = 0.05f,
        };
        Environment = new WorldEnvironment { Environment = env };
        AddChild(Environment);
        Sun = new DirectionalLight3D
        {
            LightColor = C(sky.StarColor),
            LightEnergy = 1.15f,
            ShadowEnabled = true,
            DirectionalShadowMaxDistance = 260f,
            RotationDegrees = new Vector3(-sunHeight, sunTurn, 0f),
        };
        AddChild(Sun);
        RenderingServer.GlobalShaderParameterSet("weather_ground", new Vector4(0f, 0f, 0.12f, 1f));
        RenderingServer.GlobalShaderParameterSet("weather_wind", new Vector4(1f, 0f, 3f, 0f));
    }
}
