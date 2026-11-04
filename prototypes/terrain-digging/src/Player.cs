using Godot;

namespace TerrainDigging;

/// <summary>
/// A first-person survivor: walk, sprint, jump, a small jetpack for climbing out of pits,
/// and crouch. Crouching shrinks the capsule and lowers the eye; standing back up waits
/// until there is room overhead, so an overhang can hold you down but never trap you inside it.
/// </summary>
public partial class Player : CharacterBody3D
{
    public const float StandHeight = 1.8f, CrouchHeight = 0.9f, Radius = 0.35f;
    const float StandEye = 1.62f, CrouchEye = 0.72f;
    const float WalkSpeed = 5.5f, SprintSpeed = 9f, CrouchSpeed = 2.4f;
    const float GroundAccel = 45f, AirAccel = 10f;
    const float JumpSpeed = 5.4f, Gravity = 13f;
    const float JetpackThrust = 24f, JetpackFuelMax = 2.5f;
    const float MouseSensitivity = 0.0025f;

    public Camera3D Camera;

    /// <summary>Off while the world is generating, and in tests that drive the player directly.</summary>
    public bool ControlsEnabled = true;

    /// <summary>Lets tests hold crouch without a keyboard.</summary>
    public bool CrouchOverride;

    public bool IsCrouching { get; private set; }
    public float CurrentHeight => IsCrouching ? CrouchHeight : StandHeight;
    public float CapsuleHeight => _capsule.Height;
    public float JetpackFuel => _jetFuel / JetpackFuelMax;

    /// <summary>The eye, in world space (uninterpolated: what the tool aims from).</summary>
    public Vector3 EyePosition => GlobalPosition + Vector3.Up * _eyeHeight;
    public Vector3 LookDirection => LookBasis() * Vector3.Forward;

    float _yaw, _pitch, _eyeHeight = StandEye;
    float _kick, _kickVelocity, _shake;
    float _jetFuel = JetpackFuelMax;
    Vector2 _mouse;
    CapsuleShape3D _capsule;
    CollisionShape3D _shape;
    CapsuleShape3D _standProbe;
    readonly RandomNumberGenerator _rng = new() { Seed = 7 };

    public override void _Ready()
    {
        _capsule = new CapsuleShape3D { Radius = Radius, Height = StandHeight };
        _shape = new CollisionShape3D { Shape = _capsule, Position = new Vector3(0, StandHeight / 2f, 0) };
        AddChild(_shape);
        // Slightly thinner than the body and lifted off the floor, so standing up on a slope or
        // against a wall isn't mistaken for "no room overhead".
        _standProbe = new CapsuleShape3D { Radius = Radius - 0.03f, Height = StandHeight - 0.1f };

        CollisionLayer = Layers.Player;
        CollisionMask = Layers.Solid | Layers.Props;
        FloorMaxAngle = Mathf.DegToRad(50f);
        FloorSnapLength = 0.35f;
    }

    public override void _UnhandledInput(InputEvent e)
    {
        if (!ControlsEnabled) return;
        if (e is InputEventMouseMotion motion && Input.MouseMode == Input.MouseModeEnum.Captured)
            _mouse += motion.Relative;
        else if (e is InputEventMouseButton { Pressed: true } && Input.MouseMode != Input.MouseModeEnum.Captured)
            Input.MouseMode = Input.MouseModeEnum.Captured;
        else if (e is InputEventKey { Pressed: true, Echo: false, PhysicalKeycode: Key.Escape })
            Input.MouseMode = Input.MouseModeEnum.Visible;
    }

    public override void _PhysicsProcess(double delta)
    {
        float dt = (float)delta;
        Vector2 mouse = _mouse;
        _mouse = Vector2.Zero;

        _yaw -= mouse.X * MouseSensitivity;
        _pitch = Mathf.Clamp(_pitch - mouse.Y * MouseSensitivity, -1.5f, 1.5f);

        UpdateCrouch(CrouchOverride || (ControlsEnabled && Held(Key.Ctrl)));
        Walk(dt);
        PlaceCamera(dt);
    }

    // ---- Movement ---------------------------------------------------------------------

