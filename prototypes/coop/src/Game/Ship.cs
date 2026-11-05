using System;
using System.Collections.Generic;
using Godot;

namespace Coop;

/// <summary>
/// A ship with its own frame of reference (from prototypes/walkable-ship).
///
/// The hull you see flies through the world. Anyone aboard actually walks in the ship's
/// cabin world: a small private physics world, never drawn, holding a motionless copy of the
/// cabin at ship-local coordinates. They are drawn at ship transform × their position in it.
///
/// In co-op a ship runs in one of two ways. On the host it is the real thing: it flies and its
/// cargo is simulated. On a client it is <see cref="Remote"/>: the network sets its transform
/// and its crates every tick and nothing is simulated, but the cabin world is still there, so
/// the local player walks aboard exactly as in single player.
/// </summary>
public partial class Ship : Node3D
{
    public const float MaxSpeed = 120f;
    public const float GroundClearance = 0.85f;
    public const int CrateCount = 7;
    public const int FloatsPerCrate = 7; // position xyz, rotation xyzw

    static readonly Vector3 Acceleration = new(14f, 14f, 24f);   // m/s², per ship axis
    static readonly Vector3 MaxTurnRate = new(1.1f, 1.0f, 1.8f);  // rad/s: pitch, yaw, roll
    const float BoostFactor = 1.8f;
    const float TurnResponse = 5f;

    /// <summary>Which ship this is on the network; the same on every machine.</summary>
    public int NetId;

    /// <summary>Driven by the network instead of simulated here. Set it when creating the ship.</summary>
    public bool Remote { get; init; }

    /// <summary>World-space velocity in m/s.</summary>
    public Vector3 Velocity;

    /// <summary>Spin in ship space, rad/s: x pitch, y yaw, z roll.</summary>
    public Vector3 LocalAngularVelocity;

    /// <summary>Core gravity: on by default.</summary>
    public bool GravityOn { get; private set; } = true;

    /// <summary>The peer in the pilot seat, or 0.</summary>
    public int PilotPeer;
    public bool Piloted => PilotPeer != 0;
    public bool StuntAutopilot;
    public float StuntIntensity = 1f;
    public float StuntSpeed = 30f;

    // Pilot controls, set each physics tick while someone is in the seat.
    public Vector3 ThrustInput;   // x right, y up, z back (forward is -1)
    public Vector3 TurnInput;     // x pitch, y yaw, z roll, each -1..1
    public bool Boost;

    /// <summary>Raised on a remote ship when the local player shoves a crate; the host applies it.</summary>
    public event Action<Ship, int, Vector3> CratePushed;

    /// <summary>
    /// The cabin's private world, in ship-local coordinates: the cabin colliders, the cargo
    /// bodies and anyone aboard. It never moves and is never drawn.
    /// </summary>
    public SubViewport CabinWorld { get; private set; }

    public float Speed => Velocity.Length();
    public Vector3 WorldAngularVelocity => GlobalBasis * LocalAngularVelocity;

    /// <summary>The ship's velocity at a world-space point, including its spin.</summary>
    public Vector3 PointVelocity(Vector3 worldPoint) =>
        Velocity + WorldAngularVelocity.Cross(worldPoint - GlobalPosition);

    /// <summary>The loose crates, as simulated (or placed) in the cabin world.</summary>
    public IReadOnlyList<RigidBody3D> Crates => _crates;

    readonly List<RigidBody3D> _crates = new();
    readonly List<MeshInstance3D> _crateViews = new();
    float _stuntTime;
    int _gravityFlips;

    public override void _Ready()
    {
        ProcessPhysicsPriority = -10; // move before anything reads the ship's transform
        BuildVisuals();
        BuildHull();
        BuildCabinWorld();
        BuildCargo();
    }

    public override void _PhysicsProcess(double delta)
    {
        if (!Remote) Fly((float)delta);
        SyncCargoViews();
    }

    void Fly(float dt)
    {
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
    }

    /// <summary>Host: switch core gravity; floating cargo gets a nudge so you can see it.</summary>
    public void SetGravity(bool on)
    {
        if (on == GravityOn) return;
        GravityOn = on;
        if (Remote) return; // a remote ship's crates are placed by the network
        var rng = new RandomNumberGenerator { Seed = (ulong)(NetId * 1000 + ++_gravityFlips) }; // the same nudges every time
        foreach (RigidBody3D body in _crates)
        {
            body.GravityScale = on ? 1f : 0f;
            body.Sleeping = false;
            if (!on)
            {
                var nudge = new Vector3(rng.RandfRange(-1f, 1f), rng.RandfRange(0.6f, 1.6f), rng.RandfRange(-1f, 1f));
                body.ApplyCentralImpulse(nudge * body.Mass * 0.7f);
            }
        }
    }

    /// <summary>A player shoved crate <paramref name="index"/>. On the host it moves; on a client the host is asked.</summary>
    public void PushCrate(int index, Vector3 impulse)
    {
        if (index < 0 || index >= _crates.Count) return;
        if (Remote) CratePushed?.Invoke(this, index, impulse);
        else _crates[index].ApplyCentralImpulse(impulse);
    }

    /// <summary>True while any crate is moving; asleep cargo is replicated far less often.</summary>
    public bool CargoAwake
    {
        get
        {
            foreach (RigidBody3D body in _crates)
                if (!body.Sleeping) return true;
            return false;
        }
    }

