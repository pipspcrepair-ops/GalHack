using System;
using System.Collections.Generic;
using Godot;

namespace Boarding;

/// <summary>
/// Hit feedback in the world: sparks and decals that match the surface, tracers, muzzle
/// flashes, chunks breaking off blocks, and explosions. Decals and chunks are capped and
/// recycled oldest-first so a long firefight never grows without bound.
/// </summary>
public partial class Effects : Node3D
{
    const int MaxDecals = 180;
    const int MaxChunks = 150;

    readonly Queue<Node3D> _decals = new();
    readonly Queue<Node3D> _chunks = new();
    readonly List<Anim> _anims = new();

    sealed class Anim
    {
        public Node3D Node;
        public float Age, Life;
        public bool Drawn;             // aged only after it has been on screen for one frame
        public Action<float> Update;   // t = 0..1
    }

    static readonly BoxMesh SparkMesh = new() { Size = new Vector3(0.025f, 0.025f, 0.09f) };
    static readonly BoxMesh DustMesh = new() { Size = new Vector3(0.08f, 0.08f, 0.08f) };
    static readonly QuadMesh DecalQuad = new() { Size = Vector2.One };

    public override void _Process(double delta)
    {
        float dt = (float)delta;
        for (int i = _anims.Count - 1; i >= 0; i--)
        {
            Anim a = _anims[i];
            if (a.Drawn) a.Age += dt;
            a.Drawn = true;
            if (!IsInstanceValid(a.Node))
            {
                _anims.RemoveAt(i);
                continue;
            }
            float t = Mathf.Clamp(a.Age / a.Life, 0f, 1f);
            a.Update?.Invoke(t);
            if (t >= 1f)
            {
                a.Node.QueueFree();
                _anims.RemoveAt(i);
            }
        }
    }

    void Animate(Node3D node, float life, Action<float> update = null) =>
        _anims.Add(new Anim { Node = node, Life = life, Update = update });

    // ---- Impacts -----------------------------------------------------------------------

    /// <summary>A bullet hit: sparks and a decal chosen by surface. The decal sticks to the thing hit.</summary>
    public void Impact(Vector3 point, Vector3 normal, Surface surface, Node3D stickTo, float scale = 1f)
    {
        switch (surface)
        {
            case Surface.Metal:
                Sparks(point, normal, new Color("ffb347"), 9, 7f, 0.28f);
                Decal(point, normal, new Color(0.06f, 0.05f, 0.05f, 0.9f), 0.17f * scale, stickTo);
                Decal(point, normal, new Color("ff7b2a"), 0.07f * scale, stickTo, glowFor: 0.6f);
                break;
            case Surface.Armour:
                Sparks(point, normal, new Color("e8f4ff"), 12, 10f, 0.22f);
                Decal(point, normal, new Color(0.75f, 0.75f, 0.78f, 0.7f), 0.1f * scale, stickTo);
                break;
            case Surface.Machine:
                Sparks(point, normal, new Color("ffb347"), 7, 6f, 0.3f);
                Sparks(point, normal, Palette.TealGlow, 6, 4f, 0.45f);
                Decal(point, normal, new Color(0.05f, 0.05f, 0.06f, 0.9f), 0.15f * scale, stickTo);
                break;
            case Surface.Core:
                Sparks(point, normal, Palette.VharnGlow, 14, 8f, 0.4f);
                Decal(point, normal, Palette.VharnGlow, 0.2f * scale, stickTo, glowFor: 1.5f);
                break;
            case Surface.Rock:
                Dust(point, normal, new Color("b07a4a"), 10);
                Decal(point, normal, new Color(0.3f, 0.18f, 0.1f, 0.8f), 0.16f * scale, stickTo);
                break;
            case Surface.Ichor:
                Dust(point, normal, Palette.Ichor, 12, 3.5f);
                break;
        }
    }

    public void Sparks(Vector3 point, Vector3 normal, Color color, int count, float speed, float life)
    {
        var p = new CpuParticles3D
        {
            Emitting = true,
            OneShot = true,
            Explosiveness = 1f,
            Amount = count,
            Lifetime = life,
            Mesh = SparkMesh,
            MaterialOverride = Art.Glow(color, 4f),
            Direction = Vector3.Forward,
            Spread = 55f,
            InitialVelocityMin = speed * 0.5f,
            InitialVelocityMax = speed,
            Gravity = new Vector3(0, -12f, 0),
            ParticleFlagAlignY = false,
            ScaleAmountMin = 0.6f,
            ScaleAmountMax = 1.3f,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        };
        AddChild(p);
        p.GlobalTransform = Facing(point + normal * 0.02f, normal);
        Animate(p, life + 0.1f);
    }

