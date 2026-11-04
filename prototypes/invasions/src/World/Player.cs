using System;
using System.Linq;
using Godot;
using Invasions.Combat;
using Invasions.Model;

namespace Invasions.World;

/// <summary>
/// First-person player: walk, sprint (Shift), jump (Space) and crouch (hold Ctrl, which really
/// shrinks the collision capsule), a placeholder rifle, manning a turret (F) and recovering spill
/// crates into storage (E). NPC hits go through <see cref="PlayerHealth"/>, which caps every hit at
/// 35% of max health and opens a short grace window below 25%.
/// </summary>
public partial class Player : CharacterBody3D, IDamageable
{
    public Camera3D Camera;
    public CombatWorld World;
    public BaseView Base;
    public PlayerHealth Health;
    public Vector3 SpawnPoint;
    public float SpawnYaw;
    /// <summary>Test hook: ignore incoming damage.</summary>
    public bool Invulnerable;

    public Faction Faction => Faction.Defenders;
    public bool IsAlive => Health.Alive;
    public Vector3 AimPoint => GlobalPosition + Vector3.Up * (Crouching ? 0.75f : 1.3f);

    public bool Crouching { get; private set; }
    public float CapsuleHeight => _capsule.Height;
    public float EyeHeight => _eye;
    public Turret Manning { get; private set; }
    /// <summary>What the player can do here, for the HUD ("E  recover spill crate").</summary>
    public string Prompt { get; private set; } = "";
    public int ShotsFired { get; private set; }
    public float RespawnIn => _respawn;

    /// <summary>Damage actually taken, after the safety rules.</summary>
    public event Action<float> Hurt;

    public const float StandHeight = 1.8f, CrouchHeight = 1.05f;
    const float StandEye = 1.62f, CrouchEye = 0.95f, Radius = 0.35f;
    const float WalkSpeed = 5f, SprintSpeed = 8.5f, CrouchSpeed = 2.4f, JumpSpeed = 5.2f;
    const float Gravity = 14f, GroundAccel = 45f, AirAccel = 8f, MouseSensitivity = 0.0025f;
    const float FireInterval = 0.13f, GunDamage = 34f, GunRange = 650f, GunSpread = 0.3f;
    const float WalkFov = 78f, TurretFov = 42f;

    CapsuleShape3D _capsule;
    CollisionShape3D _shape;
    float _eye = StandEye;
    float _yaw, _pitch;
    Vector2 _mouse;
    float _fireTimer, _respawn, _recoil;
    Node3D _gun;

    public override void _Ready()
    {
        CollisionLayer = Layers.Player;
        CollisionMask = Layers.World | Layers.Blocks | Layers.Vehicles | Layers.Loot;
        FloorMaxAngle = Mathf.DegToRad(50f);
        _capsule = new CapsuleShape3D { Radius = Radius, Height = StandHeight };
        _shape = new CollisionShape3D { Shape = _capsule, Position = new Vector3(0, StandHeight / 2, 0) };
        AddChild(_shape);
        World.Players.Add(this);
        BuildGun();
        _yaw = SpawnYaw;
        if (DisplayServer.GetName() != "headless") Input.MouseMode = Input.MouseModeEnum.Captured;
    }

    public override void _ExitTree() => World.Players.Remove(this);

    public override void _UnhandledInput(InputEvent e)
    {
        if (e is InputEventMouseMotion motion && Input.MouseMode == Input.MouseModeEnum.Captured)
            _mouse += motion.Relative;
        else if (e is InputEventMouseButton { Pressed: true } && Input.MouseMode != Input.MouseModeEnum.Captured)
            Input.MouseMode = Input.MouseModeEnum.Captured;
        else if (e.IsActionPressed(Controls.FreeMouse))
            Input.MouseMode = Input.MouseModeEnum.Visible;
    }

    public override void _PhysicsProcess(double delta)
    {
        float dt = (float)delta;
        Vector2 mouse = _mouse;
        _mouse = Vector2.Zero;
        _fireTimer -= dt;
        _recoil = Mathf.MoveToward(_recoil, 0f, dt * 0.6f);

        if (!Health.Alive)
        {
            Dead(dt);
            return;
        }

        _yaw -= mouse.X * MouseSensitivity;
        _pitch = Mathf.Clamp(_pitch - mouse.Y * MouseSensitivity, -1.5f, 1.5f);

        if (Manning != null)
        {
            Manning.Aim(_yaw - Manning.GetParentNode3D().GlobalRotation.Y, _pitch);
            if (Input.IsActionPressed(Controls.Fire)) Manning.FireManual();
            if (Input.IsActionJustPressed(Controls.ManTurret)) LeaveTurret();
            else
            {
                Camera.GlobalTransform = Manning.ViewTransform;
                Camera.Fov = TurretFov; // the turret's sight magnifies
            }
            _gun.Visible = false;
            Prompt = "F  leave turret";
            return;
        }

        UpdateCrouch(dt);
        Walk(dt);
        Shoot();
        Interact();
        PlaceCamera();
    }

