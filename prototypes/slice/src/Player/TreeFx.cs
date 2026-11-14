using System;
using Godot;
using Planets.Gen;
using Planets.Look;

namespace Slice.Player;

/// <summary>
/// What chopping looks like: a burst of leaves at each hit on a tree or plant, and, when a tree
/// is felled, its trunk and crown toppling away from you, lying a while and fading away, with a
/// stump left standing where it grew. The stump is kept for as long as the zone stands, apart from
/// the planet's felled list (which keeps the tree itself gone through streaming and saves).
/// </summary>
public sealed partial class TreeFx : Node3D
{
    readonly Node3D _stumps = new() { Name = "Stumps" };
    readonly Random _rng = new(11);

    public TreeFx() { Name = "TreeFx"; }

    public override void _Ready()
    {
        AddChild(_stumps);
        _ = FallingTree.Shader; // loaded now, not in the frame the first tree falls (most of 5 ms)
    }

    public int Stumps => _stumps.GetChildCount() - Holes;

    /// <summary>The kinds that fall over when felled, leaving a stump.</summary>
    public static bool IsTree(Flora kind) => kind is Flora.Conifer or Flora.Broadleaf or Flora.JungleTree or Flora.Palm or Flora.GiantMushroom;

    /// <summary>What flies off a plant: its species' own colour, or its kind's.</summary>
    public static Color LeafOf(Flora kind, GalHack.Plants.Plant species) =>
        species != null ? new Color(species.Leaf.R, species.Leaf.G, species.Leaf.B) : LeafColour(kind);

    /// <summary>The colour of a plant's leaves (or what stands in for them).</summary>
    public static Color LeafColour(Flora kind) => kind switch
    {
        Flora.Conifer => new Color(0.12f, 0.36f, 0.2f),
        Flora.Broadleaf => new Color(0.3f, 0.55f, 0.16f),
        Flora.JungleTree => new Color(0.14f, 0.6f, 0.22f),
        Flora.Palm => new Color(0.48f, 0.64f, 0.2f),
        Flora.Cactus => new Color(0.3f, 0.52f, 0.3f),
        Flora.GiantMushroom => new Color(0.75f, 0.32f, 0.42f),
        Flora.Mushroom => new Color(0.82f, 0.7f, 0.6f),
        Flora.Crystal or Flora.IceShard => new Color(0.7f, 0.9f, 1f),
        _ => new Color(0.32f, 0.52f, 0.2f),
    };

    /// <summary>A burst of leaves (or flakes) flung out from a point.</summary>
    public void Leaves(Vector3 at, Color colour, int count = 16, float spread = 0.6f)
    {
        var mat = new StandardMaterial3D { ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded, AlbedoColor = Colors.White, CullMode = BaseMaterial3D.CullModeEnum.Disabled, VertexColorUseAsAlbedo = true };
        var p = new CpuParticles3D
        {
            Position = at,
            OneShot = true,
            Amount = count,
            Lifetime = 1.4,
            Explosiveness = 0.95f,
            Mesh = new QuadMesh { Size = new Vector2(0.2f, 0.14f), Material = mat },
            EmissionShape = CpuParticles3D.EmissionShapeEnum.Sphere,
            EmissionSphereRadius = spread,
            Direction = Vector3.Up,
            Spread = 160f,
            InitialVelocityMin = 1.2f,
            InitialVelocityMax = 4.2f,
            Gravity = new Vector3(0f, -3.2f, 0f),
            AngularVelocityMin = -360f,
            AngularVelocityMax = 360f,
            AngleMin = 0f,
            AngleMax = 360f,
            ScaleAmountMin = 0.7f,
            ScaleAmountMax = 1.5f,
            Emitting = true,
            ColorRamp = new Gradient { Colors = new[] { Colors.White, new Color(1f, 1f, 1f, 0f) }, Offsets = new[] { 0.6f, 1f } },
            Color = new Color(colour.R * (0.8f + (float)_rng.NextDouble() * 0.4f), colour.G, colour.B),
        };
        AddChild(p);
        GetTree().CreateTimer(2.2).Timeout += p.QueueFree;
    }

