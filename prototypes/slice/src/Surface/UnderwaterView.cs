using Godot;
using PlanetSurface.World;

namespace Slice.Surface;

/// <summary>
/// With the camera under the surface the sky is gone: the liquid's own murk closes in, thicker and
/// darker the deeper you are, a tint lies over the view (under the visor's own layers), a few
/// bubbles drift up past you, and the surface shows from below (planet-surface's water shader
/// draws its underside). As planet-surface's own under-water view does it, on the slice's camera.
/// </summary>
public partial class UnderwaterView : Node3D
{
    public SurfaceZone Zone;

    /// <summary>The camera is under the surface.</summary>
    public bool CameraUnder { get; private set; }

    /// <summary>How far under, metres.</summary>
    public float Depth { get; private set; }

    Environment _murk;
    ColorRect _tint;
    GpuParticles3D _bubbles;
    Color _shallow, _deep;

    public override void _Ready()
    {
        var layer = new CanvasLayer { Name = "Tint", Layer = -1 };
        _tint = new ColorRect { Visible = false, MouseFilter = Control.MouseFilterEnum.Ignore };
        _tint.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        layer.AddChild(_tint);
        AddChild(layer);
    }

    public void SetWorld(PlanetWorld planet)
    {
        WorldLook look = planet.Look;
        _shallow = look.Shallow;
        _deep = look.Deep;
        _murk = new Environment
        {
            BackgroundMode = Environment.BGMode.Color,
            BackgroundColor = _deep,
            AmbientLightSource = Environment.AmbientSource.Color,
            AmbientLightColor = _shallow,
            AmbientLightEnergy = 0.9f,
            TonemapMode = Environment.ToneMapper.Filmic,
            FogEnabled = true,
            FogMode = Environment.FogModeEnum.Exponential,
            FogLightColor = _deep.Lerp(_shallow, 0.45f),
            FogDensity = 0.05f,
            FogSkyAffect = 1f,
        };
        _tint.Color = new Color(_shallow, 0.2f);
    }

    GpuParticles3D Bubbles()
    {
        var soft = new GradientTexture2D
        {
            Width = 16, Height = 16, Fill = GradientTexture2D.FillEnum.Radial, FillFrom = new Vector2(0.5f, 0.5f), FillTo = new Vector2(1f, 0.5f),
            Gradient = new Gradient { Colors = new[] { new Color(1, 1, 1, 0.1f), new Color(1, 1, 1, 0.8f), new Color(1, 1, 1, 0) }, Offsets = new[] { 0.3f, 0.75f, 1f } },
        };
        var node = new GpuParticles3D
        {
            Name = "Bubbles", Amount = 36, Lifetime = 3.2f, LocalCoords = false, Visible = false,
            VisibilityAabb = new Aabb(new Vector3(-10, -10, -10), new Vector3(20, 20, 20)),
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off, PhysicsInterpolationMode = PhysicsInterpolationModeEnum.Off,
            ProcessMaterial = new ParticleProcessMaterial
            {
                EmissionShape = ParticleProcessMaterial.EmissionShapeEnum.Box, EmissionBoxExtents = new Vector3(5, 3, 5),
                Gravity = new Vector3(0f, 0.5f, 0f), Direction = Vector3.Up, Spread = 12f, InitialVelocityMin = 0.25f, InitialVelocityMax = 0.8f,
                ScaleMin = 0.4f, ScaleMax = 1.5f,
            },
            DrawPass1 = new QuadMesh { Size = new Vector2(0.05f, 0.05f) },
            MaterialOverride = new StandardMaterial3D
            {
                ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded, Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
                BillboardMode = BaseMaterial3D.BillboardModeEnum.Particles, AlbedoTexture = soft, AlbedoColor = new Color(0.8f, 0.95f, 1f, 0.5f),
                DepthDrawMode = BaseMaterial3D.DepthDrawModeEnum.Disabled, DisableFog = true,
            },
        };
        AddChild(node);
        return node;
    }

    /// <summary>Each frame, from where the camera is.</summary>
    public void Follow(Vector3 eye, Camera3D camera)
    {
        if (_murk == null || camera == null) return;
        bool under = Zone.Water.Under(eye, out float depth);
        Depth = depth;
        if (under)
        {
            // Light gives out with depth, by the sun that is up there.
            float light = Mathf.Lerp(0.18f, 1f, Zone.Sky.Daylight) * Mathf.Exp(-depth * 0.035f);
            _murk.FogDensity = 0.05f + 0.0025f * depth;
            _murk.FogLightColor = _deep.Lerp(_shallow, 0.45f) * (0.25f + 0.75f * light);
            _murk.BackgroundColor = _deep * (0.25f + 0.75f * light);
            _murk.AmbientLightEnergy = 0.2f + 0.7f * light;
            _bubbles ??= Bubbles();
            _bubbles.GlobalPosition = eye;
        }
        if (under == CameraUnder) return;
        CameraUnder = under;
        camera.Environment = under ? _murk : null;
        _tint.Visible = under;
        if (_bubbles != null) _bubbles.Visible = under;
    }
}