    void Dust(Vector3 point, Vector3 normal, Color color, int count, float speed = 2f)
    {
        var p = new CpuParticles3D
        {
            Emitting = true,
            OneShot = true,
            Explosiveness = 0.9f,
            Amount = count,
            Lifetime = 0.5f,
            Mesh = DustMesh,
            MaterialOverride = Art.Flat(color, 1f),
            Direction = Vector3.Forward,
            Spread = 40f,
            InitialVelocityMin = speed * 0.4f,
            InitialVelocityMax = speed,
            Gravity = new Vector3(0, -4f, 0),
            ScaleAmountMin = 0.5f,
            ScaleAmountMax = 1.5f,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        };
        AddChild(p);
        p.GlobalTransform = Facing(point + normal * 0.05f, normal);
        Animate(p, 0.6f);
    }

    public void Decal(Vector3 point, Vector3 normal, Color color, float size, Node3D stickTo, float glowFor = 0f)
    {
        var decal = new MeshInstance3D
        {
            Mesh = DecalQuad,
            MaterialOverride = glowFor > 0f ? GlowSpot(color) : Art.SpotMaterial(color),
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        };
        Node parent = stickTo != null && IsInstanceValid(stickTo) ? stickTo : this;
        parent.AddChild(decal);
        // QuadMesh faces +Z: point +Z along the surface normal, with a random twist.
        Transform3D t = Facing(point + normal * 0.012f, -normal);
        t.Basis = t.Basis.Rotated(normal, GD.Randf() * Mathf.Tau).Scaled(Vector3.One * size * (0.8f + GD.Randf() * 0.4f));
        decal.GlobalTransform = t;

        if (glowFor > 0f)
        {
            // Hot metal glows and cools: animate a private material's energy down to nothing.
            var material = (StandardMaterial3D)decal.MaterialOverride;
            Animate(decal, glowFor, k => material.AlbedoColor = new Color(color, (1f - k) * 0.95f));
            return;
        }
        _decals.Enqueue(decal);
        while (_decals.Count > MaxDecals)
        {
            Node3D old = _decals.Dequeue();
            if (IsInstanceValid(old)) old.QueueFree();
        }
    }

    static StandardMaterial3D GlowSpot(Color color) => new()
    {
        AlbedoColor = color,
        AlbedoTexture = Art.Spot,
        ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
        Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
        BlendMode = BaseMaterial3D.BlendModeEnum.Add,
        CullMode = BaseMaterial3D.CullModeEnum.Disabled,
    };

    // ---- Shots ---------------------------------------------------------------------------

    /// <summary>A bright streak from muzzle to impact that fades in a few frames.</summary>
    public void Tracer(Vector3 from, Vector3 to, Color color, float width = 0.03f, float life = 0.06f)
    {
        float length = from.DistanceTo(to);
        if (length < 0.2f) return;
        var tracer = new MeshInstance3D
        {
            Mesh = new BoxMesh { Size = new Vector3(width, width, length) },
            MaterialOverride = new StandardMaterial3D
            {
                AlbedoColor = color,
                ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
                Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
                BlendMode = BaseMaterial3D.BlendModeEnum.Add,
            },
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        };
        AddChild(tracer);
        Vector3 dir = (to - from) / length;
        tracer.GlobalTransform = new Transform3D(Facing(Vector3.Zero, dir).Basis, (from + to) * 0.5f);
        var material = (StandardMaterial3D)tracer.MaterialOverride;
        Animate(tracer, life, t => material.AlbedoColor = new Color(color, 1f - t));
    }