    /// <summary>
    /// A tree felled: the trunk and crown topple away along <paramref name="away"/>, lie, and fade;
    /// a stump stays.
    /// </summary>
    public void Fell(Vector3 root, Vector3 away, float height, float radius, Color leaf, bool crown, Color? barkColour = null, Mesh body = null, Basis turn = default, bool stump = true, Color custom = default)
    {
        away.Y = 0f;
        away = away.LengthSquared() < 1e-4f ? Vector3.Forward : away.Normalized();
        Color bark = barkColour ?? new Color(0.36f, 0.25f, 0.16f);
        if (stump)
            _stumps.AddChild(new MeshInstance3D
            {
                Mesh = new CylinderMesh { TopRadius = radius * 0.85f, BottomRadius = radius * 1.15f, Height = 0.5f },
                MaterialOverride = new StandardMaterial3D { AlbedoColor = bark, Roughness = 0.95f },
                Position = root + Vector3.Up * 0.2f,
            });
        AddChild(new FallingTree(root, away, height, radius, bark, leaf, crown, body, turn, stump ? 0f : radius * 1.5f, custom));
    }

    /// <summary>Root holes left where trees were torn out whole (tests).</summary>
    public int Holes { get; private set; }

    /// <summary>Trees in the air right now.</summary>
    public int Flying
    {
        get
        {
            int n = 0;
            foreach (Node c in GetChildren()) if (c is FlungTree { Landed: false }) n++;
            return n;
        }
    }

    /// <summary>The hole a tree's root plate leaves: a dark dished scar in the ground, kept like a stump.</summary>
    public void Hole(Vector3 root, float radius)
    {
        Holes++;
        _stumps.AddChild(new MeshInstance3D
        {
            Mesh = new SphereMesh { Radius = radius, Height = radius * 0.5f, RadialSegments = 10, Rings = 3 },
            MaterialOverride = new StandardMaterial3D { AlbedoColor = new Color(0.2f, 0.15f, 0.1f), Roughness = 1f },
            Position = root + Vector3.Up * 0.02f,
        });
    }

    /// <summary>
    /// A tree torn up whole, roots and all, and thrown: it flies, turning end over end, and lies where it
    /// lands. <paramref name="body"/> and <paramref name="turn"/> are the plant's own mesh and its standing
    /// turn and scale, so it flies as itself, full size.
    /// </summary>
    public FlungTree Throw(Vector3 root, Vector3 velocity, float height, float radius, Color leaf, Func<float, float, float> ground, Mesh body = null, Basis turn = default, Color custom = default)
    {
        var tree = new FlungTree(root, velocity, height, radius, new Color(0.36f, 0.25f, 0.16f), leaf, ground, body, turn, custom);
        AddChild(tree);
        return tree;
    }
}

/// <summary>A felled tree toppling over, lying a good while along the path it was torn from, and fading away.</summary>
public sealed partial class FallingTree : Node3D
{
    /// <summary>
    /// The standing plant's own look for one that is down: the scatter's colours with the instance's own tint
    /// and glow (<paramref name="custom"/>; the leaf colour only where that isn't known), still, lit as it lies.
    /// </summary>
    public static ShaderMaterial FallMaterial(Color leaf, Color custom)
    {
        var m = new ShaderMaterial { Shader = Shader };
        bool known = custom.R + custom.G + custom.B > 0f;
        m.SetShaderParameter(PlanetSurface.Fx.Names.Of("tint"), known ? new Vector3(custom.R, custom.G, custom.B) : new Vector3(leaf.R, leaf.G, leaf.B));
        m.SetShaderParameter(PlanetSurface.Fx.Names.Of("glow"), known ? custom.A : 0f);
        return m;
    }

    /// <summary>What a fallen plant is drawn with (<see cref="FallMaterial"/>).</summary>
    public static Shader Shader => _shader ??= GD.Load<Shader>("res://shaders/flora_fall.gdshader");

    static Shader _shader;

