using System.Collections.Generic;
using Godot;
using Invasions.Director;
using Invasions.Model;
using Invasions.World;

namespace Invasions.Combat;

/// <summary>
/// PLACEHOLDER Vharn foot soldier, to be replaced by the boarding prototype's soldier AI.
///
/// Movement is an <see cref="InvaderNavigator"/>: the breach planner's route to the core, smoothed
/// only where its whole body fits, with local avoidance and stuck recovery. When a block is in the way
/// it holds at a safe spot and breaks it (breachers plant charges, everyone shoots it), then carries
/// on; when any block falls it re-plans, so a squad converges on fresh breaches. It shoots players and
/// turrets it can see on the way, through fire control. Withdrawing, it plans its way back out and
/// walks to its ride (or out of the area).
/// </summary>
public partial class PlaceholderSoldier : CharacterBody3D, IDamageable, IInvaderTroop
{
    public CombatWorld World;
    public string Role { get; init; } = "rifleman";
    public TroopState State { get; private set; } = TroopState.Assaulting;
    public IInvaderVehicle Ride { get; set; }

    public Faction Faction => Faction.Vharn;
    public TargetClass Class => TargetClass.OnFoot;
    public bool IsAlive => _hp > 0f;
    public bool IsGone => State == TroopState.Gone;
    public Node3D Body => this;
    public Vector3 AimPoint => GlobalPosition + Vector3.Up * 1.3f;

    /// <summary>The block it is currently breaking through, if any (for tests and debugging).</summary>
    public Block BreachTarget => _nav?.BreachTarget;

    /// <summary>At an engagement position: holding in front of the block in the way, or within rifle reach of it.</summary>
    public bool AtBreach => BreachTarget is { Alive: true } b &&
                            (_nav.Holding || Flat(GlobalPosition, World.BlockAim(b)) < InvaderNavigator.ShootReach);
    public bool InCoreRoom => World.Model.InCoreRoom(new GroundPos(GlobalPosition.X, GlobalPosition.Z));
    /// <summary>How long it has been trying to move without getting anywhere.</summary>
    public float StuckTime => _nav?.StuckTime ?? 0f;

    /// <summary>Per role: the weapon (from fire_control.json), engagement range and jogging speed.</summary>
    readonly record struct RoleStats(string Weapon, float Range, float Speed);

    static readonly Dictionary<string, RoleStats> Roles = new()
    {
        ["rifleman"] = new("rifle", 75f, 5.2f),
        ["breacher"] = new("rifle", 60f, 5.0f),
        ["heavy"] = new("minigun", 85f, 4.4f),
        ["crew"] = new("rifle", 60f, 5.2f),
    };

    const float MaxHp = 100f, Gravity = 18f, Accel = 20f, WithdrawSpeed = 5.8f;
    const float ChargeDamage = 340f, ChargeFuse = 2.2f, ChargeCooldown = 7f, ChargeReach = 3.6f;

    RoleStats _stats;
    FireControl _fire;
    InvaderNavigator _nav;
    float _hp = MaxHp;
    IDamageable _target;
    IInvaderVehicle _pickup;
    float _fireTimer, _scanTimer, _chargeTimer = 1.5f, _deadTimer;
    Node3D _model;

    public override void _Ready()
    {
        _stats = Roles.TryGetValue(Role, out RoleStats s) ? s : Roles["rifleman"];
        _fire = new FireControl(World.FireData, World.FireData.Weapon(_stats.Weapon));
        CollisionLayer = Layers.Invaders;
        // Solid against hulks and crates too: they walk round them, never through.
        CollisionMask = Layers.World | Layers.Blocks | Layers.Player | Layers.Vehicles | Layers.Loot;
        FloorMaxAngle = Mathf.DegToRad(50f);
        AddChild(new CollisionShape3D
        {
            Shape = new CapsuleShape3D { Radius = InvaderNavigator.Radius, Height = 1.8f },
            Position = new Vector3(0, 0.9f, 0),
        });
        BuildModel();
        _nav = new InvaderNavigator(World, this, mustTouch: Role == "breacher");
        _scanTimer = World.Rng.RandfRange(0f, 0.5f);
        World.Model.BlockDestroyed += OnWorldChanged;
        World.Model.BlockPlaced += OnWorldChanged;
        World.ObstaclesChanged += Replan;
    }

