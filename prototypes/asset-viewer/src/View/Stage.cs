using System;
using Godot;

namespace AssetViewer;

/// <summary>
/// The room an asset is shown in: a turntable pivot, a sky and a sun the user can swing, and an
/// optional ground disc with a faint grid. An asset that brings its own environment
/// (<see cref="StageSpec.OwnsEnvironment"/>) switches the stage's off.
/// </summary>
public partial class Stage : Node3D
{
    public Node3D Pivot { get; } = new() { Name = "Turntable" };
    public AssetInstance Current { get; private set; }

    WorldEnvironment _env;
    DirectionalLight3D _sun;
    MeshInstance3D _ground;
    StandardMaterial3D _groundMaterial;
    ProceduralSkyMaterial _sky;

    public bool GroundWanted = true;
    public float Light = 0.5f;
    string _background = "sky";
    bool _ownsEnvironment, _groundSpec = true;

    public override void _Ready()
    {
        _sky = new ProceduralSkyMaterial
        {
            SkyTopColor = new Color("5b86b8"), SkyHorizonColor = new Color("b7c9d8"),
            GroundBottomColor = new Color("3a3d40"), GroundHorizonColor = new Color("8a8e92"), SunAngleMax = 25,
        };
        _env = new WorldEnvironment { Name = "Environment" };
        _sun = new DirectionalLight3D { Name = "Sun", ShadowEnabled = true, DirectionalShadowMaxDistance = 400f, LightColor = new Color("fff1dc") };
        AddChild(_env);
        AddChild(_sun);
        AddChild(Pivot);

        _groundMaterial = new StandardMaterial3D
        {
            AlbedoColor = new Color("5c6066"), AlbedoTexture = GridTexture(), Uv1Triplanar = true, Uv1Scale = new Vector3(0.5f, 0.5f, 0.5f),
            Roughness = 0.95f, TextureFilter = BaseMaterial3D.TextureFilterEnum.LinearWithMipmapsAnisotropic,
        };
        _ground = new MeshInstance3D { Name = "Ground", Mesh = new CylinderMesh { TopRadius = 1f, BottomRadius = 1f, Height = 0.02f, RadialSegments = 96 }, MaterialOverride = _groundMaterial };
        AddChild(_ground);
        Refresh();
    }

    static ImageTexture GridTexture()
    {
        const int n = 128;
        var image = Image.CreateEmpty(n, n, true, Image.Format.Rgb8);
        for (int y = 0; y < n; y++)
            for (int x = 0; x < n; x++)
            {
                bool line = x < 2 || y < 2;
                float v = line ? 0.62f : ((x / 64 + y / 64) % 2 == 0 ? 0.96f : 0.86f);
                image.SetPixel(x, y, new Color(v, v, v));
            }
        image.GenerateMipmaps();
        return ImageTexture.CreateFromImage(image);
    }

    /// <summary>Replaces what is shown. The old one is freed.</summary>
    public void Present(AssetInstance instance)
    {
        if (Current != null)
        {
            Current.Release();
            Pivot.RemoveChild(Current);
            Current.QueueFree();
        }
        Pivot.Rotation = Vector3.Zero;
        Current = instance;
        if (instance != null)
        {
            Pivot.AddChild(instance);
            _ownsEnvironment = instance.Stage.OwnsEnvironment;
            _groundSpec = instance.Stage.Ground;
            _background = instance.Stage.Background;
        }
        Refresh();
    }

    /// <summary>Sizes the ground to the asset and sits it under the lowest point.</summary>
    public void FitGround(Aabb bounds)
    {
        float radius = Current != null && Current.Stage.GroundRadius > 0f ? Current.Stage.GroundRadius : Mathf.Max(bounds.Size.X, bounds.Size.Z) * 0.75f + 1f;
        _ground.Scale = new Vector3(radius, 1f, radius);
        _ground.Position = new Vector3(bounds.GetCenter().X, bounds.Position.Y - 0.011f, bounds.GetCenter().Z);
        _groundMaterial.Uv1Scale = Vector3.One * Mathf.Clamp(0.5f / Mathf.Max(1f, radius / 8f), 0.002f, 0.5f);
    }

    public void Refresh()
    {
        if (_env == null) return;
        _ground.Visible = GroundWanted && _groundSpec;
        _sun.Visible = !_ownsEnvironment;
        _env.Environment = _ownsEnvironment ? null : MakeEnvironment();
        // Light: 0 is dawn low in the east, 0.5 high noon, 1 dusk low in the west.
        float t = Mathf.Clamp(Light, 0f, 1f);
        float elevation = Mathf.Lerp(8f, 62f, Mathf.Sin(t * Mathf.Pi));
        float azimuth = Mathf.Lerp(-100f, 100f, t);
        _sun.RotationDegrees = new Vector3(-elevation, azimuth, 0f);
        float low = 1f - Mathf.Sin(t * Mathf.Pi);
        _sun.LightColor = new Color("fff1dc").Lerp(new Color("ff9a5a"), low * 0.85f);
        _sun.LightEnergy = Mathf.Lerp(1.5f, 0.55f, low);
    }

    Godot.Environment MakeEnvironment()
    {
        var env = new Godot.Environment
        {
            TonemapMode = Godot.Environment.ToneMapper.Filmic,
            GlowEnabled = true, GlowIntensity = 0.35f, GlowBloom = 0.03f,
        };
        switch (_background)
        {
            case "space":
                env.BackgroundMode = Godot.Environment.BGMode.Color;
                env.BackgroundColor = new Color("04060b");
                env.AmbientLightSource = Godot.Environment.AmbientSource.Color;
                env.AmbientLightColor = new Color("7a8294");
                env.AmbientLightEnergy = 0.45f;
                break;
            case "studio":
                env.BackgroundMode = Godot.Environment.BGMode.Color;
                env.BackgroundColor = new Color("6b7076");
                env.AmbientLightSource = Godot.Environment.AmbientSource.Color;
                env.AmbientLightColor = new Color("c8ccd2");
                env.AmbientLightEnergy = 0.7f;
                break;
            default:
                env.BackgroundMode = Godot.Environment.BGMode.Sky;
                env.Sky = new Sky { SkyMaterial = _sky };
                env.AmbientLightSource = Godot.Environment.AmbientSource.Sky;
                env.AmbientLightEnergy = 0.75f;
                break;
        }
        return env;
    }
}