    const float FallTime = 1.5f, LieTime = 90f, FadeTime = 4f;
    readonly Vector3 _axis;
    readonly StandardMaterial3D _bark, _leaf;
    readonly ShaderMaterial _body;
    float _t;

    public FallingTree(Vector3 root, Vector3 away, float height, float radius, Color bark, Color leaf, bool crown, Mesh body = null, Basis turn = default, float roots = 0f, Color custom = default)
    {
        Position = root;
        _axis = Vector3.Up.Cross(away).Normalized();
        if (roots > 0f)
            AddChild(new MeshInstance3D // torn out by its root plate: the plate comes up with it
            {
                Mesh = new CylinderMesh { TopRadius = roots, BottomRadius = roots * 0.7f, Height = roots * 0.5f, RadialSegments = 8 },
                MaterialOverride = new StandardMaterial3D { AlbedoColor = new Color(0.3f, 0.22f, 0.15f), Roughness = 1f },
            });
        if (body != null)
        {
            // The tree itself goes over, its own shape and colours.
            _body = FallMaterial(leaf, custom);
            AddChild(new MeshInstance3D { Mesh = body, MaterialOverride = _body, Basis = turn });
            return;
        }
        _bark = new StandardMaterial3D { AlbedoColor = bark, Roughness = 0.95f, Transparency = BaseMaterial3D.TransparencyEnum.Alpha };
        _leaf = new StandardMaterial3D { AlbedoColor = leaf, Roughness = 0.9f, Transparency = BaseMaterial3D.TransparencyEnum.Alpha };
        AddChild(new MeshInstance3D
        {
            Mesh = new CylinderMesh { TopRadius = radius * 0.6f, BottomRadius = radius, Height = height },
            MaterialOverride = _bark,
            Position = Vector3.Up * (height * 0.5f),
        });
        if (crown)
            AddChild(new MeshInstance3D
            {
                Mesh = new SphereMesh { Radius = MathF.Max(1.1f, height * 0.32f), Height = MathF.Max(2.2f, height * 0.58f), RadialSegments = 7, Rings = 4 },
                MaterialOverride = _leaf,
                Position = Vector3.Up * (height * 0.95f),
            });
    }

    public override void _Process(double delta)
    {
        _t += (float)delta;
        float fall = Mathf.Clamp(_t / FallTime, 0f, 1f);
        // Slow at first, faster as it goes, and a small settle at the end.
        float angle = Mathf.Pi / 2f * (fall * fall) - (fall >= 1f ? 0.04f * MathF.Exp(-(_t - FallTime) * 6f) * MathF.Sin((_t - FallTime) * 18f) : 0f);
        Basis = new Basis(_axis, angle);
        if (_t > FallTime + LieTime)
        {
            float a = 1f - (_t - FallTime - LieTime) / FadeTime;
            _body?.SetShaderParameter(PlanetSurface.Fx.Names.Of("fade"), MathF.Max(0f, a));
            if (_bark != null) _bark.AlbedoColor = new Color(_bark.AlbedoColor, MathF.Max(0f, a));
            if (_leaf != null) _leaf.AlbedoColor = new Color(_leaf.AlbedoColor, MathF.Max(0f, a));
            if (a <= 0f) QueueFree();
        }
    }
}

/// <summary>
/// A tree a tornado pulled up whole and threw: it flies on its arc turning end over end, comes down and
/// lies on its side where it landed, a good while, then fades. Its root plate comes with it.
/// </summary>
public sealed partial class FlungTree : Node3D
{
    const float LieTime = 90f, FadeTime = 4f, Gravity = 9.8f;
    readonly Func<float, float, float> _ground;
    readonly Vector3 _spinAxis;
    readonly float _spin, _height;
    readonly StandardMaterial3D _bark, _leaf;
    readonly ShaderMaterial _body;
    Vector3 _velocity;
    float _lying = -1f;

    public bool Landed => _lying >= 0f;

    /// <summary>The size it flies at, as drawn: its own mesh at its own scale.</summary>
    public Vector3 Bounds { get; }

