using System.Collections.Generic;
using Godot;

namespace Saves;

/// <summary>
/// The walkable ship from prototypes/walkable-ship, with its own cabin world: a private physics
/// world, never drawn, holding a motionless copy of the cabin at ship-local coordinates. Anyone
/// aboard walks in there, so whatever the ship does, and wherever it goes, they ride untouched.
///
/// Changes for this prototype: what is in the cabin (crates, shelf blocks, the player) comes from
/// saved records; a ship nobody flies brakes to a stop instead of drifting off across a zone
/// edge; it freezes during a climb or re-entry tunnel; and in space a force field closes the door.
/// </summary>
public partial class Ship : Node3D
{
    public const float MaxSpeed = 120f;
    public const float GroundClearance = 0.85f;

    static readonly Vector3 Acceleration = new(14f, 14f, 24f);   // m/s², per ship axis
    static readonly Vector3 MaxTurnRate = new(1.1f, 1.0f, 1.8f);  // rad/s: pitch, yaw, roll
    const float BoostFactor = 1.8f;
    const float TurnResponse = 5f;

    /// <summary>The ship's record.</summary>
    public string RecordId { get; init; }

    /// <summary>World-space velocity in m/s.</summary>
    public Vector3 Velocity;

    /// <summary>Spin in ship space, rad/s: x pitch, y yaw, z roll.</summary>
    public Vector3 LocalAngularVelocity;

    /// <summary>Gravity aboard is always on here (walkable-ship can switch it off).</summary>
    public bool GravityOn => true;

    public bool Piloted;

    /// <summary>Held still while a climb or re-entry tunnel plays.</summary>
    public bool Frozen;

    // Pilot controls, set each physics tick while someone is in the seat.
    public Vector3 ThrustInput;   // x right, y up, z back (forward is -1)
    public Vector3 TurnInput;     // x pitch, y yaw, z roll, each -1..1
    public bool Boost;

    /// <summary>The cabin's private world, in ship-local coordinates. It never moves and is never drawn.</summary>
    public SubViewport CabinWorld { get; private set; }

    public float Speed => Velocity.Length();
    public Vector3 WorldAngularVelocity => GlobalBasis * LocalAngularVelocity;

    public Vector3 PointVelocity(Vector3 worldPoint) =>
        Velocity + WorldAngularVelocity.Cross(worldPoint - GlobalPosition);

    /// <summary>In space there is no ground to clamp to, and the door's force field is up.</summary>
    public bool InSpace
    {
        get => _inSpace;
        set
        {
            _inSpace = value;
            if (_doorField == null) return;
            _doorField.CollisionLayer = value ? Layers.Interior : 0;
            _doorGlow.Visible = value;
        }
    }

    bool _inSpace;
    StaticBody3D _doorField;
    MeshInstance3D _doorGlow;

    public override void _Ready()
    {
        ProcessPhysicsPriority = -10; // move before anything reads the ship's transform
        BuildVisuals();
        BuildHull();
        BuildCabinWorld();
        InSpace = _inSpace;
    }

    public override void _PhysicsProcess(double delta)
    {
        if (Frozen) return;
        float dt = (float)delta;

        if (Piloted) FlyPiloted(dt);
        else
        {
            // Nobody at the controls: brake to a stop, so a parked ship stays where it was left.
            LocalAngularVelocity = LocalAngularVelocity.MoveToward(Vector3.Zero, 2f * dt);
            Velocity = Velocity.MoveToward(Vector3.Zero, 20f * dt);
        }

        Position += Velocity * dt;
        float spin = LocalAngularVelocity.Length();
        if (spin > 1e-5f)
            Basis = (Basis * new Basis(LocalAngularVelocity / spin, spin * dt)).Orthonormalized();

        if (!InSpace && Position.Y < GroundClearance)
        {
            Position = new Vector3(Position.X, GroundClearance, Position.Z);
            if (Velocity.Y < 0) Velocity.Y = 0;
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

        _doorGlow = new MeshInstance3D
        {
            Mesh = new BoxMesh { Size = new Vector3(2.4f, 3f, 0.05f) },
            Position = new Vector3(0, 1.5f, 12.25f),
            MaterialOverride = new StandardMaterial3D
            {
                AlbedoColor = new Color(0.04f, 0.58f, 0.59f, 0.28f),
                Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
                EmissionEnabled = true,
                Emission = ShipLayout.Teal,
                EmissionEnergyMultiplier = 0.8f,
                CullMode = BaseMaterial3D.CullModeEnum.Disabled,
            },
        };
        AddChild(_doorGlow);
    }

    /// <summary>The outer hull, for players standing outside. It rides along with the ship.</summary>
    void BuildHull()
    {
        var hull = new AnimatableBody3D { Name = "Hull", CollisionLayer = Layers.ShipHull, CollisionMask = 0 };
        AddChild(hull);
        AddShapes(hull, BlockRole.Hull);
    }

    /// <summary>
    /// The cabin world and the motionless cabin that players aboard walk on. A SubViewport with
    /// its own World3D gets its own physics space, which keeps stepping while rendering is off.
    /// </summary>
    void BuildCabinWorld()
    {
        CabinWorld = new SubViewport
        {
            Name = "CabinWorld",
            OwnWorld3D = true,
            RenderTargetUpdateMode = SubViewport.UpdateMode.Disabled,
            Size = new Vector2I(2, 2),
        };
        AddChild(CabinWorld);

        var cabin = new StaticBody3D { Name = "Cabin", CollisionLayer = Layers.Interior, CollisionMask = 0 };
        CabinWorld.AddChild(cabin);
        AddShapes(cabin, BlockRole.Interior);

        // No spacewalks in this prototype: in space the open door is closed by a force field.
        _doorField = new StaticBody3D { Name = "DoorField", CollisionLayer = 0, CollisionMask = 0 };
        _doorField.AddChild(new CollisionShape3D
        {
            Shape = new BoxShape3D { Size = new Vector3(2.6f, 3.2f, 0.4f) },
            Position = new Vector3(0, 1.5f, 12.25f),
        });
        CabinWorld.AddChild(_doorField);
    }

    static void AddShapes(Node3D body, BlockRole role)
    {
        foreach (var block in ShipLayout.Blocks)
            if (block.Roles.HasFlag(role))
                body.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = block.Size }, Position = block.Center });
    }
}