    /// <summary>Packs every crate's ship-local transform: 7 floats per crate.</summary>
    public float[] ReadCargo()
    {
        var data = new float[_crates.Count * FloatsPerCrate];
        for (int i = 0; i < _crates.Count; i++)
        {
            Transform3D t = _crates[i].Transform;
            Quaternion q = t.Basis.GetRotationQuaternion();
            int k = i * FloatsPerCrate;
            data[k] = t.Origin.X; data[k + 1] = t.Origin.Y; data[k + 2] = t.Origin.Z;
            data[k + 3] = q.X; data[k + 4] = q.Y; data[k + 5] = q.Z; data[k + 6] = q.W;
        }
        return data;
    }

    /// <summary>Remote ships: places the crates from replicated cargo, blending two snapshots.</summary>
    public void ApplyCargo(float[] a, float[] b, float alpha)
    {
        if (a == null || a.Length < _crates.Count * FloatsPerCrate) return;
        if (b == null || b.Length != a.Length) b = a;
        for (int i = 0; i < _crates.Count; i++)
        {
            int k = i * FloatsPerCrate;
            var pa = new Vector3(a[k], a[k + 1], a[k + 2]);
            var pb = new Vector3(b[k], b[k + 1], b[k + 2]);
            var qa = new Quaternion(a[k + 3], a[k + 4], a[k + 5], a[k + 6]).Normalized();
            var qb = new Quaternion(b[k + 3], b[k + 4], b[k + 5], b[k + 6]).Normalized();
            _crates[i].Transform = new Transform3D(new Basis(qa.Slerp(qb, alpha)), pa.Lerp(pb, alpha));
        }
    }

    /// <summary>The ship-local position of crate <paramref name="index"/>.</summary>
    public Vector3 CratePosition(int index) => _crates[index].Position;

    public int CrateIndex(RigidBody3D body) => _crates.IndexOf(body);

    /// <summary>Remote ships: the transform and motion the network says the ship has right now.</summary>
    public void ApplyRemoteState(Transform3D transform, Vector3 velocity, Vector3 localSpin)
    {
        Transform = transform;
        Velocity = velocity;
        LocalAngularVelocity = localSpin;
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
        Velocity = -Basis.Z * StuntSpeed + Vector3.Up * climb;
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

    /// <summary>
    /// The cabin world and the motionless cabin that players aboard actually walk on. A
    /// SubViewport with its own World3D gets its own physics space, which keeps stepping while
    /// rendering is off.
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

        // A cargo net across the rear door: crates stay aboard, people walk through.
        var net = new StaticBody3D { Name = "CargoNet", CollisionLayer = Layers.CargoNet, CollisionMask = 0 };
        net.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = ShipLayout.DoorSize }, Position = ShipLayout.DoorCenter });
        CabinWorld.AddChild(net);
    }

    /// <summary>
    /// Loose crates, in the same spots on every machine. On the host they are simulated in the
    /// cabin world; on a client they are frozen and placed by the network, so the local player
    /// still bumps into them.
    /// </summary>
    void BuildCargo()
    {
        var rng = new RandomNumberGenerator { Seed = 7 };
        Color[] colors = { ShipLayout.Orange, ShipLayout.Yellow, ShipLayout.Teal, ShipLayout.Red };
        for (int i = 0; i < CrateCount; i++)
        {
            var size = Vector3.One * rng.RandfRange(0.5f, 0.9f);
            // Somewhere clear of the furniture: a crate born inside a bunk gets flung out through the wall.
            Vector3 local;
            do local = new Vector3(rng.RandfRange(-3f, 3f), 0.6f + i * 0.15f, rng.RandfRange(-5f, 9f));
            while (InsideFurniture(local, size));

            var body = new RigidBody3D
            {
                Name = $"Crate{i}",
                Mass = 20f,
                LinearDamp = 0.3f,
                AngularDamp = 0.3f,
                CollisionLayer = Layers.Interior,
                CollisionMask = Layers.Interior | Layers.CargoNet,
                ContinuousCd = true, // never tunnels through a thin wall or the cargo net
                Freeze = Remote,
                FreezeMode = RigidBody3D.FreezeModeEnum.Kinematic,
            };
            body.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = size } });
            CabinWorld.AddChild(body);
            body.Position = local;

            var view = new MeshInstance3D
            {
                Mesh = new BoxMesh { Size = size },
                MaterialOverride = new StandardMaterial3D { AlbedoColor = colors[i % colors.Length], Roughness = 0.8f },
            };
            AddChild(view);
            _crates.Add(body);
            _crateViews.Add(view);
        }
        SyncCargoViews();
    }

    void SyncCargoViews()
    {
        for (int i = 0; i < _crates.Count; i++)
            _crateViews[i].Transform = _crates[i].Transform; // the cabin world is in ship-local coordinates
    }

    static bool InsideFurniture(Vector3 center, Vector3 size)
    {
        var crate = new Aabb(center - size / 2f, size).Grow(0.05f);
        foreach (var block in ShipLayout.Blocks)
            if (block.Roles.HasFlag(BlockRole.Interior) && block.Center.Y > 0f
                && crate.Intersects(new Aabb(block.Center - block.Size / 2f, block.Size)))
                return true;
        return false;
    }

    static void AddShapes(Node3D body, BlockRole role)
    {
        foreach (var block in ShipLayout.Blocks)
            if (block.Roles.HasFlag(role))
                body.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = block.Size }, Position = block.Center });
    }
}
