using Godot;

namespace Encounters;

/// <summary>Shared materials and meshes for the presentation, built once.</summary>
public static class Visuals
{
    static ShaderMaterial _hull;
    static Texture2D _soft;
    static readonly System.Collections.Generic.Dictionary<Color, StandardMaterial3D> Glows = new();

    /// <summary>The one hull material every ship shares; damage and corruption are per-instance uniforms.</summary>
    public static ShaderMaterial Hull => _hull ??= new ShaderMaterial { Shader = GD.Load<Shader>("res://shaders/hull.gdshader") };

    /// <summary>A soft round spot, for flares, beacons and smoke.</summary>
    public static Texture2D Soft => _soft ??= new GradientTexture2D
    {
        Width = 64,
        Height = 64,
        Fill = GradientTexture2D.FillEnum.Radial,
        FillFrom = new Vector2(0.5f, 0.5f),
        FillTo = new Vector2(0.5f, 0f),
        Gradient = new Gradient
        {
            Offsets = new[] { 0f, 0.35f, 1f },
            Colors = new[] { new Color(1, 1, 1, 1), new Color(1, 1, 1, 0.45f), new Color(1, 1, 1, 0) },
        },
    };

    /// <summary>An additive camera-facing glow. HDR colours above 1 bloom.</summary>
    public static StandardMaterial3D Glow(Color color)
    {
        if (Glows.TryGetValue(color, out StandardMaterial3D material)) return material;
        material = new StandardMaterial3D
        {
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            BlendMode = BaseMaterial3D.BlendModeEnum.Add,
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
            BillboardMode = BaseMaterial3D.BillboardModeEnum.Enabled,
            AlbedoTexture = Soft,
            AlbedoColor = color,
            NoDepthTest = false,
            DisableReceiveShadows = true,
        };
        Glows[color] = material;
        return material;
    }

    /// <summary>An unshaded additive solid, for tracers and beams.</summary>
    public static StandardMaterial3D Bolt(Color color) => new()
    {
        ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
        BlendMode = BaseMaterial3D.BlendModeEnum.Add,
        AlbedoColor = color,
        DisableReceiveShadows = true,
    };

    public static MeshInstance3D Flare(Color color, float size) => new()
    {
        Mesh = new QuadMesh { Size = new Vector2(size, size) },
        MaterialOverride = Glow(color),
        CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
    };

    /// <summary>Grey-brown smoke billowing off a wreck: a few big soft puffs, cheap on low-end GPUs.</summary>
    public static CpuParticles3D Smoke(float scale)
    {
        var smoke = new CpuParticles3D
        {
            Amount = 26,
            Lifetime = 9,
            Preprocess = 9,
            LocalCoords = false,
            Mesh = new QuadMesh { Size = Vector2.One },
            EmissionShape = CpuParticles3D.EmissionShapeEnum.Sphere,
            EmissionSphereRadius = scale * 0.3f,
            Direction = Vector3.Up,
            Spread = 50,
            Gravity = Vector3.Zero,
            InitialVelocityMin = scale * 0.12f,
            InitialVelocityMax = scale * 0.3f,
            DampingMin = 0.05f,
            DampingMax = 0.15f,
            ScaleAmountMin = scale * 0.5f,
            ScaleAmountMax = scale * 0.9f,
            ColorRamp = new Gradient
            {
                Offsets = new[] { 0f, 0.15f, 0.6f, 1f },
                Colors = new[]
                {
                    new Color(1f, 0.55f, 0.2f, 0.0f), new Color(0.32f, 0.27f, 0.24f, 0.55f),
                    new Color(0.24f, 0.22f, 0.22f, 0.32f), new Color(0.2f, 0.2f, 0.22f, 0f),
                },
            },
            MaterialOverride = new StandardMaterial3D
            {
                ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
                Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
                BillboardMode = BaseMaterial3D.BillboardModeEnum.Particles,
                VertexColorUseAsAlbedo = true,
                AlbedoTexture = Soft,
                DisableReceiveShadows = true,
            },
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        };
        var curve = new Curve { MaxValue = 2f };
        curve.AddPoint(new Vector2(0f, 0.3f));
        curve.AddPoint(new Vector2(1f, 1.6f));
        smoke.ScaleAmountCurve = curve; // puffs swell as they drift off
        return smoke;
    }
}
