using Godot;
using SurvivalVitals.Model;

namespace SurvivalVitals;

/// <summary>
/// A first-person player on foot: walk, run (R toggles), sprint (Shift), jump (Space), crouch
/// (hold Ctrl) and a suit jetpack (Space in the air). Speeds and stamina gating come from the
/// survival model, so the body and the vitals always agree on what pace you are going at.
/// </summary>
public partial class Player : CharacterBody3D
{
    public Main Main;
    public Camera3D Camera { get; private set; }

    /// <summary>R toggles a running pace; walking is the default, so moving costs no extra food.</summary>
    public bool RunToggled;

    /// <summary>False while dead or in a menu: no movement and no mining.</summary>
    public bool Controllable = true;

    public Pace Pace { get; private set; } = Pace.Idle;
    public bool Crouching { get; private set; }

    /// <summary>Downward speed while airborne, for the fall warning; 0 on the ground.</summary>
    public float FallSpeed { get; private set; }

    public float JetpackFuel { get; private set; }

    const float GroundAccel = 45f, AirAccel = 8f;
    const float StandHeight = 1.8f, CrouchHeight = 1.1f, HeadHeight = 1.62f, CrouchHeadHeight = 0.95f;
    const float MouseSensitivity = 0.0025f;

    float _yaw, _pitch, _eye = HeadHeight;
    Vector2 _mouse;
    CollisionShape3D _capsule;
    bool _wasOnFloor = true;
    float _landingSpeed;

    MovementRules Moves => Main.Sim.Data.Vitals.Movement;

    public override void _Ready()
    {
        CollisionLayer = 0;
        CollisionMask = Layers.World;
        FloorMaxAngle = Mathf.DegToRad(50f);
        FloorSnapLength = 0.4f;
        _capsule = new CollisionShape3D
        {
            Name = "Capsule",
            Shape = new CapsuleShape3D { Radius = 0.35f, Height = StandHeight },
            Position = new Vector3(0, StandHeight / 2, 0),
        };
        AddChild(_capsule);
        Camera = new Camera3D { Name = "Camera", Fov = 78f, Near = 0.05f, Far = 4000f, Position = new Vector3(0, HeadHeight, 0) };
        AddChild(Camera);
        Camera.MakeCurrent();
    }

    public override void _UnhandledInput(InputEvent e)
    {
        if (e is InputEventMouseMotion motion && Input.MouseMode == Input.MouseModeEnum.Captured && Controllable)
            _mouse += motion.Relative;
    }

    public override void _PhysicsProcess(double delta)
    {
        float dt = (float)delta;
        _yaw -= _mouse.X * MouseSensitivity;
        _pitch = Mathf.Clamp(_pitch - _mouse.Y * MouseSensitivity, -1.5f, 1.5f);
        _mouse = Vector2.Zero;

        Vector3 v = Velocity;
        bool onFloor = IsOnFloor();
        Modifiers mods = Main.Sim.Mods;
        float jetpackMax = (float)mods.JetpackSeconds;
        if (onFloor) JetpackFuel = Mathf.MoveToward(JetpackFuel, jetpackMax, dt * 1.5f);
        JetpackFuel = Mathf.Min(JetpackFuel, jetpackMax);
        if (!onFloor) v.Y -= (float)Moves.Gravity * dt;

        Vector2 move = Controllable ? MoveInput() : Vector2.Zero;
        Crouch(Controllable && onFloor && Held(Key.Ctrl));
        bool canRun = Main.Sim.Player.Vitals.CanRun;
        Pace = move == Vector2.Zero ? Pace.Idle
            : Crouching ? Pace.Walk
            : Held(Key.Shift) && canRun ? Pace.Sprint
            : RunToggled && canRun ? Pace.Run
            : Pace.Walk;
        float speed = (float)(Crouching ? Moves.CrouchSpeed : Pace switch
        {
            Pace.Sprint => Moves.SprintSpeed,
            Pace.Run => Moves.RunSpeed,
            _ => Moves.WalkSpeed,
        } * mods.Speed);

        Vector3 wish = new Basis(Vector3.Up, _yaw) * new Vector3(move.X, 0, move.Y) * speed;
        float accel = onFloor ? GroundAccel : AirAccel;
        v.X = Mathf.MoveToward(v.X, wish.X, accel * dt);
        v.Z = Mathf.MoveToward(v.Z, wish.Z, accel * dt);

        if (Controllable && Held(Key.Space))
        {
            if (onFloor) v.Y = (float)Moves.JumpSpeed;
            else if (JetpackFuel > 0 && v.Y < 6f)
            {
                v.Y += (float)Moves.JetpackThrust * dt;
                JetpackFuel -= dt;
            }
        }

        float fallingAt = -v.Y;
        Velocity = v;
        MoveAndSlide();

        bool landed = IsOnFloor();
        if (landed && !_wasOnFloor) _landingSpeed = Mathf.Max(_landingSpeed, fallingAt);
        _wasOnFloor = landed;
        FallSpeed = landed ? 0 : Mathf.Max(0, -Velocity.Y);

        _eye = Mathf.MoveToward(_eye, Crouching ? CrouchHeadHeight : HeadHeight, 5f * dt);
        Rotation = new Vector3(0, _yaw, 0);
        Camera.Position = new Vector3(0, _eye, 0);
        Camera.Rotation = new Vector3(_pitch, 0, 0);
    }

    /// <summary>The speed of the last landing, once; 0 if there wasn't one.</summary>
    public float TakeLanding()
    {
        float speed = _landingSpeed;
        _landingSpeed = 0;
        return speed;
    }

    /// <summary>Puts the player somewhere, standing still, facing a direction (radians, 0 = north).</summary>
    public void Teleport(Vector3 feet, float yaw, float pitch = 0)
    {
        GlobalPosition = feet;
        Velocity = Vector3.Zero;
        _yaw = yaw;
        _pitch = pitch;
        _wasOnFloor = true;
        _landingSpeed = 0;
        FallSpeed = 0;
        Rotation = new Vector3(0, _yaw, 0);
        Camera.Rotation = new Vector3(_pitch, 0, 0);
        ResetPhysicsInterpolation();
    }

    public Vector3 Forward => -new Basis(Vector3.Up, _yaw).Z;

    void Crouch(bool want)
    {
        if (want) SetCrouching(true);
        else if (Crouching && !TestMove(GlobalTransform, Vector3.Up * (StandHeight - CrouchHeight))) SetCrouching(false);
    }

    void SetCrouching(bool crouching)
    {
        if (crouching == Crouching) return;
        Crouching = crouching;
        float height = crouching ? CrouchHeight : StandHeight;
        ((CapsuleShape3D)_capsule.Shape).Height = height;
        _capsule.Position = new Vector3(0, height / 2, 0);
    }

    static bool Held(Key key) => Input.IsPhysicalKeyPressed(key);
    static float Axis(Key positive, Key negative) => (Held(positive) ? 1f : 0f) - (Held(negative) ? 1f : 0f);
    static Vector2 MoveInput() => new Vector2(Axis(Key.D, Key.A), Axis(Key.S, Key.W)).LimitLength(1f);
}