    void Walk(float dt)
    {
        Vector3 v = Velocity;
        bool onFloor = IsOnFloor();
        if (onFloor) _jetFuel = Mathf.MoveToward(_jetFuel, JetpackFuelMax, dt);
        else v.Y -= Gravity * dt;

        Vector2 move = ControlsEnabled ? MoveInput() : Vector2.Zero;
        float speed = IsCrouching ? CrouchSpeed : Held(Key.Shift) && ControlsEnabled ? SprintSpeed : WalkSpeed;
        Vector3 wish = new Basis(Vector3.Up, _yaw) * new Vector3(move.X, 0, move.Y) * speed;
        float accel = onFloor ? GroundAccel : AirAccel;
        v.X = Mathf.MoveToward(v.X, wish.X, accel * dt);
        v.Z = Mathf.MoveToward(v.Z, wish.Z, accel * dt);

        if (ControlsEnabled && Held(Key.Space) && !IsCrouching)
        {
            if (onFloor) v.Y = JumpSpeed;
            else if (_jetFuel > 0f && v.Y < 7f)
            {
                v.Y += JetpackThrust * dt;
                _jetFuel -= dt;
            }
        }

        Velocity = v;
        MoveAndSlide();
    }

    void UpdateCrouch(bool wantCrouch)
    {
        if (wantCrouch && !IsCrouching && IsOnFloor()) SetCrouch(true);
        else if (!wantCrouch && IsCrouching && CanStand()) SetCrouch(false);
    }

    void SetCrouch(bool crouch)
    {
        IsCrouching = crouch;
        float height = crouch ? CrouchHeight : StandHeight;
        _capsule.Height = height;
        _shape.Position = new Vector3(0, height / 2f, 0);
    }

    /// <summary>Whether a standing capsule would fit here right now (terrain, blocks and props).</summary>
    public bool CanStand()
    {
        var query = new PhysicsShapeQueryParameters3D
        {
            Shape = _standProbe,
            Transform = new Transform3D(Basis.Identity, GlobalPosition + Vector3.Up * (0.08f + _standProbe.Height / 2f)),
            CollisionMask = Layers.Solid | Layers.Props,
            Exclude = new Godot.Collections.Array<Rid> { GetRid() },
        };
        return GetWorld3D().DirectSpaceState.IntersectShape(query, 1).Count == 0;
    }

    // ---- Camera -----------------------------------------------------------------------

    /// <summary>A small upward kick (degrees) and optional shake, for digging and blasts.</summary>
    public void Kick(float degrees, float shake = 0f)
    {
        _kickVelocity += Mathf.DegToRad(degrees) * 18f;
        _shake = Mathf.Max(_shake, shake);
    }

    void PlaceCamera(float dt)
    {
        _eyeHeight = Mathf.MoveToward(_eyeHeight, IsCrouching ? CrouchEye : StandEye, 5f * dt);

        // A stiff spring: the kick snaps up and settles in about a tenth of a second.
        _kickVelocity += (-_kick * 420f - _kickVelocity * 30f) * dt;
        _kick += _kickVelocity * dt;
        _shake = Mathf.MoveToward(_shake, 0f, dt * 1.8f);
        float jitter = _shake * _shake;
        var shake = new Vector3(_rng.RandfRange(-1f, 1f), _rng.RandfRange(-1f, 1f), 0f) * jitter * 0.05f;

        Basis view = new Basis(Vector3.Up, _yaw + shake.X) * new Basis(Vector3.Right, _pitch + _kick + shake.Y);
        Camera.GlobalTransform = new Transform3D(view, EyePosition);
    }

    Basis LookBasis() => new Basis(Vector3.Up, _yaw) * new Basis(Vector3.Right, _pitch);

    // ---- Moves made by other systems --------------------------------------------------

    /// <summary>Moves the player (stuck rescue, load, tests), keeping the view.</summary>
    public void Teleport(Vector3 feet)
    {
        GlobalPosition = feet;
        Velocity = Vector3.Zero;
        ResetPhysicsInterpolation();
        Camera?.ResetPhysicsInterpolation();
    }

    public void SetLook(float yaw, float pitch)
    {
        _yaw = yaw;
        _pitch = pitch;
    }

    /// <summary>Crouches immediately (tests): as if Ctrl had been held on the ground.</summary>
    public void ForceCrouch() => SetCrouch(true);

    static bool Held(Key key) => Input.IsPhysicalKeyPressed(key);
    static float Axis(Key positive, Key negative) => (Held(positive) ? 1f : 0f) - (Held(negative) ? 1f : 0f);
    static Vector2 MoveInput() => new Vector2(Axis(Key.D, Key.A), Axis(Key.S, Key.W)).LimitLength(1f);
}
