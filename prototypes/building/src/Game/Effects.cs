using Godot;

namespace BlockBuilding;

/// <summary>
/// Short-lived feedback: a white flash that settles onto a placed block, a puff of debris when
/// one comes off, and the blaster's tracer and explosion. All built from primitives in code.
/// </summary>
public partial class Effects : Node3D
{
    StandardMaterial3D _flash, _tracer, _fireball;
    BoxMesh _debrisMesh;

    public override void _Ready()
    {
        PhysicsInterpolationMode = PhysicsInterpolationModeEnum.Off; // effects are placed and tweened per frame
        _flash = Unshaded(new Color(1f, 0.97f, 0.85f, 0.85f));
        _tracer = Unshaded(new Color(1f, 0.55f, 0.15f, 1f));
        _tracer.EmissionEnabled = true;
        _fireball = Unshaded(new Color(1f, 0.6f, 0.2f, 0.9f));
        _debrisMesh = new BoxMesh();
    }

    static StandardMaterial3D Unshaded(Color c) => new()
    {
        ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
        Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
        AlbedoColor = c,
        CullMode = BaseMaterial3D.CullModeEnum.Back,
    };

    /// <summary>A bright shell that shrinks onto the new block and fades: the "clunk" you can see.</summary>
    public void PlaceFlash(Transform3D cell, float size)
    {
        var material = (StandardMaterial3D)_flash.Duplicate();
        var box = new MeshInstance3D
        {
            Mesh = new BoxMesh { Size = Vector3.One * size },
            MaterialOverride = material,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        };
        AddChild(box);
        box.GlobalTransform = cell.ScaledLocal(Vector3.One * 1.18f);
        Tween tween = CreateTween().SetParallel();
        tween.TweenProperty(box, "scale", Vector3.One * 1.01f, 0.16f).SetTrans(Tween.TransitionType.Back).SetEase(Tween.EaseType.Out);
        tween.TweenProperty(material, "albedo_color:a", 0f, 0.22f);
        tween.Chain().TweenCallback(Callable.From(box.QueueFree));
    }

    /// <summary>Chunks of the removed block tumbling away.</summary>
    public void Debris(Vector3 at, float size, Color color, int count = 14, float speed = 4f)
    {
        var particles = new CpuParticles3D
        {
            Emitting = false,
            OneShot = true,
            Amount = count,
            Lifetime = 1.1f,
            Explosiveness = 1f,
            Mesh = _debrisMesh,
            Direction = Vector3.Up,
            Spread = 70f,
            InitialVelocityMin = speed * 0.4f,
            InitialVelocityMax = speed,
            Gravity = new Vector3(0, -12f, 0),
            AngularVelocityMin = -360f,
            AngularVelocityMax = 360f,
            ScaleAmountMin = size * 0.12f,
            ScaleAmountMax = size * 0.3f,
            Color = color,
            EmissionShape = CpuParticles3D.EmissionShapeEnum.Box,
            EmissionBoxExtents = Vector3.One * size * 0.4f,
        };
        particles.MaterialOverride = new StandardMaterial3D { VertexColorUseAsAlbedo = true, Roughness = 0.7f };
        AddChild(particles);
        particles.GlobalPosition = at;
        particles.Emitting = true;
        GetTree().CreateTimer(1.5).Timeout += particles.QueueFree;
    }

    /// <summary>The blaster bolt from the muzzle to what it hit.</summary>
    public void Tracer(Vector3 from, Vector3 to)
    {
        float length = from.DistanceTo(to);
        if (length < 0.1f) return;
        var material = (StandardMaterial3D)_tracer.Duplicate();
        var bolt = new MeshInstance3D
        {
            Mesh = new BoxMesh { Size = new Vector3(0.12f, 0.12f, length) },
            MaterialOverride = material,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        };
        AddChild(bolt);
        bolt.GlobalTransform = new Transform3D(Basis.LookingAt((to - from).Normalized(), Mathf.Abs((to - from).Normalized().Y) > 0.99f ? Vector3.Right : Vector3.Up), (from + to) * 0.5f);
        Tween tween = CreateTween();
        tween.TweenProperty(material, "albedo_color:a", 0f, 0.12f);
        tween.TweenCallback(Callable.From(bolt.QueueFree));
    }

    /// <summary>A fireball, a flash of light and flying debris.</summary>
    public void Explosion(Vector3 at, float radius)
    {
        var material = (StandardMaterial3D)_fireball.Duplicate();
        var ball = new MeshInstance3D
        {
            Mesh = new SphereMesh { Radius = radius * 0.5f, Height = radius, RadialSegments = 20, Rings = 10 },
            MaterialOverride = material,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        };
        AddChild(ball);
        ball.GlobalPosition = at;
        var light = new OmniLight3D { LightColor = new Color("ff9a3c"), LightEnergy = 9f, OmniRange = radius * 6f };
        AddChild(light);
        light.GlobalPosition = at;

        Tween tween = CreateTween().SetParallel();
        tween.TweenProperty(ball, "scale", Vector3.One * 2.4f, 0.35f).SetEase(Tween.EaseType.Out).SetTrans(Tween.TransitionType.Expo);
        tween.TweenProperty(material, "albedo_color", new Color(0.25f, 0.2f, 0.18f, 0f), 0.45f);
        tween.TweenProperty(light, "light_energy", 0f, 0.4f);
        tween.Chain().TweenCallback(Callable.From(() => { ball.QueueFree(); light.QueueFree(); }));

        Debris(at, radius * 0.5f, new Color("3a3532"), 26, 11f);
        Debris(at, radius * 0.25f, new Color("ffb347"), 16, 14f);
    }
}
