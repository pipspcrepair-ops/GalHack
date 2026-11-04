using System;
using Godot;
using GalHack.Structures.Presentation;

namespace BlockBuilding;

/// <summary>
/// A first-person builder: walk, sprint (Shift), jump (Space), crouch (hold Ctrl) and a fly
/// toggle (N) for building big things. Ladders are climbed by walking into them.
/// </summary>
public partial class Player : CharacterBody3D
{
    const float WalkSpeed = 5.5f, SprintSpeed = 9.5f, CrouchSpeed = 2.6f, GroundAccel = 50f, AirAccel = 12f;
    const float JumpSpeed = 5.2f, Gravity = 13f, ClimbSpeed = 3.2f;
    const float FlySpeed = 12f, FlyBoost = 32f, FlyAccel = 60f;
    const float StandHeight = 1.8f, CrouchHeight = 1.1f, HeadHeight = 1.62f, CrouchHeadHeight = 0.95f;
    const float MouseSensitivity = 0.0025f;

    public Camera3D Camera { get; private set; }
    public bool Flying { get; private set; }
    public bool Crouching { get; private set; }

    /// <summary>Asked each tick whether a world point is inside a ladder block.</summary>
    public Func<Vector3, bool> IsLadderAt = _ => false;

    float _yaw, _pitch, _eye = HeadHeight;
    Vector2 _mouse;
    CollisionShape3D _capsule;

    public override void _Ready()
    {
        CollisionLayer = PhysicsLayers.Player;
        CollisionMask = PhysicsLayers.World | PhysicsLayers.Structures;
        FloorMaxAngle = Mathf.DegToRad(50f);
        FloorSnapLength = 0.4f;
        _capsule = new CollisionShape3D
        {
            Shape = new CapsuleShape3D { Radius = 0.35f, Height = StandHeight },
            Position = new Vector3(0, StandHeight / 2f, 0),
        };
        AddChild(_capsule);
        Camera = new Camera3D { Fov = 78f, Near = 0.05f, Far = 9000f, Position = new Vector3(0, HeadHeight, 0) };
        AddChild(Camera);
        Camera.MakeCurrent();
    }

    public void HandleInput(InputEvent e)
    {
        if (e is InputEventMouseMotion motion && Input.MouseMode == Input.MouseModeEnum.Captured)
            _mouse += motion.Relative;
        else if (e is InputEventKey { Pressed: true, Echo: false, PhysicalKeycode: Key.N })
            SetFlying(!Flying);
    }

    public void SetFlying(bool on)
    {
        Flying = on;
        MotionMode = on ? MotionModeEnum.Floating : MotionModeEnum.Grounded;
        if (on) SetCrouching(false);
        Velocity = Vector3.Zero;
    }

    public override void _PhysicsProcess(double delta)
    {
        float dt = (float)delta;
        _yaw -= _mouse.X * MouseSensitivity;
        _pitch = Mathf.Clamp(_pitch - _mouse.Y * MouseSensitivity, -1.55f, 1.55f);
        _mouse = Vector2.Zero;

        if (Flying) Fly(dt);
        else Walk(dt);

        _eye = Mathf.MoveToward(_eye, Crouching ? CrouchHeadHeight : HeadHeight, 6f * dt);
        Camera.Position = new Vector3(0, _eye, 0);
        Camera.Rotation = new Vector3(_pitch, 0, 0);
        Rotation = new Vector3(0, _yaw, 0);
    }

    void Walk(float dt)
    {
        Vector3 v = Velocity;
        bool onFloor = IsOnFloor();
        Vector2 move = MoveInput();
        bool climbing = IsLadderAt(GlobalPosition + Vector3.Up * 0.6f) || IsLadderAt(GlobalPosition + Vector3.Up * 1.4f);

        if (Held(Key.Ctrl) && onFloor) SetCrouching(true);
        else if (Crouching && !Held(Key.Ctrl) && !TestMove(GlobalTransform, Vector3.Up * (StandHeight - CrouchHeight))) SetCrouching(false);

        float speed = Crouching ? CrouchSpeed : Held(Key.Shift) ? SprintSpeed : WalkSpeed;
        Vector3 wish = new Basis(Vector3.Up, _yaw) * new Vector3(move.X, 0, move.Y) * speed;
        float accel = onFloor ? GroundAccel : AirAccel;
        v.X = Mathf.MoveToward(v.X, wish.X, accel * dt);
        v.Z = Mathf.MoveToward(v.Z, wish.Z, accel * dt);

        if (climbing)
        {
            // On a ladder: forward or jump climbs, crouch descends, otherwise hang on.
            float up = (move.Y < -0.1f || Held(Key.Space) ? 1f : 0f) - (Held(Key.Ctrl) ? 1f : 0f);
            v.Y = up * ClimbSpeed;
        }
        else if (!onFloor) v.Y -= Gravity * dt;
        else if (Held(Key.Space) && !Crouching) v.Y = JumpSpeed;

        Velocity = v;
        MoveAndSlide();
    }

    void Fly(float dt)
    {
        Vector2 move = MoveInput();
        float vertical = (Held(Key.Space) ? 1f : 0f) - (Held(Key.Ctrl) ? 1f : 0f);
        Basis look = LookBasis();
        Vector3 wish = (look * new Vector3(move.X, 0, move.Y) + Vector3.Up * vertical).LimitLength(1f) * (Held(Key.Shift) ? FlyBoost : FlySpeed);
        Velocity = Velocity.MoveToward(wish, FlyAccel * dt);
        MoveAndSlide();
    }

    void SetCrouching(bool crouching)
    {
        if (crouching == Crouching) return;
        Crouching = crouching;
        float height = crouching ? CrouchHeight : StandHeight;
        ((CapsuleShape3D)_capsule.Shape).Height = height;
        _capsule.Position = new Vector3(0, height / 2f, 0);
    }

    public Basis LookBasis() => new Basis(Vector3.Up, _yaw) * new Basis(Vector3.Right, _pitch);

    /// <summary>The capsule's world-space bounds, so building never traps the player inside a block.</summary>
    public Aabb Bounds()
    {
        float h = Crouching ? CrouchHeight : StandHeight;
        return new Aabb(GlobalPosition + new Vector3(-0.35f, 0f, -0.35f), new Vector3(0.7f, h, 0.7f));
    }

    // ---- Test and screenshot hooks ------------------------------------------------------

    public void DebugPlace(Vector3 feet, float yaw, float pitch, bool fly = false)
    {
        GlobalPosition = feet;
        _yaw = yaw;
        _pitch = pitch;
        Rotation = new Vector3(0, _yaw, 0);
        Camera.Rotation = new Vector3(_pitch, 0, 0);
        SetFlying(fly);
        ResetPhysicsInterpolation();
        Camera.ResetPhysicsInterpolation();
    }

    /// <summary>Points the camera at a world position.</summary>
    public void DebugLookAt(Vector3 target)
    {
        Vector3 d = (target - Camera.GlobalPosition).Normalized();
        _yaw = Mathf.Atan2(-d.X, -d.Z);
        _pitch = Mathf.Asin(Mathf.Clamp(d.Y, -1f, 1f));
        Rotation = new Vector3(0, _yaw, 0);
        Camera.Rotation = new Vector3(_pitch, 0, 0);
    }

    static bool Held(Key key) => Input.IsPhysicalKeyPressed(key);
    static float Axis(Key positive, Key negative) => (Held(positive) ? 1f : 0f) - (Held(negative) ? 1f : 0f);
    static Vector2 MoveInput() => new Vector2(Axis(Key.D, Key.A), Axis(Key.S, Key.W)).LimitLength(1f);
}
