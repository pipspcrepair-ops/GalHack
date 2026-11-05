using Godot;
using Invasions.Director;
using Invasions.Model;
using Invasions.World;

namespace Invasions.Combat;

/// <summary>
/// PLACEHOLDER invader on foot, to be replaced by the boarding prototype's soldier AI. One class for
/// every faction: its role (weapon, reach, pace, toughness, shield, how it breaks walls) and its
/// faction's tactics come from factions.json, and its looks from <see cref="Looks"/>.
///
/// Movement is an <see cref="InvaderNavigator"/>: the breach planner's route to its target, smoothed
/// only where its whole body fits, with local avoidance and stuck recovery. When a block is in the way
/// it holds at a safe spot and breaks it, then carries on; when any block falls it re-plans, so a squad
/// converges on fresh breaches. How it breaks a block:
/// - everyone shoots it within rifle reach (except a breaching team's riflemen, who stack up and hold
///   fire for a while to let the charges do it);
/// - "charge" roles walk up and plant breaching charges, "cut" roles burn through with a beam, "burst"
///   roles burst against it.
/// It shoots players and turrets it can see on the way, always through fire control (warning shots,
/// the accuracy ramp, projectiles with travel time). Raiders go for the storage containers first and
/// the core after; everyone else goes for the core. Wrecked containers spill into crates, and nobody
/// takes anything. Withdrawing, it plans its way back out and walks to its ride (or out of the area).
/// </summary>
public partial class PlaceholderSoldier : CharacterBody3D, IDamageable, IInvaderTroop
{
    public CombatWorld World;
    public string Faction { get; init; } = "vharn";
    public string Role { get; init; } = "rifleman";
    public TroopState State { get; private set; } = TroopState.Assaulting;
    public IInvaderVehicle Ride { get; set; }

    public Team Team => Team.Invaders;
    public TargetClass Class => TargetClass.OnFoot;
    public bool IsAlive => _hp > 0f;
    public bool IsGone => State == TroopState.Gone;
    public Node3D Body => this;
    public Vector3 AimPoint => GlobalPosition + Vector3.Up * 1.3f;

    /// <summary>The block it is currently breaking through, if any (for tests and debugging).</summary>
    public Block BreachTarget => _nav?.BreachTarget;
    /// <summary>The block it was sent for: the core, or a container for raiders.</summary>
    public Block Goal { get; private set; }

    /// <summary>At an engagement position: holding in front of the block in the way, or within rifle reach of it.</summary>
    public bool AtBreach => BreachTarget is { Alive: true } b &&
                            (_nav.Holding || Flat(GlobalPosition, World.BlockAim(b)) < InvaderNavigator.ShootReach);
    public bool InCoreRoom => World.Model.InCoreRoom(new GroundPos(GlobalPosition.X, GlobalPosition.Z));
    /// <summary>How long it has been trying to move without getting anywhere.</summary>
    public float StuckTime => _nav?.StuckTime ?? 0f;
    /// <summary>For tests and debugging: what it is doing and where its route stands.</summary>
    public string Describe() => $"{Faction} {Role} {State} goal {Goal?.Kind}; {_nav?.Describe()}";

    public RoleData Stats => _role;
    public float Health => _hp;
    public float ShieldLeft => _shield;
    // Tactics at work, for tests and tuning.
    public int WallShots { get; private set; }
    public int ChargesPlanted { get; private set; }
    public float CutDamage { get; private set; }
    public bool Burst { get; private set; }
    /// <summary>Longest it has held fire on a wall waiting for a breaching team's charges.</summary>
    public float LongestStackUp { get; private set; }

    const float Gravity = 18f, Accel = 20f, WithdrawSpeed = 5.8f;
    const float ChargeFuse = 2.2f, ChargeCooldown = 7f, ChargeReach = 3.6f, BurstPersonDamage = 25f;

    FactionData _faction;
    RoleData _role;
    Livery _livery;
    bool _breacherRole;
    FireControl _fire;
    InvaderNavigator _nav;
    float _hp, _shield, _sinceHit = 99f;
    IDamageable _target;
    IInvaderVehicle _pickup;
    float _fireTimer, _scanTimer, _chargeTimer = 1.5f, _deadTimer, _stackUp;
    Block _stackingAt;
    Node3D _model, _hover;
    Vector3 _muzzle;
    MeshInstance3D _beam, _beamTip, _bubble;
    float _bubbleTime, _bob;