    // ---- Movement ---------------------------------------------------------------------

    void UpdateCrouch(float dt)
    {
        bool want = Input.IsActionPressed(Controls.Crouch);
        if (want && !Crouching) SetCrouch(true);
        else if (!want && Crouching && HasHeadroom()) SetCrouch(false);
        _eye = Mathf.MoveToward(_eye, Crouching ? CrouchEye : StandEye, dt * 5f);
    }

    void SetCrouch(bool crouch)
    {
        Crouching = crouch;
        float h = crouch ? CrouchHeight : StandHeight;
        _capsule.Height = h;
        _shape.Position = new Vector3(0, h / 2, 0);
    }

    /// <summary>Room to stand up: the crouched capsule could move up by the difference without hitting anything.</summary>
    bool HasHeadroom() => !TestMove(GlobalTransform, Vector3.Up * (StandHeight - CrouchHeight + 0.05f));

    void Walk(float dt)
    {
        Vector3 v = Velocity;
        bool onFloor = IsOnFloor();
        if (!onFloor) v.Y -= Gravity * dt;

        Vector2 input = Input.GetVector(Controls.Left, Controls.Right, Controls.Forward, Controls.Back);
        float speed = Crouching ? CrouchSpeed : Input.IsActionPressed(Controls.Sprint) ? SprintSpeed : WalkSpeed;
        Vector3 wish = new Basis(Vector3.Up, _yaw) * new Vector3(input.X, 0, input.Y) * speed;
        float accel = onFloor ? GroundAccel : AirAccel;
        v.X = Mathf.MoveToward(v.X, wish.X, accel * dt);
        v.Z = Mathf.MoveToward(v.Z, wish.Z, accel * dt);
        if (onFloor && !Crouching && Input.IsActionPressed(Controls.Jump)) v.Y = JumpSpeed;

        Velocity = v;
        MoveAndSlide();
    }

    Basis LookBasis => new Basis(Vector3.Up, _yaw) * new Basis(Vector3.Right, _pitch + _recoil);

    void PlaceCamera()
    {
        Camera.Fov = WalkFov;
        Camera.GlobalTransform = new Transform3D(LookBasis, GlobalPosition + Vector3.Up * _eye);
        _gun.Visible = true;
    }

    // ---- Gun --------------------------------------------------------------------------

    void Shoot()
    {
        if (!Input.IsActionPressed(Controls.Fire) || _fireTimer > 0f) return;
        _fireTimer = FireInterval;
        ShotsFired++;
        Vector3 eye = GlobalPosition + Vector3.Up * _eye;
        Vector3 forward = -LookBasis.Z;
        // The round flies from the eye (so it goes where the crosshair is); the tracer starts at the barrel.
        GodotObject hit = World.Shoot(eye, forward, GunRange, GunSpread, Layers.DefenderShots, Faction.Defenders, GunDamage,
            Palette.TracerSettler);
        Fx.Flash(_gun.GlobalTransform * new Vector3(0, 0.05f, -0.75f), Palette.TracerSettler, 3f, 1.5f);
        _recoil += 0.012f;
    }

    void BuildGun()
    {
        _gun = new Node3D { Name = "Rifle", Position = new Vector3(0.17f, -0.16f, -0.32f), Scale = Vector3.One * 0.45f };
        Camera.AddChild(_gun);
        Palette.Box(_gun, new Vector3(0, 0, -0.1f), new Vector3(0.09f, 0.13f, 0.62f), Palette.Mat(Palette.Dark, 0.4f));
        Palette.Box(_gun, new Vector3(0, 0.02f, -0.48f), new Vector3(0.05f, 0.05f, 0.4f), Palette.Mat(Palette.Gunmetal, 0.3f, 0.6f));
        Palette.Box(_gun, new Vector3(0, -0.02f, 0.26f), new Vector3(0.08f, 0.12f, 0.22f), Palette.Mat(Palette.Cream, 0.6f));
        Palette.Box(_gun, new Vector3(0, -0.12f, 0.02f), new Vector3(0.07f, 0.16f, 0.08f), Palette.Mat(Palette.Dark, 0.5f));
        Palette.Box(_gun, new Vector3(0.047f, 0.01f, -0.1f), new Vector3(0.01f, 0.05f, 0.5f), Palette.Mat(Palette.Orange));
        Palette.Box(_gun, new Vector3(0, 0.1f, -0.08f), new Vector3(0.04f, 0.05f, 0.14f), Palette.Mat(Palette.Teal, 0.4f));
        foreach (MeshInstance3D part in _gun.GetChildren().OfType<MeshInstance3D>())
            part.CastShadow = GeometryInstance3D.ShadowCastingSetting.Off;
    }