    public override void _ExitTree()
    {
        World.Model.BlockDestroyed -= OnWorldChanged;
        World.Model.BlockPlaced -= OnWorldChanged;
        World.ObstaclesChanged -= Replan;
    }

    void OnWorldChanged(Block _) => _nav.Invalidate();
    void Replan() => _nav.Invalidate();

    // ---- Orders -----------------------------------------------------------------------

    public void Assault()
    {
        if (!IsAlive || IsGone) return;
        State = TroopState.Assaulting;
        _nav.SetGoal(World.Model.Grid.CoreSlot, null);
    }

    public void Withdraw(IInvaderVehicle pickup)
    {
        if (!IsAlive || IsGone) return;
        State = TroopState.Withdrawing;
        _pickup = pickup;
        _target = null;
        Vector3 exit = WithdrawPoint();
        // Plan back out through the base to the edge of the grid nearest the exit, then walk straight.
        _nav.SetGoal(World.Model.Grid.Clamp(World.CellAt(exit)), exit, pickup != null ? 6f : 3f);
    }

    Vector3 WithdrawPoint()
    {
        if (_pickup != null) return _pickup.PickupPoint;
        Vector3 outward = (GlobalPosition - World.BaseCentre) with { Y = 0 };
        if (outward.LengthSquared() < 1f) outward = Vector3.Forward;
        return World.BaseCentre + outward.Normalized() * ((float)World.Data.Approach.GoneDistance + 40f);
    }

    // ---- Every tick -------------------------------------------------------------------

    public override void _PhysicsProcess(double delta)
    {
        float dt = (float)delta;
        if (State == TroopState.Dead)
        {
            Topple(dt);
            return;
        }
        if (State == TroopState.Gone) return;
        if (State == TroopState.Withdrawing && Withdrawn()) return;

        float speed = State == TroopState.Withdrawing ? WithdrawSpeed : _stats.Speed;
        Vector3 desired = _nav.Steer(dt, speed);

        Vector3 v = Velocity;
        v.X = Mathf.MoveToward(v.X, desired.X, Accel * dt);
        v.Z = Mathf.MoveToward(v.Z, desired.Z, Accel * dt);
        v.Y = IsOnFloor() ? -0.5f : v.Y - Gravity * dt;
        Velocity = v;
        MoveAndSlide();

        Face(dt);
        if (State == TroopState.Assaulting) Fight(dt);
    }

    /// <summary>True once it has climbed aboard its ride or walked out of the area.</summary>
    bool Withdrawn()
    {
        if (_pickup != null && (_pickup.IsGone || _pickup.IsStranded)) Withdraw(null);
        if (_pickup != null && _nav.Arrived && _pickup.ReadyForPickup)
        {
            _pickup.Extract(this);
            Leave();
            return true;
        }
        if (_pickup == null && Flat(GlobalPosition, World.BaseCentre) > World.Data.Approach.GoneDistance)
        {
            Leave();
            return true;
        }
        return false;
    }

    // ---- Fighting ---------------------------------------------------------------------

