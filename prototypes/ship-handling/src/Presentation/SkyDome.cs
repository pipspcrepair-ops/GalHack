using Godot;

namespace ShipHandling;

/// <summary>
/// The sky: a hazy apricot desert sky inside the atmosphere, fading to an indigo-black starfield
/// above the atmosphere ceiling, where flight switches to space handling. A pale neighbouring
/// world hangs over the horizon either way.
/// </summary>
public partial class SkyDome : Node3D
{
    public float Ceiling = 600f;

    ProceduralSkyMaterial _sky;
    Godot.Environment _env;
    MultiMeshInstance3D _stars;
    StandardMaterial3D _starMat;

    static readonly Color AirTop = new("1d3557"), AirHorizon = new("f4a261"), AirGround = new("e76f51");
    static readonly Color SpaceTop = new("05040c"), SpaceHorizon = new("2a1b3d"), SpaceGround = new("3a2016");

    /// <summary>0 in the air, 1 in space.</summary>
    public float SpaceFactor { get; private set; }

    public override void _Ready()
    {
        _sky = new ProceduralSkyMaterial
        {
            SkyTopColor = AirTop,
            SkyHorizonColor = AirHorizon,
            GroundHorizonColor = AirGround,
            GroundBottomColor = new Color("6d2e1f"),
            SunAngleMax = 30f,
        };
        _env = new Godot.Environment
        {
            BackgroundMode = Godot.Environment.BGMode.Sky,
            Sky = new Sky { SkyMaterial = _sky },
            AmbientLightSource = Godot.Environment.AmbientSource.Sky,
            AmbientLightEnergy = 0.75f,
            TonemapMode = Godot.Environment.ToneMapper.Filmic,
            FogEnabled = true,
            FogLightColor = new Color("f4a261"),
            FogDensity = 0.00022f,
            GlowEnabled = true,
            GlowIntensity = 0.5f,
            GlowBloom = 0.05f,
        };
        AddChild(new WorldEnvironment { Environment = _env });

        var sun = new DirectionalLight3D
        {
            LightColor = new Color("ffd6a0"),
            LightEnergy = 1.3f,
            ShadowEnabled = true,
            DirectionalShadowMaxDistance = 400f,
        };
        AddChild(sun);
        sun.RotationDegrees = new Vector3(-38f, 35f, 0f);

        var moon = new MeshInstance3D
        {
            Mesh = new SphereMesh { Radius = 520f, Height = 1040f },
            MaterialOverride = new StandardMaterial3D
            {
                AlbedoColor = new Color("7fb7be"),
                ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
                DisableFog = true,
            },
        };
        AddChild(moon);
        moon.Position = new Vector3(3100f, 1900f, -4600f);

        BuildStars();
    }

    void BuildStars()
    {
        var rng = new RandomNumberGenerator { Seed = 42 };
        var multimesh = new MultiMesh
        {
            TransformFormat = MultiMesh.TransformFormatEnum.Transform3D,
            Mesh = new QuadMesh { Size = new Vector2(9f, 9f) },
            InstanceCount = 1400,
        };
        for (int i = 0; i < multimesh.InstanceCount; i++)
        {
            // Uniform on a sphere, a little brighter (bigger) now and then.
            float y = rng.RandfRange(-0.3f, 1f), a = rng.Randf() * Mathf.Tau, r = Mathf.Sqrt(1f - y * y);
            var dir = new Vector3(r * Mathf.Cos(a), y, r * Mathf.Sin(a));
            float scale = rng.Randf() < 0.08f ? 2.4f : rng.RandfRange(0.6f, 1.3f);
            multimesh.SetInstanceTransform(i, new Transform3D(Basis.FromScale(Vector3.One * scale), dir * 7000f));
        }
        _starMat = new StandardMaterial3D
        {
            AlbedoColor = new Color(1f, 0.95f, 0.85f, 0f),
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
            BillboardMode = BaseMaterial3D.BillboardModeEnum.Enabled,
            DisableFog = true,
        };
        _stars = new MultiMeshInstance3D
        {
            Multimesh = multimesh,
            MaterialOverride = _starMat,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            PhysicsInterpolationMode = PhysicsInterpolationModeEnum.Off,
            Visible = false,
        };
        AddChild(_stars);
    }

    /// <summary>Called every frame with the viewer's position: blends the sky across the ceiling.</summary>
    public void Follow(Vector3 viewer)
    {
        float t = Mathf.SmoothStep(Ceiling - 150f, Ceiling + 40f, viewer.Y);
        SpaceFactor = t;
        _sky.SkyTopColor = AirTop.Lerp(SpaceTop, t);
        _sky.SkyHorizonColor = AirHorizon.Lerp(SpaceHorizon, t);
        _sky.GroundHorizonColor = AirGround.Lerp(SpaceGround, t);
        _env.FogDensity = Mathf.Lerp(0.00022f, 0.00002f, t);
        _env.FogSkyAffect = Mathf.Lerp(0.8f, 0f, t); // the haze is in the air, not in space
        _env.FogLightColor = AirHorizon.Lerp(SpaceHorizon, t);
        _env.AmbientLightEnergy = Mathf.Lerp(0.75f, 0.45f, t);
        _stars.Visible = t > 0.05f;
        _starMat.AlbedoColor = new Color(1f, 0.95f, 0.85f, t);
        _stars.GlobalPosition = viewer; // stars live at infinity
    }
}