    public override void _Ready()
    {
        _faction = World.Data.Faction(Faction);
        _role = _faction.Role(Role);
        _livery = World.LiveryFor(Faction);
        _breacherRole = !string.IsNullOrEmpty(_role.Breach);
        _hp = (float)_role.Hp;
        _shield = (float)_role.Shield;
        _fire = new FireControl(World.FireData, World.FireData.Weapon(_role.Weapon));
        CollisionLayer = Layers.Invaders;
        // Solid against hulks and crates too: they walk round them, never through.
        CollisionMask = Layers.World | Layers.Blocks | Layers.Player | Layers.Vehicles | Layers.Loot;
        FloorMaxAngle = Mathf.DegToRad(50f);
        AddChild(new CollisionShape3D
        {
            Shape = new CapsuleShape3D { Radius = InvaderNavigator.Radius, Height = 1.8f },
            Position = new Vector3(0, 0.9f, 0),
        });
        _model = new Node3D { Name = "Model" };
        AddChild(_model);
        _muzzle = Looks.Trooper(_model, Faction, string.IsNullOrEmpty(_role.Look) ? Role : _role.Look, _livery);
        _hover = _model.GetNodeOrNull<Node3D>("Hover");
        _bob = World.Rng.RandfRange(0f, Mathf.Tau);
        _nav = new InvaderNavigator(World, this, mustTouch: _breacherRole, World.PlannerFor(Faction));
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

    void OnWorldChanged(Block b)
    {
        _nav.Invalidate();
        // Its target fell (a container smashed open): on to the next one.
        if (State == TroopState.Assaulting && b == Goal) Assault();
    }

    void Replan() => _nav.Invalidate();

    // ---- Orders -----------------------------------------------------------------------

    public void Assault()
    {
        if (!IsAlive || IsGone) return;
        State = TroopState.Assaulting;
        Goal = PickGoal();
        _nav.SetGoal(Goal?.Cell ?? World.Model.Grid.CoreSlot, null);
    }

    /// <summary>The core, or for raiders the nearest standing storage container first.</summary>
    Block PickGoal()
    {
        BaseGrid grid = World.Model.Grid;
        if (_faction.Tactics.Targets == "storage")
        {
            Block nearest = null;
            float best = float.MaxValue;
            foreach (Block b in grid.Alive)
            {
                if (b.Kind != BlockKind.Container) continue;
                float d = Flat(GlobalPosition, World.CellCentre(b.Cell));
                if (d < best) { best = d; nearest = b; }
            }
            if (nearest != null) return nearest;
        }
        return grid.Core;
    }

    public void Withdraw(IInvaderVehicle pickup)
    {
        if (!IsAlive || IsGone) return;
        State = TroopState.Withdrawing;
        _pickup = pickup;
        _target = null;
        SetBeam(null);
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
        RechargeShield(dt);
        if (State == TroopState.Withdrawing && Withdrawn()) return;

        float pace = (float)_role.Speed;
        float speed = State == TroopState.Withdrawing ? Mathf.Max(WithdrawSpeed, pace) : pace;
        Vector3 desired = _nav.Steer(dt, speed);

        Vector3 v = Velocity;
        v.X = Mathf.MoveToward(v.X, desired.X, Accel * dt);
        v.Z = Mathf.MoveToward(v.Z, desired.Z, Accel * dt);
        v.Y = IsOnFloor() ? -0.5f : v.Y - Gravity * dt;
        Velocity = v;
        MoveAndSlide();

        Face(dt);
        if (State == TroopState.Assaulting) Fight(dt);
        Animate(dt);
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
            float range = (float)_role.Range;
            float playerRange = BreachTarget != null ? Mathf.Min(25f, range) : range;
            _target = World.NearestPlayer(eye, playerRange, sight);
            if (_target == null && BreachTarget == null) _target = World.NearestTurret(eye, range + 15f, sight);
            if (_target == null) _fire.LostSight();
        }
        if (!CombatWorld.Usable(_target)) _target = null;

        Block breach = BreachTarget is { Alive: true } b ? b : null;
        float toBreach = breach != null ? Flat(GlobalPosition, World.BlockAim(breach)) : float.MaxValue;
        bool breachInReach = toBreach < InvaderNavigator.ShootReach;
        bool atTheFace = toBreach < ChargeReach && _target == null;

        // The breaching roles' own way through.
        bool cutting = false;
        switch (_role.Breach)
        {
            case "charge" when atTheFace && _chargeTimer <= 0f:
                PlantCharge(breach);
                _chargeTimer = ChargeCooldown;
                break;
            case "cut" when atTheFace:
                cutting = true;
                float dealt = (float)_role.BreachDamage * dt;
                CutDamage += dealt;
                World.Model.Damage(breach, dealt);
                break;
            case "burst" when toBreach < ChargeReach:
                BurstAgainst(breach);
                return;
        }
        SetBeam(cutting ? breach : null);
        if (cutting) return;

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
            World.FireAt(_fire, muzzle, _target, Layers.InvaderShots, Team.Invaders, _livery.Tracer, maxDistance: (float)_role.Range * 1.4f);
        }
        else if (breachInReach && !HoldingFireForCharges(breach, dt))
        {
            _fireTimer = (float)_fire.Weapon.Interval;
            WallShots++;
            Vector3 muzzle = Muzzle();
            float spread = (float)World.FireData.BaselineSpread(_fire.Weapon, TargetClass.Capital);
            World.Fire(muzzle, World.BlockAim(breach), _fire.Weapon, spread, Layers.Blocks | Layers.World, Team.Invaders,
                _livery.Tracer, maxDistance: 12f);
        }
    }

    /// <summary>
    /// A breaching team: at a wall, everyone but the breachers stacks up and holds fire on it for a
    /// while, so the shaped charges open it. After that they shoot it too, so a team that lost its
    /// breachers still gets through.
    /// </summary>
    bool HoldingFireForCharges(Block breach, float dt)
    {
        float wait = (float)_faction.Tactics.StackUpSeconds;
        if (wait <= 0f || _breacherRole || breach.Kind == BlockKind.Core) return false;
        if (breach != _stackingAt)
        {
            _stackingAt = breach;
            _stackUp = 0f;
        }
        // Called once per shot it would have fired; count the time since the last call.
        _stackUp += (float)_fire.Weapon.Interval;
        LongestStackUp = Mathf.Max(LongestStackUp, _stackUp);
        if (_stackUp >= wait) return false;
        _fireTimer = (float)_fire.Weapon.Interval;
        return true;
    }

    void PlantCharge(Block block)
    {
        ChargesPlanted++;
        Vector3 face = World.BlockAim(block);
        Vector3 at = GlobalPosition.Lerp(face, 0.75f) with { Y = 0.6f };
        var charge = new BreachCharge { World = World, Fuse = ChargeFuse, Damage = (float)_role.BreachDamage };
        World.AddChild(charge);
        charge.GlobalPosition = at;
    }

    /// <summary>A burster bursts against the block in its way: a breaching blast, and it is gone.</summary>
    void BurstAgainst(Block block)
    {
        Burst = true;
        Vector3 at = GlobalPosition.Lerp(World.BlockAim(block), 0.6f) with { Y = 0.9f };
        World.Blast(at, 2.2f, (float)_role.BreachDamage, BurstPersonDamage, Team.Invaders);
        Fx.Debris(at, _livery.Trim, 12, 8f, 0.3f);
        Die();
        _model.Visible = false;
    }

    Vector3 Muzzle() => GlobalTransform * _muzzle;

    /// <summary>The cutting beam: on while a cutter is burning through a block.</summary>
    void SetBeam(Block block)
    {
        if (block == null)
        {
            if (_beam != null) _beam.Visible = _beamTip.Visible = false;
            return;
        }
        _beam ??= MakeBeam();
        Vector3 from = Muzzle(), to = World.BlockAim(block);
        Vector3 d = to - from;
        float length = Mathf.Max(0.1f, d.Length() - 0.9f); // stop at the block's face
        Vector3 dir = d.Normalized();
        Vector3 up = Mathf.Abs(dir.Y) > 0.98f ? Vector3.Right : Vector3.Up;
        Basis basis = Basis.LookingAt(dir, up);
        basis.Z *= length; // the unit-long beam mesh stretched along its own axis
        _beam.GlobalTransform = new Transform3D(basis, from + dir * length * 0.5f);
        _beamTip.GlobalPosition = from + dir * length; // the white-hot spot where it bites into the block
        _beam.Visible = _beamTip.Visible = true;
        if (Engine.GetPhysicsFrames() % 6 == 0) Fx.Spark(from + dir * length, _livery.Glow, 0.18f);
    }

    MeshInstance3D MakeBeam()
    {
        var beam = new MeshInstance3D
        {
            Name = "CutBeam",
            Mesh = new BoxMesh { Size = new Vector3(0.06f, 0.06f, 1f) },
            MaterialOverride = Palette.Glow(_livery.Glow, 6f),
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            TopLevel = true,
        };
        AddChild(beam);
        _beamTip = new MeshInstance3D
        {
            Name = "CutTip",
            Mesh = new SphereMesh { Radius = 0.16f, Height = 0.32f, RadialSegments = 8, Rings = 4 },
            MaterialOverride = Palette.Glow(Colors.White.Lerp(_livery.Glow, 0.4f), 8f),
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            TopLevel = true,
        };
        AddChild(_beamTip);
        return beam;
    }

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

    void Animate(float dt)
    {
        if (_hover != null)
        {
            _bob += dt * 2.4f;
            _hover.Position = new Vector3(0, 1.25f + 0.06f * Mathf.Sin(_bob), 0);
        }
        if (_bubble != null && _bubble.Visible)
        {
            _bubbleTime -= dt;
            if (_bubbleTime <= 0f) _bubble.Visible = false;
        }
    }

    // ---- Damage -----------------------------------------------------------------------

    /// <summary>
    /// Full damage, never reduced or capped: a shield soaks what it can hold and the rest goes straight
    /// through to health in the same hit.
    /// </summary>
    public void TakeDamage(float amount, Vector3 from, Team by)
    {
        if (by == Team || !IsAlive) return;
        _sinceHit = 0f;
        if (_shield > 0f)
        {
            float soaked = Mathf.Min(_shield, amount);
            _shield -= soaked;
            amount -= soaked;
            FlashShield();
        }
        if (amount <= 0f) return;
        _hp -= amount;
        if (_hp <= 0f) Die();
    }

    void RechargeShield(float dt)
    {
        if (_role.Shield <= 0) return;
        _sinceHit += dt;
        if (_sinceHit >= _faction.Tactics.ShieldRechargeDelay)
            _shield = Mathf.Min((float)_role.Shield, _shield + (float)_faction.Tactics.ShieldRechargePerSecond * dt);
    }

    void FlashShield()
    {
        if (_bubble == null)
        {
            _bubble = new MeshInstance3D
            {
                Name = "Shield",
                Mesh = new SphereMesh { Radius = 0.62f, Height = 2.1f, RadialSegments = 16, Rings = 8 },
                MaterialOverride = ShieldMaterial(_livery.Glow),
                CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
                Position = new Vector3(0, 1.0f, 0),
            };
            AddChild(_bubble);
        }
        _bubble.Visible = true;
        _bubbleTime = 0.18f;
    }

    static readonly System.Collections.Generic.Dictionary<Color, StandardMaterial3D> ShieldMaterials = new();

    static StandardMaterial3D ShieldMaterial(Color glow)
    {
        if (ShieldMaterials.TryGetValue(glow, out StandardMaterial3D m)) return m;
        return ShieldMaterials[glow] = new StandardMaterial3D
        {
            AlbedoColor = new Color(glow, 0.28f),
            EmissionEnabled = true,
            Emission = glow,
            EmissionEnergyMultiplier = 1.5f,
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            CullMode = BaseMaterial3D.CullModeEnum.Disabled,
        };
    }

    void Die()
    {
        _hp = 0f;
        _shield = 0f;
        State = TroopState.Dead;
        CollisionLayer = 0;
        CollisionMask = 0;
        Velocity = Vector3.Zero;
        _deadTimer = 0f;
        SetBeam(null);
        if (_bubble != null) _bubble.Visible = false;
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
        World.Blast(GlobalPosition + Vector3.Up * 0.4f, 2.2f, Damage, 25f, Team.Invaders);
        QueueFree();
    }
}