    void Fight(float dt)
    {
        _fireTimer -= dt;
        _scanTimer -= dt;
        _chargeTimer -= dt;
        Vector3 eye = GlobalPosition + Vector3.Up * 1.55f;

        if (_scanTimer <= 0f)
        {
            _scanTimer = 0.5f;
            const uint sight = Layers.World | Layers.Blocks;
            // While breaching, only a player close by is worth turning for.
            float playerRange = BreachTarget != null ? 25f : _stats.Range;
            _target = World.NearestPlayer(eye, playerRange, sight);
            if (_target == null && BreachTarget == null) _target = World.NearestTurret(eye, _stats.Range + 15f, sight);
            if (_target == null) _fire.LostSight();
        }
        if (!CombatWorld.Usable(_target)) _target = null;

        Block breach = BreachTarget is { Alive: true } b ? b : null;
        float toBreach = breach != null ? Flat(GlobalPosition, World.BlockAim(breach)) : float.MaxValue;
        bool breachInReach = toBreach < InvaderNavigator.ShootReach;
        if (toBreach < ChargeReach && Role == "breacher" && _chargeTimer <= 0f && _target == null)
        {
            PlantCharge(breach);
            _chargeTimer = ChargeCooldown;
        }

        if (_fireTimer > 0f) return;
        if (_target != null)
        {
            Vector3 muzzle = Muzzle();
            // Lost sight since the last look: the engagement (warning shots, ramp) starts over.
            if (!World.LineOfSight(eye, _target, Layers.World | Layers.Blocks))
            {
                _fire.LostSight();
                _target = null;
                return;
            }
            _fireTimer = (float)_fire.Weapon.Interval;
            World.FireAt(_fire, muzzle, _target, Layers.InvaderShots, Faction.Vharn, Palette.TracerVharn, maxDistance: _stats.Range * 1.4f);
        }
        else if (breachInReach)
        {
            _fireTimer = (float)_fire.Weapon.Interval;
            Vector3 muzzle = Muzzle();
            float spread = (float)World.FireData.BaselineSpread(_fire.Weapon, TargetClass.Capital);
            World.Fire(muzzle, World.BlockAim(breach), _fire.Weapon, spread, Layers.Blocks | Layers.World, Faction.Vharn,
                Palette.TracerVharn, maxDistance: 12f);
        }
    }

    void PlantCharge(Block block)
    {
        Vector3 face = World.BlockAim(block);
        Vector3 at = GlobalPosition.Lerp(face, 0.75f) with { Y = 0.6f };
        var charge = new BreachCharge { World = World, Fuse = ChargeFuse, Damage = ChargeDamage };
        World.AddChild(charge);
        charge.GlobalPosition = at;
    }

    Vector3 Muzzle() => GlobalTransform * new Vector3(0.22f, 1.25f, -0.8f);

    void Face(float dt)
    {
        Vector3 look = Vector3.Zero;
        if (!CombatWorld.Usable(_target)) _target = null;
        if (State == TroopState.Assaulting && _target != null) look = _target.AimPoint - GlobalPosition;
        else if (State == TroopState.Assaulting && BreachTarget != null) look = World.BlockAim(BreachTarget) - GlobalPosition;
        else if (Velocity.LengthSquared() > 0.3f) look = Velocity;
        look.Y = 0;
        if (look.LengthSquared() < 0.01f) return;
        float want = Mathf.Atan2(-look.X, -look.Z);
        Rotation = new Vector3(0, Mathf.LerpAngle(Rotation.Y, want, Mathf.Min(1f, 10f * dt)), 0);
    }

    // ---- Damage -----------------------------------------------------------------------

    public void TakeDamage(float amount, Vector3 from, Faction by)
    {
        if (by == Faction || !IsAlive) return;
        _hp -= amount;
        if (_hp <= 0f) Die();
    }

    void Die()
    {
        _hp = 0f;
        State = TroopState.Dead;
        CollisionLayer = 0;
        CollisionMask = 0;
        Velocity = Vector3.Zero;
        _deadTimer = 0f;
    }

    void Topple(float dt)
    {
        _deadTimer += dt;
        if (_model != null) _model.Rotation = new Vector3(Mathf.Lerp(_model.Rotation.X, -1.45f, Mathf.Min(1f, dt * 6f)), 0, 0);
        if (_model != null) _model.Position = new Vector3(0, Mathf.Lerp(_model.Position.Y, 0.25f, Mathf.Min(1f, dt * 6f)), 0);
        if (_deadTimer > 30f) QueueFree();
    }

    void Leave()
    {
        State = TroopState.Gone;
        Visible = false;
        CollisionLayer = 0;
        CollisionMask = 0;
        QueueFree();
    }