    // ---- Interaction ------------------------------------------------------------------

    void Interact()
    {
        Prompt = "";
        SpillCrateNode crate = Base.CrateNodes.Where(c => IsInstanceValid(c) && c.GlobalPosition.DistanceTo(GlobalPosition) < 2.8f)
            .OrderBy(c => c.GlobalPosition.DistanceTo(GlobalPosition)).FirstOrDefault();
        Turret turret = Base.Turrets.Where(t => IsInstanceValid(t) && Flat(t.GlobalPosition, GlobalPosition) < 3.2f).FirstOrDefault();

        if (crate != null)
        {
            Prompt = $"E  recover spill crate ({crate.Crate.Total} items) into storage";
            if (Input.IsActionJustPressed(Controls.Interact))
            {
                int moved = World.Model.RecoverCrate(crate.Crate);
                if (moved == 0) Prompt = "Storage is full: the crate stays put, nothing is lost";
            }
        }
        else if (turret != null)
        {
            Prompt = turret.CanMan ? "F  man turret" : "Turret has no power";
            if (turret.CanMan && Input.IsActionJustPressed(Controls.ManTurret))
            {
                ManTurret(turret);
                _yaw = turret.GetParentNode3D().GlobalRotation.Y + turret.Yaw;
                _pitch = turret.Pitch;
            }
        }
    }

    /// <summary>Takes the controls of a turret (key F does this when standing by one).</summary>
    public void ManTurret(Turret turret)
    {
        if (turret == null || !turret.CanMan) return;
        Manning = turret;
        turret.Man(this);
    }

    public void LeaveTurret()
    {
        if (Manning == null) return;
        Manning.Release();
        Manning = null;
        Camera.ResetPhysicsInterpolation();
    }

    static float Flat(Vector3 a, Vector3 b) => new Vector2(a.X - b.X, a.Z - b.Z).Length();

    // ---- Damage -----------------------------------------------------------------------

    public void TakeDamage(float amount, Vector3 from, Faction by)
    {
        if (by == Faction || Invulnerable || !Health.Alive) return;
        float taken = (float)Health.ApplyNpcHit(amount, World.Time);
        Hurt?.Invoke(taken);
        if (!Health.Alive)
        {
            LeaveTurret();
            _respawn = 4f;
        }
    }

    void Dead(float dt)
    {
        _respawn -= dt;
        _eye = Mathf.MoveToward(_eye, 0.3f, dt * 2f);
        PlaceCamera();
        _gun.Visible = false;
        Prompt = $"Down. Back on your feet in {Mathf.CeilToInt(Mathf.Max(0f, _respawn))} s";
        if (_respawn > 0f) return;
        Health.Respawn();
        Teleport(SpawnPoint, SpawnYaw, 0f);
    }

    // ---- Test and screenshot hooks ----------------------------------------------------

    public void Teleport(Vector3 position, float yaw, float pitch)
    {
        LeaveTurret();
        GlobalPosition = position;
        Velocity = Vector3.Zero;
        _yaw = yaw;
        _pitch = pitch;
        _eye = Crouching ? CrouchEye : StandEye;
        ResetPhysicsInterpolation();
        PlaceCamera();
        Camera.ResetPhysicsInterpolation();
    }

    public void SetLook(float yaw, float pitch)
    {
        _yaw = yaw;
        _pitch = pitch;
    }

    /// <summary>Turns to look at a world point.</summary>
    public void LookAtPoint(Vector3 target)
    {
        Vector3 d = target - (GlobalPosition + Vector3.Up * _eye);
        _yaw = Mathf.Atan2(-d.X, -d.Z);
        _pitch = Mathf.Atan2(d.Y, new Vector2(d.X, d.Z).Length());
    }
}
