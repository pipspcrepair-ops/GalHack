using Godot;

namespace Invasions.World;

/// <summary>
/// Throwaway visual effects: tracers, flashes, explosions, debris, smoke. Purely cosmetic; nothing
/// here holds game state, so it is free to vanish. Effects live under one root with physics
/// interpolation off, because they are placed once and never move on the physics clock.
/// </summary>
public static class Fx
{
    public static Node3D Root;

    static bool Ready => Root != null && GodotObject.IsInstanceValid(Root) && Root.IsInsideTree();

    public static void Tracer(Vector3 from, Vector3 to, Color color, float width = 0.045f, float life = 0.07f)
    {
        if (!Ready) return;
        Vector3 d = to - from;
        float length = d.Length();
        if (length < 0.05f) return;
        Vector3 dir = d / length;
        Vector3 up = Mathf.Abs(dir.Y) > 0.98f ? Vector3.Right : Vector3.Up;
        var mesh = new MeshInstance3D
        {
            Mesh = new BoxMesh { Size = new Vector3(width, width, length) },
            MaterialOverride = Palette.Glow(color, 2.2f),
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        };
        Root.AddChild(mesh);
        mesh.GlobalTransform = new Transform3D(Basis.LookingAt(dir, up), from + d * 0.5f);
        Expire(mesh, life);
    }

    public static void Flash(Vector3 at, Color color, float range = 5f, float energy = 3f, float life = 0.05f)
    {
        if (!Ready) return;
        var light = new OmniLight3D { OmniRange = range, LightEnergy = energy, LightColor = color, ShadowEnabled = false };
        Root.AddChild(light);
        light.GlobalPosition = at;
        Expire(light, life);
    }

    public static void Spark(Vector3 at, Color color, float size = 0.22f)
    {
        if (!Ready) return;
        var mesh = new MeshInstance3D
        {
            Mesh = new SphereMesh { Radius = size, Height = size * 2, RadialSegments = 6, Rings = 3 },
            MaterialOverride = Palette.Glow(color, 5f),
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        };
        Root.AddChild(mesh);
        mesh.GlobalPosition = at;
        Expire(mesh, 0.06f);
    }

    public static void Explosion(Vector3 at, float radius, bool withDebris = true)
    {
        if (!Ready) return;
        var material = new StandardMaterial3D
        {
            AlbedoColor = new Color(1f, 0.72f, 0.25f, 0.95f),
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
        };
        var ball = new MeshInstance3D
        {
            Mesh = new SphereMesh { Radius = 0.5f, Height = 1f, RadialSegments = 14, Rings = 7 },
            MaterialOverride = material,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        };
        Root.AddChild(ball);
        ball.GlobalPosition = at;
        ball.Scale = Vector3.One * 0.3f;
        Tween tween = ball.CreateTween().SetParallel();
        tween.TweenProperty(ball, "scale", Vector3.One * radius * 2f, 0.32f).SetEase(Tween.EaseType.Out).SetTrans(Tween.TransitionType.Quad);
        tween.TweenProperty(material, "albedo_color", new Color(0.35f, 0.12f, 0.05f, 0f), 0.5f);
        tween.Chain().TweenCallback(Callable.From(ball.QueueFree));

        Flash(at + Vector3.Up, Palette.Fire, radius * 5f, 8f, 0.18f);
        Puff(at, radius);
        if (withDebris) Debris(at, Palette.Black, 10, 9f);
    }

