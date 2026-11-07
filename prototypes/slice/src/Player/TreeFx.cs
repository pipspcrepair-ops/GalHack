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

    public override void _Ready() => AddChild(_stumps);

    public int Stumps => _stumps.GetChildCount();

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
    public void Fell(Vector3 root, Vector3 away, float height, float radius, Color leaf, bool crown, Color? barkColour = null, Mesh body = null, Basis turn = default)
    {
        away.Y = 0f;
        away = away.LengthSquared() < 1e-4f ? Vector3.Forward : away.Normalized();
        Color bark = barkColour ?? new Color(0.36f, 0.25f, 0.16f);
        var stump = new MeshInstance3D
        {
            Mesh = new CylinderMesh { TopRadius = radius * 0.85f, BottomRadius = radius * 1.15f, Height = 0.5f },
            MaterialOverride = new StandardMaterial3D { AlbedoColor = bark, Roughness = 0.95f },
            Position = root + Vector3.Up * 0.2f,
        };
        _stumps.AddChild(stump);
        AddChild(new FallingTree(root, away, height, radius, bark, leaf, crown, body, turn));
    }
}

/// <summary>A felled tree toppling over, lying, and fading away.</summary>
public sealed partial class FallingTree : Node3D
{
    const float FallTime = 1.5f, LieTime = 5f, FadeTime = 2.5f;
    readonly Vector3 _axis;
    readonly StandardMaterial3D _bark, _leaf;
    readonly ShaderMaterial _body;
    float _t;

    public FallingTree(Vector3 root, Vector3 away, float height, float radius, Color bark, Color leaf, bool crown, Mesh body = null, Basis turn = default)
    {
        Position = root;
        _axis = Vector3.Up.Cross(away).Normalized();
        if (body != null)
        {
            // The tree itself goes over, its own shape and colours.
            _body = new ShaderMaterial { Shader = GD.Load<Shader>("res://shaders/flora_fall.gdshader") };
            _body.SetShaderParameter("tint", new Vector3(leaf.R, leaf.G, leaf.B));
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
            _body?.SetShaderParameter("fade", MathF.Max(0f, a));
            if (_bark != null) _bark.AlbedoColor = new Color(_bark.AlbedoColor, MathF.Max(0f, a));
            if (_leaf != null) _leaf.AlbedoColor = new Color(_leaf.AlbedoColor, MathF.Max(0f, a));
            if (a <= 0f) QueueFree();
        }
    }
}
