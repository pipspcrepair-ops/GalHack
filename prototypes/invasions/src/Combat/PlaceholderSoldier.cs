using System.Collections.Generic;
using Godot;
using Invasions.Director;
using Invasions.Model;
using Invasions.World;

namespace Invasions.Combat;

/// <summary>
/// PLACEHOLDER Vharn foot soldier, to be replaced by the boarding prototype's soldier AI.
///
/// Follows the breach planner's route to the core, cell by cell. When the next cell holds a block
/// it stops and breaks it (breachers plant charges, everyone shoots it), then carries on; when any
/// block falls it re-plans, so a squad converges on fresh breaches. It shoots players and turrets it
/// can see on the way. Withdrawing, it plans its way back out and walks to its ride (or out of the area).
/// </summary>
public partial class PlaceholderSoldier : CharacterBody3D, IDamageable, IInvaderTroop
{
    public CombatWorld World;
    public string Role { get; init; } = "rifleman";
    public TroopState State { get; private set; } = TroopState.Assaulting;
    public IInvaderVehicle Ride { get; set; }

    public Faction Faction => Faction.Vharn;
    public bool IsAlive => _hp > 0f;
    public bool IsGone => State == TroopState.Gone;
    public Node3D Body => this;
    public Vector3 AimPoint => GlobalPosition + Vector3.Up * 1.3f;

    /// <summary>The block it is currently breaking through, if any (for tests and debugging).</summary>
    public Block BreachTarget => _breachTarget;

    /// <summary>Close enough to the block in the way to be hitting it.</summary>
    public bool AtBreach => _breachTarget is { Alive: true } && Flat(GlobalPosition, World.BlockAim(_breachTarget)) < 3.6f;
    public bool InCoreRoom => World.Model.InCoreRoom(new GroundPos(GlobalPosition.X, GlobalPosition.Z));

    readonly struct RoleStats
    {
        public readonly float FireInterval, PlayerDamage, BlockDamage, Spread, Range, Speed;
        public RoleStats(float interval, float player, float block, float spread, float range, float speed)
        {
            FireInterval = interval; PlayerDamage = player; BlockDamage = block; Spread = spread; Range = range; Speed = speed;
        }
    }

    static readonly Dictionary<string, RoleStats> Roles = new()
    {
        ["rifleman"] = new(0.45f, 9f, 16f, 3.5f, 75f, 5.2f),
        ["breacher"] = new(0.6f, 7f, 12f, 4f, 60f, 5.0f),
        ["heavy"] = new(0.16f, 6f, 10f, 5.5f, 85f, 4.4f),
        ["crew"] = new(0.6f, 6f, 8f, 5f, 60f, 5.2f),
    };

    const float MaxHp = 100f, Gravity = 18f, Accel = 20f, WithdrawSpeed = 5.8f;
    const float ChargeDamage = 340f, ChargeFuse = 2.2f, ChargeCooldown = 7f;

    RoleStats _stats;
    float _hp = MaxHp;
    AssaultPlan _plan;
    int _step;
    bool _needsPlan = true;
    Cell _goal;
    Vector3? _finalPoint;
    Block _breachTarget;
    IDamageable _target;
    IInvaderVehicle _pickup;
    float _fireTimer, _scanTimer, _chargeTimer = 1.5f, _stuckTimer, _deadTimer;
    Vector3 _progressMark;
    Vector3 _jitter;
    Node3D _model;

    public override void _Ready()
    {
        _stats = Roles.TryGetValue(Role, out RoleStats s) ? s : Roles["rifleman"];
        CollisionLayer = Layers.Invaders;
        CollisionMask = Layers.World | Layers.Blocks | Layers.Player;
        FloorMaxAngle = Mathf.DegToRad(50f);
        AddChild(new CollisionShape3D { Shape = new CapsuleShape3D { Radius = 0.34f, Height = 1.8f }, Position = new Vector3(0, 0.9f, 0) });
        BuildModel();
        _jitter = new Vector3(World.Rng.RandfRange(-0.45f, 0.45f), 0f, World.Rng.RandfRange(-0.45f, 0.45f));
        _scanTimer = World.Rng.RandfRange(0f, 0.5f);
        _progressMark = GlobalPosition;
        World.Model.BlockDestroyed += OnBlockDestroyed;
        World.Model.BlockPlaced += OnBlockDestroyed;
    }