    static float Flat(Vector3 a, Vector3 b) => new Vector2(a.X - b.X, a.Z - b.Z).Length();

    // ---- Looks ------------------------------------------------------------------------

    void BuildModel()
    {
        _model = new Node3D { Name = "Model" };
        AddChild(_model);
        Material armour = Palette.Mat(Palette.Oxblood, 0.6f);
        Material bone = Palette.Mat(Palette.Bone, 0.5f);
        Material dark = Palette.Mat(Palette.Black, 0.7f);
        Material metal = Palette.Mat(Palette.Gunmetal, 0.4f, 0.5f);
        bool heavy = Role == "heavy";
        float bulk = heavy ? 1.18f : 1f;

        Palette.Box(_model, new Vector3(-0.13f, 0.43f, 0), new Vector3(0.2f, 0.86f, 0.26f), dark);
        Palette.Box(_model, new Vector3(0.13f, 0.43f, 0), new Vector3(0.2f, 0.86f, 0.26f), dark);
        Palette.Box(_model, new Vector3(0, 1.15f, 0), new Vector3(0.52f * bulk, 0.62f, 0.32f * bulk), armour);
        Palette.Box(_model, new Vector3(0, 1.24f, -0.17f * bulk), new Vector3(0.42f, 0.3f, 0.05f), bone);
        Palette.Box(_model, new Vector3(-0.32f * bulk, 1.4f, 0), new Vector3(0.18f, 0.16f, 0.34f), bone);
        Palette.Box(_model, new Vector3(0.32f * bulk, 1.4f, 0), new Vector3(0.18f, 0.16f, 0.34f), bone);
        Palette.Box(_model, new Vector3(0, 1.64f, 0), new Vector3(0.3f, 0.32f, 0.32f), bone);
        Palette.Box(_model, new Vector3(0, 1.66f, -0.165f), new Vector3(0.26f, 0.07f, 0.02f), Palette.Glow(Palette.Acid, 3f));
        Palette.Box(_model, new Vector3(0, 1.82f, 0.02f), new Vector3(0.06f, 0.06f, 0.3f), armour); // crest
        Palette.Box(_model, new Vector3(0, 1.18f, 0.24f * bulk), new Vector3(0.38f, 0.42f, 0.18f), dark);
        if (Role == "breacher")
        {
            Palette.Box(_model, new Vector3(-0.1f, 1.2f, 0.36f), new Vector3(0.14f, 0.3f, 0.1f), Palette.Mat(Palette.Orange));
            Palette.Box(_model, new Vector3(0.1f, 1.2f, 0.36f), new Vector3(0.14f, 0.3f, 0.1f), Palette.Mat(Palette.Orange));
        }
        float gunLength = heavy ? 1.1f : 0.8f;
        Palette.Box(_model, new Vector3(0.22f, 1.2f, -0.3f), new Vector3(heavy ? 0.14f : 0.08f, heavy ? 0.16f : 0.1f, gunLength), metal);
    }
}

/// <summary>A placed breaching charge: blinks, then blasts the block it's stuck to.</summary>
public partial class BreachCharge : Node3D
{
    public CombatWorld World;
    public float Fuse = 2.2f, Damage = 340f;
    MeshInstance3D _light;
    float _t;

    public override void _Ready()
    {
        Palette.Box(this, Vector3.Zero, new Vector3(0.4f, 0.3f, 0.2f), Palette.Mat(Palette.Orange));
        _light = Palette.Box(this, new Vector3(0, 0.2f, 0), new Vector3(0.1f, 0.1f, 0.1f), Palette.Glow(Palette.SirenRed, 6f));
    }

    public override void _PhysicsProcess(double delta)
    {
        _t += (float)delta;
        _light.Visible = Mathf.PosMod(_t * (2f + _t * 3f), 1f) < 0.5f;
        if (_t < Fuse) return;
        World.Blast(GlobalPosition + Vector3.Up * 0.4f, 2.2f, Damage, 25f, Faction.Vharn);
        QueueFree();
    }
}