    /// <summary>Chunks that fly out and fall. Cosmetic only: a broken block's loot goes to a crate, never here.</summary>
    public static void Debris(Vector3 at, Color color, int count, float speed, float size = 0.35f)
    {
        if (!Ready) return;
        var rng = new RandomNumberGenerator();
        for (int i = 0; i < count; i++)
        {
            var chunk = new DebrisChunk
            {
                Mesh = new BoxMesh { Size = Vector3.One * size * rng.RandfRange(0.5f, 1.3f) },
                MaterialOverride = Palette.Mat(color),
                Velocity = new Vector3(rng.RandfRange(-1f, 1f), rng.RandfRange(0.4f, 1.4f), rng.RandfRange(-1f, 1f)) * speed,
                Spin = new Vector3(rng.RandfRange(-8f, 8f), rng.RandfRange(-8f, 8f), rng.RandfRange(-8f, 8f)),
                Life = rng.RandfRange(1.2f, 2.2f),
            };
            Root.AddChild(chunk);
            chunk.GlobalPosition = at + new Vector3(rng.RandfRange(-0.6f, 0.6f), rng.RandfRange(0f, 1f), rng.RandfRange(-0.6f, 0.6f));
        }
    }

    /// <summary>A short burst of dust and smoke.</summary>
    public static void Puff(Vector3 at, float scale)
    {
        if (!Ready) return;
        CpuParticles3D p = SmokeEmitter(scale, 14, 1.6f, oneShot: true);
        Root.AddChild(p);
        p.GlobalPosition = at;
        p.Emitting = true;
        Expire(p, 2.5f);
    }

    /// <summary>A lasting smoke column, e.g. over a wrecked core.</summary>
    public static CpuParticles3D SmokeColumn(Node3D parent, Vector3 localPosition, float scale)
    {
        CpuParticles3D p = SmokeEmitter(scale, 28, 6f, oneShot: false);
        p.Position = localPosition;
        parent.AddChild(p);
        p.Emitting = true;
        return p;
    }

    static CpuParticles3D SmokeEmitter(float scale, int amount, float lifetime, bool oneShot)
    {
        var ramp = new Gradient();
        ramp.SetColor(0, new Color(0.2f, 0.17f, 0.15f, 0.6f));
        ramp.SetColor(1, new Color(0.42f, 0.38f, 0.34f, 0f));
        return new CpuParticles3D
        {
            Amount = amount,
            Lifetime = lifetime,
            OneShot = oneShot,
            Explosiveness = oneShot ? 0.9f : 0f,
            Mesh = new SphereMesh { Radius = 0.5f, Height = 1f, RadialSegments = 8, Rings = 4 },
            MaterialOverride = new StandardMaterial3D
            {
                VertexColorUseAsAlbedo = true,
                Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
                ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            },
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            EmissionShape = CpuParticles3D.EmissionShapeEnum.Sphere,
            EmissionSphereRadius = 0.8f * scale,
            Direction = Vector3.Up,
            Spread = oneShot ? 70f : 14f,
            InitialVelocityMin = 1.5f * scale,
            InitialVelocityMax = 3.5f * scale,
            Gravity = new Vector3(0.5f, oneShot ? -1.5f : 0.6f, 0.2f),
            ScaleAmountMin = 1.2f * scale,
            ScaleAmountMax = 3f * scale,
            ColorRamp = ramp,
        };
    }

    static void Expire(Node node, float seconds)
    {
        SceneTreeTimer timer = node.GetTree().CreateTimer(seconds, processAlways: false, processInPhysics: true);
        timer.Timeout += () =>
        {
            if (GodotObject.IsInstanceValid(node)) node.QueueFree();
        };
    }
}

/// <summary>A falling chunk of debris that removes itself.</summary>
public partial class DebrisChunk : MeshInstance3D
{
    public Vector3 Velocity, Spin;
    public float Life = 1.5f;

    public override void _PhysicsProcess(double delta)
    {
        float dt = (float)delta;
        Velocity += Vector3.Down * 18f * dt;
        Position += Velocity * dt;
        Rotation += Spin * dt;
        if (Position.Y < 0.15f)
        {
            Position = new Vector3(Position.X, 0.15f, Position.Z);
            Velocity *= new Vector3(0.6f, -0.25f, 0.6f);
            Spin *= 0.6f;
        }
        Life -= dt;
        if (Life < 0.3f) Scale = Vector3.One * Mathf.Max(0.01f, Life / 0.3f);
        if (Life <= 0f) QueueFree();
    }
}