    public override void _ExitTree()
    {
        World.Model.BlockDestroyed -= OnBlockDestroyed;
        World.Model.BlockPlaced -= OnBlockDestroyed;
    }

    void OnBlockDestroyed(Block _) => _needsPlan = true;

    // ---- Orders -----------------------------------------------------------------------

    public void Assault()
    {
        if (!IsAlive || IsGone) return;
        State = TroopState.Assaulting;
        _goal = World.Model.Grid.CoreSlot;
        _finalPoint = null;
        _needsPlan = true;
    }

    public void Withdraw(IInvaderVehicle pickup)
    {
        if (!IsAlive || IsGone) return;
        State = TroopState.Withdrawing;
        _pickup = pickup;
        _target = null;
        _breachTarget = null;
        Vector3 exit = WithdrawPoint();
        // Plan back out through the base to the edge of the grid nearest the exit, then walk straight.
        _goal = World.Model.Grid.Clamp(World.CellAt(exit));
        _finalPoint = exit;
        _needsPlan = true;
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

        Vector3? goal = State == TroopState.Withdrawing ? WithdrawStep() : AssaultStep();
        float speed = State == TroopState.Withdrawing ? WithdrawSpeed : _stats.Speed;

        Vector3 desired = Vector3.Zero;
        if (goal is Vector3 g)
        {
            Vector3 to = (g - GlobalPosition) with { Y = 0 };
            float distance = to.Length();
            if (distance > 0.15f) desired = to / distance * speed * Mathf.Clamp(distance / 1.2f, 0.25f, 1f);
        }
        desired += Separation() * 2.2f;

        Vector3 v = Velocity;
        v.X = Mathf.MoveToward(v.X, desired.X, Accel * dt);
        v.Z = Mathf.MoveToward(v.Z, desired.Z, Accel * dt);
        v.Y = IsOnFloor() ? -0.5f : v.Y - Gravity * dt;
        Velocity = v;
        MoveAndSlide();

        Face(dt);
        if (State == TroopState.Assaulting) Fight(dt);
        CheckStuck(dt, goal);
    }

    Vector3? AssaultStep()
    {
        BaseGrid grid = World.Model.Grid;
        if (_needsPlan) Replan();
        if (_plan is not { Found: true }) return null;

        while (_step < _plan.Cells.Count)
        {
            Cell c = _plan.Cells[_step];
            if (grid.At(c) is { Alive: true } blocker)
            {
                // Something stands in the way: hold at the last open cell and break it.
                _breachTarget = blocker;
                return _step > 0 ? World.CellCentre(_plan.Cells[_step - 1]) + _jitter * 0.5f : GlobalPosition;
            }
            _breachTarget = null;
            Vector3 point = World.CellCentre(c) + _jitter;
            bool last = _step == _plan.Cells.Count - 1;
            if (!last && Flat(GlobalPosition, point) < 0.8f)
            {
                _step++;
                continue;
            }
            if (last)
            {
                // At the core: hit it until it falls.
                _breachTarget = grid.Core;
            }
            return point;
        }
        return null;
    }