    public FlungTree(Vector3 root, Vector3 velocity, float height, float radius, Color bark, Color leaf, Func<float, float, float> ground, Mesh body = null, Basis turn = default, Color custom = default)
    {
        Position = root + Vector3.Up * (height * 0.5f);
        _velocity = velocity;
        _ground = ground;
        _height = height;
        var flat = new Vector3(velocity.X, 0f, velocity.Z);
        _spinAxis = flat.LengthSquared() > 0.01f ? Vector3.Up.Cross(flat).Normalized() : Vector3.Right;
        _spin = 1.2f + 6f / MathF.Max(2f, height);
        _bark = new StandardMaterial3D { AlbedoColor = bark, Roughness = 0.95f, Transparency = BaseMaterial3D.TransparencyEnum.Alpha };
        _leaf = new StandardMaterial3D { AlbedoColor = leaf, Roughness = 0.9f, Transparency = BaseMaterial3D.TransparencyEnum.Alpha };
        // Built about its middle, so it turns about it: the plant itself (its own mesh, turn and scale as it
        // stood), or a trunk and crown where the world's herbarium has no mesh for it; its root plate below.
        if (body != null)
        {
            _body = FallingTree.FallMaterial(leaf, custom);
            var standing = new Transform3D(turn, Vector3.Down * (height * 0.5f));
            AddChild(new MeshInstance3D { Mesh = body, MaterialOverride = _body, Transform = standing });
            Bounds = (new Transform3D(turn, Vector3.Zero) * body.GetAabb()).Size;
        }
        else
        {
            AddChild(new MeshInstance3D { Mesh = new CylinderMesh { TopRadius = radius * 0.6f, BottomRadius = radius, Height = height }, MaterialOverride = _bark });
            AddChild(new MeshInstance3D
            {
                Mesh = new SphereMesh { Radius = MathF.Max(1.1f, height * 0.32f), Height = MathF.Max(2.2f, height * 0.58f), RadialSegments = 7, Rings = 4 },
                MaterialOverride = _leaf,
                Position = Vector3.Up * (height * 0.45f),
            });
            Bounds = new Vector3(radius * 2f, height, radius * 2f);
        }
        AddChild(new MeshInstance3D
        {
            Mesh = new CylinderMesh { TopRadius = radius * 3f, BottomRadius = radius * 2f, Height = radius * 1.5f, RadialSegments = 8 },
            MaterialOverride = new StandardMaterial3D { AlbedoColor = new Color(0.3f, 0.22f, 0.15f), Roughness = 1f },
            Position = Vector3.Down * (height * 0.5f),
        });
    }

    public override void _PhysicsProcess(double delta)
    {
        float dt = (float)delta;
        if (_lying < 0f)
        {
            _velocity += Vector3.Down * Gravity * dt;
            Position += _velocity * dt;
            Basis = new Basis(_spinAxis, _spin * dt) * Basis;
            float ground = _ground(Position.X, Position.Z);
            if (Position.Y - _height * 0.15f > ground) return;
            // Down: it lies along the ground on its side, the way it was turning.
            _lying = 0f;
            Vector3 along = Basis.Y;
            along.Y = 0f;
            along = along.LengthSquared() > 1e-4f ? along.Normalized() : _spinAxis.Cross(Vector3.Up);
            Vector3 x = along.Cross(Vector3.Up).Normalized();
            Basis = new Basis(x, along, x.Cross(along)); // its trunk along the ground
            Position = new Vector3(Position.X, ground + MathF.Max(0.4f, _height * 0.05f), Position.Z);
            return;
        }
        _lying += dt;
        if (_lying < LieTime) return;
        float a = 1f - (_lying - LieTime) / FadeTime;
        _bark.AlbedoColor = new Color(_bark.AlbedoColor, MathF.Max(0f, a));
        _leaf.AlbedoColor = new Color(_leaf.AlbedoColor, MathF.Max(0f, a));
        _body?.SetShaderParameter(PlanetSurface.Fx.Names.Of("fade"), MathF.Max(0f, a));
        if (a <= 0f) QueueFree();
    }
}
