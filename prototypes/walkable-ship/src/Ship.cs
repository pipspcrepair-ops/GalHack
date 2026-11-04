using System.Collections.Generic;
using Godot;

namespace WalkableShip;

/// <summary>
/// A ship with its own frame of reference.
///
/// The hull you see flies through the world. Anyone aboard actually walks on a "ghost" copy
/// of the cabin that sits still, far below the map, on its own physics layer, and is drawn at
/// ship transform × their position on the ghost. However hard the ship turns, rolls or
/// accelerates, the character controller only ever sees a motionless room with gravity
/// pointing at the floor. Loose cargo works the same way.
/// </summary>
public partial class Ship : Node3D
{
    /// <summary>Where the ghost cabin sits. It never moves and never rotates.</summary>
    public static readonly Vector3 GhostOrigin = new(0, -2000, 0);

    public const float MaxSpeed = 120f;
    public const float GroundClearance = 0.85f;

    static readonly Vector3 Acceleration = new(14f, 14f, 24f);   // m/s², per ship axis
    static readonly Vector3 MaxTurnRate = new(1.1f, 1.0f, 1.8f);  // rad/s: pitch, yaw, roll
    const float BoostFactor = 1.8f;
    const float TurnResponse = 5f;

    /// <summary>World-space velocity in m/s.</summary>
    public Vector3 Velocity;

    /// <summary>Spin in ship space, rad/s: x pitch, y yaw, z roll.</summary>
    public Vector3 LocalAngularVelocity;

    /// <summary>Core gravity: on by default, toggled from the console.</summary>
    public bool GravityOn { get; private set; } = true;

    public bool Piloted;
    public bool StuntAutopilot;
    public float StuntIntensity = 1f;

    // Pilot controls, set each physics tick while someone is in the seat.
    public Vector3 ThrustInput;   // x right, y up, z back (forward is -1)
    public Vector3 TurnInput;     // x pitch, y yaw, z roll, each -1..1
    public bool Boost;

    public float Speed => Velocity.Length();
    public Vector3 WorldAngularVelocity => GlobalBasis * LocalAngularVelocity;

    /// <summary>The ship's velocity at a world-space point, including its spin.</summary>
    public Vector3 PointVelocity(Vector3 worldPoint) =>
        Velocity + WorldAngularVelocity.Cross(worldPoint - GlobalPosition);

    readonly List<(RigidBody3D body, MeshInstance3D view)> _cargo = new();
    float _stuntTime;

    public override void _Ready()
    {
        ProcessPhysicsPriority = -10; // move before anything reads the ship's transform
        BuildVisuals();
        BuildHull();
        BuildGhostCabin();
        BuildCargo();
    }

    public override void _PhysicsProcess(double delta)
    {
        float dt = (float)delta;

        if (StuntAutopilot) FlyStunts(dt);
        else if (Piloted) FlyPiloted(dt);
        else LocalAngularVelocity = LocalAngularVelocity.MoveToward(Vector3.Zero, 2f * dt); // autopilot holds course

        Position += Velocity * dt;
        float spin = LocalAngularVelocity.Length();
        if (spin > 1e-5f)
            Basis = (Basis * new Basis(LocalAngularVelocity / spin, spin * dt)).Orthonormalized();

        if (Position.Y < GroundClearance)
        {
            Position = new Vector3(Position.X, GroundClearance, Position.Z);
            if (Velocity.Y < 0) Velocity.Y = 0;
        }

        SyncCargoViews();
    }

    public void ToggleGravity()
    {
        GravityOn = !GravityOn;
        var rng = new RandomNumberGenerator();
        foreach (var (body, _) in _cargo)
        {
            body.GravityScale = GravityOn ? 1f : 0f;
            body.Sleeping = false;
            if (!GravityOn)
            {
                var nudge = new Vector3(rng.RandfRange(-1f, 1f), rng.RandfRange(0.6f, 1.6f), rng.RandfRange(-1f, 1f));
                body.ApplyCentralImpulse(nudge * body.Mass * 0.7f);
            }
        }
    }

    void FlyPiloted(float dt)
    {
        float boost = Boost ? BoostFactor : 1f;
        Vector3 local = Basis.Inverse() * Velocity;
        for (int axis = 0; axis < 3; axis++)
        {
            float input = ThrustInput[axis];
            local[axis] = Mathf.Abs(input) > 0.01f
                ? local[axis] + input * Acceleration[axis] * boost * dt
                : Mathf.MoveToward(local[axis], 0f, Acceleration[axis] * dt); // flight assist: let go and it stops
        }
        Velocity = (Basis * local).LimitLength(MaxSpeed * (Boost ? 1.25f : 1f));

        Vector3 target = TurnInput * MaxTurnRate;
        LocalAngularVelocity = LocalAngularVelocity.MoveToward(target, TurnResponse * dt);
    }