    Vector3? WithdrawStep()
    {
        BaseGrid grid = World.Model.Grid;
        if (_pickup != null && (_pickup.IsGone || _pickup.IsStranded)) Withdraw(null);
        Vector3 exit = WithdrawPoint();
        _finalPoint = exit;

        // Inside the grid, follow a plan out (through the holes we came in by); outside it, walk straight.
        bool insideGrid = grid.InBounds(World.CellAt(GlobalPosition)) && Flat(GlobalPosition, World.BaseCentre) < (grid.Width * grid.CellSize * 0.5);
        if (insideGrid && Flat(GlobalPosition, exit) > 8f)
        {
            if (_needsPlan) Replan();
            if (_plan is { Found: true } && _step < _plan.Cells.Count)
            {
                Cell c = _plan.Cells[_step];
                if (grid.At(c) is { Alive: true }) { _needsPlan = true; return GlobalPosition; }
                Vector3 point = World.CellCentre(c) + _jitter;
                if (Flat(GlobalPosition, point) < 0.8f) _step++;
                return point;
            }
        }

        if (_pickup != null)
        {
            if (Flat(GlobalPosition, exit) < 6f && _pickup.ReadyForPickup)
            {
                _pickup.Extract(this);
                Leave();
                return null;
            }
            return exit;
        }
        if (Flat(GlobalPosition, World.BaseCentre) > World.Data.Approach.GoneDistance)
        {
            Leave();
            return null;
        }
        return exit;
    }

    void Replan()
    {
        _needsPlan = false;
        BaseGrid grid = World.Model.Grid;
        _plan = World.Planner.Plan(grid, grid.Clamp(World.CellAt(GlobalPosition)), _goal);
        _step = 0;
        // Skip cells already behind us: start from the plan cell nearest to where we stand.
        if (_plan is { Found: true })
        {
            float best = float.MaxValue;
            for (int i = 0; i < Mathf.Min(4, _plan.Cells.Count); i++)
            {
                float d = Flat(GlobalPosition, World.CellCentre(_plan.Cells[i]));
                if (d < best) { best = d; _step = i; }
            }
        }
    }

    void CheckStuck(float dt, Vector3? goal)
    {
        if (goal == null || _breachTarget != null || Flat(GlobalPosition, _progressMark) > 0.6f)
        {
            _progressMark = GlobalPosition;
            _stuckTimer = 0f;
            return;
        }
        _stuckTimer += dt;
        if (_stuckTimer > 3f)
        {
            _stuckTimer = 0f;
            _jitter = new Vector3(World.Rng.RandfRange(-0.6f, 0.6f), 0f, World.Rng.RandfRange(-0.6f, 0.6f));
            _needsPlan = true;
        }
    }

    Vector3 Separation()
    {
        Vector3 push = Vector3.Zero;
        foreach (IInvaderTroop other in World.Troops)
        {
            if (other == this || !other.IsAlive || other.IsGone) continue;
            Vector3 away = (GlobalPosition - other.Body.GlobalPosition) with { Y = 0 };
            float d = away.Length();
            if (d < 1.1f && d > 0.001f) push += away / d * (1.1f - d);
        }
        return push;
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
            float playerRange = _breachTarget != null ? 25f : _stats.Range;
            _target = World.NearestPlayer(eye, playerRange, sight);
            if (_target == null && _breachTarget == null) _target = World.NearestTurret(eye, _stats.Range + 15f, sight);
        }
        if (!CombatWorld.Usable(_target)) _target = null;

        bool breachInReach = _breachTarget is { Alive: true } && Flat(GlobalPosition, World.BlockAim(_breachTarget)) < 3.6f;
        if (breachInReach && Role == "breacher" && _chargeTimer <= 0f && _target == null)
        {
            PlantCharge(_breachTarget);
            _chargeTimer = ChargeCooldown;
        }

        if (_fireTimer > 0f) return;
        if (_target != null)
        {
            _fireTimer = _stats.FireInterval;
            Vector3 muzzle = Muzzle();
            World.Shoot(muzzle, _target.AimPoint - muzzle, _stats.Range * 1.3f, _stats.Spread, Layers.InvaderShots, Faction.Vharn,
                _stats.PlayerDamage, Palette.TracerVharn);
        }
        else if (breachInReach)
        {
            _fireTimer = _stats.FireInterval;
            Vector3 muzzle = Muzzle();
            World.Shoot(muzzle, World.BlockAim(_breachTarget) - muzzle, 8f, 2f, Layers.Blocks | Layers.World, Faction.Vharn,
                _stats.BlockDamage, Palette.TracerVharn);
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
        else if (State == TroopState.Assaulting && _breachTarget != null) look = World.BlockAim(_breachTarget) - GlobalPosition;
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
