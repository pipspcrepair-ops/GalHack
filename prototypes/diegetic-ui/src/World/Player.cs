using Godot;

namespace DiegeticUi.World;

public static class Layers
{
    public const uint World = 1;
    public const uint Pick = global::DiegeticUi.Screens.WorldScreen.PickLayer; // screens and the holo-table
    public const uint Player = 4;
    public const uint Crates = 8;
}

/// <summary>
/// A first-person body: walk, sprint (Shift), jump (Space), crouch (hold Ctrl). The camera is
/// not a child: <see cref="HeadView"/> gives where it would be, and the interactor blends
/// that with whatever screen is in use.
///
/// Mouse-look stops while a screen is in use, but walking does not stop while the wrist is
/// raised: the world keeps running.
/// </summary>
public partial class Player : CharacterBody3D
{
    const float WalkSpeed = 4.5f, SprintSpeed = 7.5f, CrouchSpeed = 2.2f, Accel = 40f, AirAccel = 8f;
    const float JumpSpeed = 4.6f, Gravity = 9.8f;
    const float StandHeight = 1.8f, CrouchHeight = 1.1f, EyeStand = 1.62f, EyeCrouch = 0.95f;
    const float MouseSensitivity = 0.0025f;

    public float Yaw { get; private set; }
    public float Pitch { get; private set; }
    public bool Crouching { get; private set; }

    /// <summary>Set by the game: true while a screen has the mouse (no looking around).</summary>
    public bool LookLocked { get; set; }
    /// <summary>Set by the game: true while a framed screen is in use (no walking either).</summary>
    public bool MoveLocked { get; set; }

    /// <summary>How fast the view turned this frame, radians/s. The visor lags against it.</summary>
    public Vector2 LookRate { get; private set; }

    CollisionShape3D _capsule;
    Vector2 _mouse;
    float _eye = EyeStand;

    public override void _Ready()
    {
        CollisionLayer = Layers.Player;
        CollisionMask = Layers.World | Layers.Crates;
        _capsule = new CollisionShape3D
        {
            Name = "Capsule",
            Shape = new CapsuleShape3D { Radius = 0.33f, Height = StandHeight },
            Position = new Vector3(0, StandHeight / 2, 0),
        };
        AddChild(_capsule);
        FloorMaxAngle = Mathf.DegToRad(50);
    }

    public void HandleInput(InputEvent e)
    {
        if (e is InputEventMouseMotion motion && Input.MouseMode == Input.MouseModeEnum.Captured && !LookLocked)
            _mouse += motion.Relative;
        else if (e is InputEventMouseButton { Pressed: true } && Input.MouseMode != Input.MouseModeEnum.Captured)
            Input.MouseMode = Input.MouseModeEnum.Captured;
        else if (e is InputEventKey { Pressed: true, Echo: false, PhysicalKeycode: Key.Escape })
            Input.MouseMode = Input.MouseModeEnum.Visible;
    }

    public override void _Process(double delta)
    {
        // Look in _Process, at frame rate: mouse-look at the physics rate feels sticky.
        float dt = Mathf.Max((float)delta, 1e-4f);
        Vector2 before = new(Yaw, Pitch);
        Yaw -= _mouse.X * MouseSensitivity;
        Pitch = Mathf.Clamp(Pitch - _mouse.Y * MouseSensitivity, -1.45f, 1.45f);
        _mouse = Vector2.Zero;
        LookRate = (new Vector2(Yaw, Pitch) - before) / dt;
        _eye = Mathf.MoveToward(_eye, Crouching ? EyeCrouch : EyeStand, 4.5f * dt);
    }

    public override void _PhysicsProcess(double delta)
    {
        float dt = (float)delta;
        Vector3 v = Velocity;
        bool onFloor = IsOnFloor();
        if (!onFloor) v.Y -= Gravity * dt;

        Crouch(!MoveLocked && onFloor && Held(Key.Ctrl));
        Vector2 move = MoveLocked ? Vector2.Zero : new Vector2(Axis(Key.D, Key.A), Axis(Key.S, Key.W)).LimitLength(1);
        float speed = Crouching ? CrouchSpeed : Held(Key.Shift) ? SprintSpeed : WalkSpeed;
        Vector3 wish = new Basis(Vector3.Up, Yaw) * new Vector3(move.X, 0, move.Y) * speed;
        float accel = onFloor ? Accel : AirAccel;
        v.X = Mathf.MoveToward(v.X, wish.X, accel * dt);
        v.Z = Mathf.MoveToward(v.Z, wish.Z, accel * dt);
        if (!MoveLocked && onFloor && Held(Key.Space)) v.Y = JumpSpeed;

        Velocity = v;
        MoveAndSlide();
    }

    /// <summary>Where the eyes are this frame, smoothed between physics ticks.</summary>
    public Transform3D HeadView()
    {
        Vector3 feet = GetGlobalTransformInterpolated().Origin;
        var look = new Basis(Vector3.Up, Yaw) * new Basis(Vector3.Right, Pitch);
        return new Transform3D(look, feet + Vector3.Up * _eye);
    }

    void Crouch(bool want)
    {
        if (want) SetCrouching(true);
        else if (Crouching && !TestMove(GlobalTransform, Vector3.Up * (StandHeight - CrouchHeight))) SetCrouching(false);
    }

    void SetCrouching(bool crouching)
    {
        if (crouching == Crouching) return;
        Crouching = crouching;
        float h = crouching ? CrouchHeight : StandHeight;
        ((CapsuleShape3D)_capsule.Shape).Height = h;
        _capsule.Position = new Vector3(0, h / 2, 0);
    }

    /// <summary>Puts the player somewhere, facing a way, moving at a velocity (the cold-zone shove, tests).</summary>
    public void Place(Vector3 feet, float yaw, float pitch = 0, Vector3 velocity = default)
    {
        GlobalPosition = feet;
        Velocity = velocity;
        Yaw = yaw;
        Pitch = pitch;
        ResetPhysicsInterpolation();
    }

    /// <summary>Points the view. For tests and screenshots.</summary>
    public void SetLook(float yaw, float pitch)
    {
        Yaw = yaw;
        Pitch = pitch;
    }

    static bool Held(Key key) => Input.IsPhysicalKeyPressed(key);
    static float Axis(Key positive, Key negative) => (Held(positive) ? 1f : 0f) - (Held(negative) ? 1f : 0f);
}