    /// <summary>Barrel rolls and swoops, so you can walk around while the world tumbles past.</summary>
    void FlyStunts(float dt)
    {
        _stuntTime += dt;
        float k = StuntIntensity;
        LocalAngularVelocity = new Vector3(
            0.45f * k * Mathf.Sin(_stuntTime * 0.7f),
            0.2f * k,
            0.9f * k * Mathf.Sin(_stuntTime * 0.45f));
        float climb = Mathf.Clamp((70f - Position.Y) * 0.5f, 0f, 15f);
        Velocity = -Basis.Z * 30f + Vector3.Up * climb;
    }

    void BuildVisuals()
    {
        var materials = new Dictionary<(Color, BlockRole), StandardMaterial3D>();
        foreach (var block in ShipLayout.Blocks)
        {
            if ((block.Roles & (BlockRole.Visual | BlockRole.Glass)) == 0) continue;
            var key = (block.Color, block.Roles & (BlockRole.Glass | BlockRole.Glow));
            if (!materials.TryGetValue(key, out var material))
            {
                material = new StandardMaterial3D { AlbedoColor = block.Color, Roughness = 0.75f };
                if (block.Roles.HasFlag(BlockRole.Glass))
                {
                    material.Transparency = BaseMaterial3D.TransparencyEnum.Alpha;
                    material.Roughness = 0.05f;
                    material.Metallic = 0.3f;
                    material.CullMode = BaseMaterial3D.CullModeEnum.Disabled;
                }
                if (block.Roles.HasFlag(BlockRole.Glow))
                {
                    material.EmissionEnabled = true;
                    material.Emission = block.Color;
                    material.EmissionEnergyMultiplier = 2.5f;
                }
                materials[key] = material;
            }
            AddChild(new MeshInstance3D
            {
                Mesh = new BoxMesh { Size = block.Size },
                MaterialOverride = material,
                Position = block.Center,
            });
        }

        var warm = new Color("ffe2b0");
        AddChild(new OmniLight3D { Position = new Vector3(0, 4.4f, -6), OmniRange = 12, LightEnergy = 1.4f, LightColor = warm });
        AddChild(new OmniLight3D { Position = new Vector3(0, 4.4f, 6), OmniRange = 12, LightEnergy = 1.2f, LightColor = warm });
    }

    /// <summary>The outer hull, for players standing outside. It rides along with the ship.</summary>
    void BuildHull()
    {
        var hull = new AnimatableBody3D { Name = "Hull", CollisionLayer = Layers.ShipHull, CollisionMask = 0 };
        AddChild(hull);
        AddShapes(hull, BlockRole.Hull);
    }

    /// <summary>The motionless copy of the cabin that players aboard actually walk on.</summary>
    void BuildGhostCabin()
    {
        var ghost = new StaticBody3D { Name = "GhostCabin", CollisionLayer = Layers.Interior, CollisionMask = 0, TopLevel = true };
        AddChild(ghost);
        ghost.GlobalPosition = GhostOrigin;
        AddShapes(ghost, BlockRole.Ghost);
    }

    /// <summary>Loose crates: simulated on the ghost cabin, drawn inside the real ship.</summary>
    void BuildCargo()
    {
        var rng = new RandomNumberGenerator { Seed = 7 };
        Color[] colors = { ShipLayout.Orange, ShipLayout.Yellow, ShipLayout.Teal, ShipLayout.Red };
        for (int i = 0; i < 7; i++)
        {
            var size = Vector3.One * rng.RandfRange(0.5f, 0.9f);
            var local = new Vector3(rng.RandfRange(-3f, 3f), 0.6f + i * 0.15f, rng.RandfRange(-5f, 9f));

            var body = new RigidBody3D
            {
                TopLevel = true,
                Mass = 20f,
                LinearDamp = 0.3f,
                AngularDamp = 0.3f,
                CollisionLayer = Layers.Interior,
                CollisionMask = Layers.Interior,
            };
            body.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = size } });
            AddChild(body);
            body.GlobalPosition = GhostOrigin + local;

            var view = new MeshInstance3D
            {
                Mesh = new BoxMesh { Size = size },
                MaterialOverride = new StandardMaterial3D { AlbedoColor = colors[i % colors.Length], Roughness = 0.8f },
            };
            AddChild(view);
            _cargo.Add((body, view));
        }
        SyncCargoViews();
    }

    void SyncCargoViews()
    {
        foreach (var (body, view) in _cargo)
        {
            Transform3D onGhost = body.GlobalTransform;
            view.Transform = new Transform3D(onGhost.Basis, onGhost.Origin - GhostOrigin);
        }
    }

    static void AddShapes(Node3D body, BlockRole role)
    {
        foreach (var block in ShipLayout.Blocks)
            if (block.Roles.HasFlag(role))
                body.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = block.Size }, Position = block.Center });
    }
}
