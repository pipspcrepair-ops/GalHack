using Godot;

namespace PoiGenerator;

/// <summary>
/// Sky, sun, fog, glow and the world hanging in the sky, switched as a whole per state.
/// Ash falls around whatever <see cref="Follow"/> is when the atmosphere asks for it.
/// </summary>
public partial class SkyRig : Node3D
{
    public Node3D Follow;
    WorldEnvironment _world;
    ProceduralSkyMaterial _sky;
    DirectionalLight3D _sun;
    Node3D _planet;
    StandardMaterial3D _planetMat, _ringMat;
    GpuParticles3D _ash;

    public DirectionalLight3D Sun => _sun;
    public Godot.Environment Environment => _world.Environment;

    public override void _Ready()
    {
        _sky = new ProceduralSkyMaterial { SunAngleMax = 25f, SunCurve = 0.08f };
        var env = new Godot.Environment
        {
            BackgroundMode = Godot.Environment.BGMode.Sky,
            Sky = new Sky { SkyMaterial = _sky },
            AmbientLightSource = Godot.Environment.AmbientSource.Sky,
            TonemapMode = Godot.Environment.ToneMapper.Filmic,
            TonemapExposure = 1.0f,
            GlowEnabled = true,
            GlowIntensity = 0.75f,
            GlowBloom = 0.08f,
            GlowHdrThreshold = 0.95f,
            GlowBlendMode = Godot.Environment.GlowBlendModeEnum.Additive,
            FogEnabled = true,
            FogSkyAffect = 0.06f, // keep the zenith clean: deep sky over a burning horizon
        };
        _world = new WorldEnvironment { Environment = env };
        AddChild(_world);

        _sun = new DirectionalLight3D
        {
            ShadowEnabled = true, DirectionalShadowMaxDistance = 110f, ShadowBias = 0.05f, ShadowBlur = 1.5f,
            DirectionalShadowMode = DirectionalLight3D.ShadowMode.Parallel4Splits, DirectionalShadowBlendSplits = true,
        };
        AddChild(_sun);

        _planetMat = new StandardMaterial3D { ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded, DisableFog = true };
        _ringMat = new StandardMaterial3D
        {
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded, DisableFog = true,
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha, CullMode = BaseMaterial3D.CullModeEnum.Disabled,
        };
        _planet = new Node3D { Name = "Planet" };
        _planet.AddChild(new MeshInstance3D { Mesh = new SphereMesh { Radius = 520f, Height = 1040f, RadialSegments = 48, Rings = 24 }, MaterialOverride = _planetMat });
        var ring = new MeshInstance3D
        {
            Mesh = new TorusMesh { InnerRadius = 760f, OuterRadius = 1050f, Rings = 64, RingSegments = 4 },
            MaterialOverride = _ringMat,
            Scale = new Vector3(1f, 0.02f, 1f),
            RotationDegrees = new Vector3(14f, 0f, 22f),
        };
        _planet.AddChild(ring);
        AddChild(_planet);

        _ash = new GpuParticles3D
        {
            Amount = 1600, Lifetime = 9f, Preprocess = 9f, Emitting = false, Visible = false,
            VisibilityAabb = new Aabb(new Vector3(-60, -40, -60), new Vector3(120, 80, 120)),
            ProcessMaterial = new ParticleProcessMaterial
            {
                EmissionShape = ParticleProcessMaterial.EmissionShapeEnum.Box,
                EmissionBoxExtents = new Vector3(45f, 18f, 45f),
                Gravity = new Vector3(0.4f, -0.9f, 0.2f),
                InitialVelocityMin = 0.1f, InitialVelocityMax = 0.5f,
                Direction = new Vector3(1, -0.3f, 0.4f), Spread = 60f,
                TurbulenceEnabled = true, TurbulenceNoiseStrength = 0.6f,
            },
            DrawPass1 = new QuadMesh
            {
                Size = new Vector2(0.045f, 0.045f),
                Material = new StandardMaterial3D
                {
                    AlbedoColor = new Color(0.66f, 0.63f, 0.6f, 0.75f),
                    ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
                    Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
                    BillboardMode = BaseMaterial3D.BillboardModeEnum.Enabled,
                },
            },
        };
        AddChild(_ash);
    }

    public void Apply(Look.Atmosphere a, float shadowDistance = 160f)
    {
        _sky.SkyTopColor = a.SkyTop;
        _sky.SkyHorizonColor = a.Horizon;
        _sky.GroundHorizonColor = a.GroundHorizon;
        _sky.GroundBottomColor = a.GroundBottom;
        _sky.SunCurve = 0.1f;
        _sky.SkyCurve = 0.06f;      // a narrow horizon band, then straight to the top colour
        _sky.GroundCurve = 0.04f;
        var env = _world.Environment;
        env.AmbientLightEnergy = a.Ambient;
        env.FogLightColor = a.Fog;
        env.FogDensity = a.FogDensity;
        _sun.LightColor = a.Sun;
        _sun.LightEnergy = a.SunEnergy;
        _sun.RotationDegrees = a.SunAngles;
        _sun.DirectionalShadowMaxDistance = shadowDistance;
        _planetMat.AlbedoColor = a.Planet;
        _ringMat.AlbedoColor = new Color(a.Planet.Lightened(0.25f), 0.55f);
        _planet.Position = a.PlanetAt;
        _planet.Scale = Vector3.One * a.PlanetScale;
        _ash.Emitting = a.Ash;
        _ash.Visible = a.Ash;
    }

    /// <summary>
    /// Hangs the world in the sky where a camera will see it: turned from the camera's heading
    /// by <paramref name="yawDeg"/> and raised <paramref name="elevationDeg"/> above the horizon.
    /// </summary>
    public void PlacePlanetInView(Camera3D camera, float yawDeg, float elevationDeg, float distance = 4800f)
    {
        Vector3 fwd = -camera.GlobalBasis.Z;
        fwd.Y = 0;
        fwd = fwd.Normalized().Rotated(Vector3.Up, Mathf.DegToRad(yawDeg));
        Vector3 dir = (fwd * Mathf.Cos(Mathf.DegToRad(elevationDeg)) + Vector3.Up * Mathf.Sin(Mathf.DegToRad(elevationDeg))).Normalized();
        _planet.GlobalPosition = camera.GlobalPosition + dir * distance;
    }

    public override void _Process(double delta)
    {
        if (Follow != null && IsInstanceValid(Follow) && _ash.Visible)
            _ash.GlobalPosition = Follow.GlobalPosition + Vector3.Up * 6f;
    }
}