    /// <summary>An NPC muzzle flash: a hot billboard and a flicker of light.</summary>
    public void Flash(Vector3 at, Color color, float size = 0.5f)
    {
        var flash = new MeshInstance3D
        {
            Mesh = new QuadMesh { Size = Vector2.One * size },
            MaterialOverride = new StandardMaterial3D
            {
                AlbedoColor = color,
                AlbedoTexture = Art.Spot,
                ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
                Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
                BlendMode = BaseMaterial3D.BlendModeEnum.Add,
                BillboardMode = BaseMaterial3D.BillboardModeEnum.Enabled,
            },
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        };
        AddChild(flash);
        flash.GlobalPosition = at;
        var light = new OmniLight3D { LightColor = color, LightEnergy = 3f, OmniRange = 5f };
        flash.AddChild(light);
        Animate(flash, 0.07f, t => light.LightEnergy = 3f * (1f - t));
    }

    // ---- Debris and explosions -----------------------------------------------------------

    /// <summary>Tumbling chunks thrown out of a broken block or piece.</summary>
    public void Chunks(Vector3 center, Vector3 size, Color color, int count)
    {
        var material = Art.Flat(color.Darkened(0.15f), 0.9f);
        for (int i = 0; i < count; i++)
        {
            float s = Mathf.Clamp(Mathf.Min(size.X, Mathf.Min(size.Y, size.Z)) * (0.25f + GD.Randf() * 0.3f), 0.08f, 0.6f);
            var chunkSize = new Vector3(s, s * (0.6f + GD.Randf() * 0.6f), s * (0.7f + GD.Randf() * 0.6f));
            var body = new RigidBody3D
            {
                CollisionLayer = Layers.Debris,
                CollisionMask = Layers.World | Layers.Ship,
                Mass = 2f,
                LinearDamp = 0.2f,
                AngularDamp = 0.5f,
            };
            body.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = chunkSize } });
            body.AddChild(Art.Mesh(Art.BoxMesh(chunkSize), material, Vector3.Zero));
            var offset = new Vector3(GD.Randf() - 0.5f, GD.Randf() - 0.5f, GD.Randf() - 0.5f) * size * 0.8f;
            body.Position = ToLocal(center + offset);   // placed before entering the tree, like every body here
            AddChild(body);
            body.LinearVelocity = offset.Normalized() * (2f + GD.Randf() * 3f) + Vector3.Up * (1.5f + GD.Randf() * 2.5f);
            body.AngularVelocity = new Vector3(GD.Randf() - 0.5f, GD.Randf() - 0.5f, GD.Randf() - 0.5f) * 12f;

            _chunks.Enqueue(body);
            Animate(body, 9f, t =>
            {
                if (t > 0.85f) body.Scale = Vector3.One * Mathf.Max(0.01f, (1f - t) / 0.15f);
            });
        }
        while (_chunks.Count > MaxChunks)
        {
            Node3D old = _chunks.Dequeue();
            if (IsInstanceValid(old)) old.QueueFree();
        }
    }

    public void Explosion(Vector3 at, float radius, Color color)
    {
        var ball = new MeshInstance3D
        {
            Mesh = new SphereMesh { Radius = 1f, Height = 2f, RadialSegments = 16, Rings = 8 },
            MaterialOverride = new StandardMaterial3D
            {
                AlbedoColor = color,
                ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
                Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
                BlendMode = BaseMaterial3D.BlendModeEnum.Add,
            },
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        };
        AddChild(ball);
        ball.GlobalPosition = at;
        var light = new OmniLight3D { LightColor = color, LightEnergy = 8f, OmniRange = radius * 5f };
        ball.AddChild(light);
        var material = (StandardMaterial3D)ball.MaterialOverride;
        Animate(ball, 0.45f, t =>
        {
            ball.Scale = Vector3.One * radius * (0.3f + 0.9f * Mathf.Sqrt(t));
            material.AlbedoColor = new Color(color.Lerp(new Color("fff2c0"), 1f - t), 1f - t);
            light.LightEnergy = 8f * (1f - t);
        });
        Sparks(at, Vector3.Up, new Color("ffb347"), 40, 14f, 0.7f);
        Chunks(at, Vector3.One * radius * 0.5f, Palette.Dark, 6);
    }

    /// <summary>A transform at a point whose -Z axis looks along a direction (any direction).</summary>
    public static Transform3D Facing(Vector3 at, Vector3 forward)
    {
        if (forward.LengthSquared() < 1e-6f) forward = Vector3.Forward;
        Vector3 up = Mathf.Abs(forward.Normalized().Y) > 0.95f ? Vector3.Right : Vector3.Up;
        return new Transform3D(Basis.LookingAt(forward, up), at);
    }
}
