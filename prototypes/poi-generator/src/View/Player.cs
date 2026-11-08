using Godot;

namespace PoiGenerator;

/// <summary>
/// First-person walker for the walk-through, the same feel as the walkable-ship prototype:
/// WASD, Shift to sprint, Space to jump, hold Ctrl to crouch (you stay down under a low
/// ceiling), and a shoulder lamp for the dark places.
/// </summary>
public partial class Player : CharacterBody3D
{
    public Camera3D Camera;
    public SpotLight3D Lamp { get; private set; }

    const float WalkSpeed = 5f, SprintSpeed = 8.5f, CrouchSpeed = 2.3f, GroundAccel = 45f, AirAccel = 8f;
    const float JumpSpeed = 4.8f, Gravity = 9.8f;
    const float StandHeight = 1.8f, CrouchHeight = 1.1f;
    const float HeadHeight = 1.62f, CrouchHeadHeight = 0.92f;
    const float MouseSensitivity = 0.0025f;

    float _yaw, _pitch;
    Vector2 _mouse;
    float _eye = HeadHeight;
    CollisionShape3D _capsule;
    public bool Crouching { get; private set; }

    /// <summary>For tests: walk toward this point instead of reading the keyboard.</summary>
    public Vector3? AutoTarget;

    /// <summary>For tests: crouch as a player would under something low (a jammed door's leaf).</summary>
    public bool AutoCrouch;

    /// <summary>Asks the place whether there is a ladder at a point (global): then the body climbs.</summary>
    public System.Func<Vector3, bool> LadderQuery;
    public bool Climbing { get; private set; }
    const float ClimbSpeed = 2.6f;

    public Vector3 EyePosition => GlobalPosition + Vector3.Up * _eye;
    public Vector3 LookDirection => LookBasis() * Vector3.Forward;

    public override void _Ready()
    {
        _capsule = new CollisionShape3D
        {
            Shape = new CapsuleShape3D { Radius = 0.33f, Height = StandHeight },
            Position = new Vector3(0, StandHeight / 2f, 0),
        };
        AddChild(_capsule);
        CollisionLayer = 2;
        CollisionMask = 1;
        FloorMaxAngle = Mathf.DegToRad(50f); // the ramps are 45 degrees
        FloorSnapLength = 0.4f;

        Lamp = new SpotLight3D
        {
            LightColor = new Color("fff0d8"), LightEnergy = 3f, SpotRange = 26f, SpotAngle = 32f,
            SpotAttenuation = 1.1f, ShadowEnabled = true,
        };
    }

    /// <summary>The lamp lives on the camera; without one (in tests) it was never in the tree, so free it here.</summary>
    public override void _ExitTree()
    {
        if (Lamp != null && !Lamp.IsInsideTree()) Lamp.Free();
    }

    public void Place(Vector3 feet, float yaw)
    {
        GlobalPosition = feet;
        Velocity = Vector3.Zero;
        _yaw = yaw;
        _pitch = -0.08f;
        ResetPhysicsInterpolation();
        PlaceCamera();
        Camera?.ResetPhysicsInterpolation();
    }

    public void HandleInput(InputEvent e)
    {
        if (e is InputEventMouseMotion m && Input.MouseMode == Input.MouseModeEnum.Captured) _mouse += m.Relative;
    }

    public override void _PhysicsProcess(double delta)
    {
        float dt = (float)delta;
        _yaw -= _mouse.X * MouseSensitivity;
        _pitch = Mathf.Clamp(_pitch - _mouse.Y * MouseSensitivity, -1.5f, 1.5f);
        _mouse = Vector2.Zero;

        Vector3 v = Velocity;
        bool onFloor = IsOnFloor();
        // On a ladder (or just over its top rung) there is no falling: you climb.
        Climbing = LadderQuery != null && (LadderQuery(GlobalPosition + Vector3.Up * 0.1f) || LadderQuery(GlobalPosition + Vector3.Down * 0.45f));
        if (Climbing)
        {
            float climb = AutoTarget is Vector3 at
                ? Mathf.Clamp((at.Y + 0.12f - GlobalPosition.Y) * 4f, -ClimbSpeed, ClimbSpeed)
                : Axis(Key.W, Key.S) * ClimbSpeed;
            v.Y = climb;
        }
        else if (!onFloor) v.Y -= Gravity * dt;
        Crouch(onFloor && (Held(Key.Ctrl) || AutoCrouch));

        var move = new Vector2(Axis(Key.D, Key.A), Axis(Key.S, Key.W)).LimitLength(1f);
        float speed = Crouching ? CrouchSpeed : Held(Key.Shift) ? SprintSpeed : WalkSpeed;
        Vector3 wish = new Basis(Vector3.Up, _yaw) * new Vector3(move.X, 0, move.Y) * speed;
        if (AutoTarget is Vector3 target)
        {
            // The test autopilot walks toward a point instead of reading keys.
            Vector3 to = (target - GlobalPosition) with { Y = 0 };
            wish = to.Length() > 0.05f ? to.Normalized() * WalkSpeed : Vector3.Zero;
        }
        float accel = onFloor ? GroundAccel : AirAccel;
        v.X = Mathf.MoveToward(v.X, wish.X, accel * dt);
        v.Z = Mathf.MoveToward(v.Z, wish.Z, accel * dt);
        if ((onFloor || Climbing) && Held(Key.Space) && !Crouching && AutoTarget == null) v.Y = JumpSpeed;
        Velocity = v;
        MoveAndSlide();

        _eye = Mathf.MoveToward(_eye, Crouching ? CrouchHeadHeight : HeadHeight, 5f * dt);
        PlaceCamera();
    }

    void PlaceCamera()
    {
        if (Camera == null) return;
        Camera.GlobalTransform = new Transform3D(LookBasis(), GlobalPosition + Vector3.Up * _eye);
    }

    /// <summary>Crouch while asked; stand again only where there's headroom.</summary>
    void Crouch(bool want)
    {
        if (want) SetCrouching(true);
        else if (Crouching && !TestMove(GlobalTransform, Vector3.Up * (StandHeight - CrouchHeight))) SetCrouching(false);
    }

    void SetCrouching(bool c)
    {
        if (c == Crouching) return;
        Crouching = c;
        float h = c ? CrouchHeight : StandHeight;
        ((CapsuleShape3D)_capsule.Shape).Height = h;
        _capsule.Position = new Vector3(0, h / 2f, 0);
    }

    Basis LookBasis() => new Basis(Vector3.Up, _yaw) * new Basis(Vector3.Right, _pitch);

    public void SetLook(float yaw, float pitch)
    {
        _yaw = yaw;
        _pitch = pitch;
        PlaceCamera();
    }

    static bool Held(Key k) => Input.IsPhysicalKeyPressed(k);
    static float Axis(Key pos, Key neg) => (Held(pos) ? 1f : 0f) - (Held(neg) ? 1f : 0f);
}
