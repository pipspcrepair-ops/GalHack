using Godot;

namespace WalkableShip;

/// <summary>
/// A first-person player who can walk around inside a moving ship, fly it, or step off it.
///
/// Aboard, the player's physics body lives on the ship's ghost cabin and the camera is placed
/// at ship transform × that position, so the ship's motion never touches the character
/// controller. Stepping off converts position, velocity and view direction into the world
/// frame (adding the ship's velocity at the door); stepping on does the reverse.
/// </summary>
public partial class Player : CharacterBody3D
{
    public enum Mode { Outside, Aboard, Piloting }

    public Mode CurrentMode { get; private set; } = Mode.Aboard;
    public Ship Ship;
    public Camera3D Camera;
    public bool ThirdPerson;

    const float WalkSpeed = 5.5f, SprintSpeed = 9f, GroundAccel = 45f, AirAccel = 10f;
    const float JumpSpeed = 4.8f, Gravity = 9.8f, HeadHeight = 1.6f;
    const float ZeroGSpeed = 5f, ZeroGAccel = 7f;
    const float JetpackThrust = 18f, JetpackFuelMax = 3f;
    const float MouseSensitivity = 0.0025f;

    float _yaw, _pitch;   // relative to the ship while aboard, to the world while outside
    Vector2 _mouse;
    float _jetFuel = JetpackFuelMax;
    Node3D _body;         // what third person (and later, other players) see

    /// <summary>Position on the ghost cabin, which is the same as ship-local coordinates.</summary>
    public Vector3 ShipLocalPosition => GlobalPosition - Ship.GhostOrigin;

    public float JetpackFuel => _jetFuel / JetpackFuelMax;

    public override void _Ready()
    {
        AddChild(new CollisionShape3D
        {
            Shape = new CapsuleShape3D { Radius = 0.35f, Height = 1.8f },
            Position = new Vector3(0, 0.9f, 0),
        });
        FloorMaxAngle = Mathf.DegToRad(50f);
        SetAboardPhysics();
        BuildBody();
        Input.MouseMode = Input.MouseModeEnum.Captured;
    }

    public override void _UnhandledInput(InputEvent e)
    {
        if (e is InputEventMouseMotion motion && Input.MouseMode == Input.MouseModeEnum.Captured)
        {
            _mouse += motion.Relative;
        }
        else if (e is InputEventMouseButton { Pressed: true } && Input.MouseMode != Input.MouseModeEnum.Captured)
        {
            Input.MouseMode = Input.MouseModeEnum.Captured;
        }
        else if (e is InputEventKey { Pressed: true, Echo: false } key)
        {
            switch (key.PhysicalKeycode)
            {
                case Key.Escape: Input.MouseMode = Input.MouseModeEnum.Visible; break;
                case Key.F: ToggleSeat(); break;
                case Key.G: Ship.ToggleGravity(); break;
                case Key.T: Ship.StuntAutopilot = !Ship.StuntAutopilot; break;
                case Key.V: ThirdPerson = !ThirdPerson; break;
                case Key.R: ReturnAboard(); break;
            }
        }
    }

    public override void _PhysicsProcess(double delta)
    {
        float dt = (float)delta;
        Vector2 mouse = _mouse;
        _mouse = Vector2.Zero;

        switch (CurrentMode)
        {
            case Mode.Piloting:
                Pilot(mouse);
                break;

            case Mode.Aboard:
                Look(mouse);
                if (Ship.GravityOn) Walk(dt, outside: false);
                else Float(dt);
                PushCargo();
                if (!ShipLayout.StillAboard(ShipLocalPosition)) StepOff();
                break;

            case Mode.Outside:
                Look(mouse);
                Walk(dt, outside: true);
                Vector3 local = Ship.GlobalTransform.AffineInverse() * GlobalPosition;
                if (ShipLayout.ShouldBoard(local)) StepOn(local);
                break;
        }

        PlaceCameraAndBody();
    }

    // ---- Movement ---------------------------------------------------------------------

    void Walk(float dt, bool outside)
    {
        MotionMode = MotionModeEnum.Grounded;
        UpDirection = Vector3.Up;
        Vector3 v = Velocity;
        bool onFloor = IsOnFloor();
        if (onFloor) _jetFuel = Mathf.MoveToward(_jetFuel, JetpackFuelMax, dt);
        else v.Y -= Gravity * dt;

        Vector2 move = MoveInput();
        float speed = Held(Key.Shift) ? SprintSpeed : WalkSpeed;
        Vector3 wish = new Basis(Vector3.Up, _yaw) * new Vector3(move.X, 0, move.Y) * speed;
        if (onFloor)
        {
            v.X = Mathf.MoveToward(v.X, wish.X, GroundAccel * dt);
            v.Z = Mathf.MoveToward(v.Z, wish.Z, GroundAccel * dt);
        }
        else if (move != Vector2.Zero)
        {
            // In the air, steer without bleeding off momentum: jumping off a fast ship keeps its speed.
            Vector3 horizontal = new(v.X, 0, v.Z);
            Vector3 steered = horizontal + wish.Normalized() * AirAccel * dt;
            if (steered.Length() > Mathf.Max(horizontal.Length(), speed))
                steered = steered.LimitLength(Mathf.Max(horizontal.Length(), speed));
            v.X = steered.X;
            v.Z = steered.Z;
        }

        if (Held(Key.Space))
        {
            if (onFloor) v.Y = JumpSpeed;
            else if (outside && _jetFuel > 0f && v.Y < 8f)
            {
                v.Y += JetpackThrust * dt;
                _jetFuel -= dt;
            }
        }

        Velocity = v;
        MoveAndSlide();
    }

    /// <summary>Zero-g aboard: drift and steer with suit thrusters, relative to where you look.</summary>
    void Float(float dt)
    {
        MotionMode = MotionModeEnum.Floating;
        Vector2 move = MoveInput();
        float vertical = Axis(Key.Space, Key.Ctrl);
        Basis look = LookBasis();
        Vector3 wish = (look * new Vector3(move.X, 0, move.Y) + look.Y * vertical).LimitLength(1f) * ZeroGSpeed;
        Velocity = Velocity.MoveToward(wish, ZeroGAccel * dt);
        MoveAndSlide();
    }

    void Look(Vector2 mouse)
    {
        _yaw -= mouse.X * MouseSensitivity;
        _pitch = Mathf.Clamp(_pitch - mouse.Y * MouseSensitivity, -1.5f, 1.5f);
    }

    Basis LookBasis() => new Basis(Vector3.Up, _yaw) * new Basis(Vector3.Right, _pitch);

    void PushCargo()
    {
        for (int i = 0; i < GetSlideCollisionCount(); i++)
        {
            KinematicCollision3D hit = GetSlideCollision(i);
            if (hit.GetCollider() is RigidBody3D crate)
                crate.ApplyCentralImpulse(-hit.GetNormal() * 1.5f);
        }
    }

    // ---- Flying -----------------------------------------------------------------------

    void Pilot(Vector2 mouse)
    {
        Ship.ThrustInput = new Vector3(Axis(Key.D, Key.A), Axis(Key.Space, Key.Ctrl), Axis(Key.S, Key.W));
        Ship.TurnInput = new Vector3(
            Mathf.Clamp(-mouse.Y * 0.06f, -1f, 1f),
            Mathf.Clamp(-mouse.X * 0.06f, -1f, 1f),
            Axis(Key.Q, Key.E));
        Ship.Boost = Held(Key.Shift);
    }

    void ToggleSeat()
    {
        if (CurrentMode == Mode.Piloting) StandUp();
        else if (CurrentMode == Mode.Aboard && ShipLocalPosition.DistanceTo(ShipLayout.SeatLocal) < 2.2f) SitDown();
    }

    void SitDown()
    {
        CurrentMode = Mode.Piloting;
        CollisionLayer = 0;
        CollisionMask = 0;
        Velocity = Vector3.Zero;
        GlobalPosition = Ship.GhostOrigin + ShipLayout.SeatLocal;
        _yaw = 0f;
        _pitch = 0f;
        Ship.StuntAutopilot = false;
        Ship.Piloted = true;
    }

    void StandUp()
    {
        Ship.Piloted = false;
        Ship.ThrustInput = Vector3.Zero;
        Ship.TurnInput = Vector3.Zero;
        Ship.Boost = false;
        CurrentMode = Mode.Aboard;
        SetAboardPhysics();
        GlobalPosition = Ship.GhostOrigin + ShipLayout.SeatExitLocal;
        ResetInterpolation();
    }

    // ---- Changing frames --------------------------------------------------------------

    /// <summary>Leave the ship: keep the ship's velocity at this point, plus your own.</summary>
    void StepOff()
    {
        Vector3 worldPos = Ship.GlobalTransform * ShipLocalPosition;
        Vector3 worldVel = Ship.PointVelocity(worldPos) + Ship.GlobalBasis * Velocity;
        SetLookFrom(Ship.GlobalBasis * (LookBasis() * Vector3.Forward));
        CurrentMode = Mode.Outside;
        SetOutsidePhysics();
        GlobalPosition = worldPos;
        Velocity = worldVel;
        ResetInterpolation();
    }

    /// <summary>Board the ship: subtract the ship's velocity at this point.</summary>
    void StepOn(Vector3 local)
    {
        Basis toShip = Ship.GlobalBasis.Inverse();
        Vector3 localVel = toShip * (Velocity - Ship.PointVelocity(GlobalPosition));
        SetLookFrom(toShip * (LookBasis() * Vector3.Forward));
        CurrentMode = Mode.Aboard;
        SetAboardPhysics();
        GlobalPosition = Ship.GhostOrigin + local;
        Velocity = localVel;
        ResetInterpolation();
    }

    void ReturnAboard()
    {
        if (CurrentMode == Mode.Piloting) return;
        CurrentMode = Mode.Aboard;
        SetAboardPhysics();
        GlobalPosition = Ship.GhostOrigin + ShipLayout.SpawnLocal;
        Velocity = Vector3.Zero;
        ResetInterpolation();
    }

    void SetLookFrom(Vector3 forward)
    {
        forward = forward.Normalized();
        _yaw = Mathf.Atan2(-forward.X, -forward.Z);
        _pitch = Mathf.Asin(Mathf.Clamp(forward.Y, -1f, 1f));
    }

    void SetAboardPhysics()
    {
        CollisionLayer = Layers.Interior;
        CollisionMask = Layers.Interior;
    }

    void SetOutsidePhysics()
    {
        CollisionLayer = 0;
        CollisionMask = Layers.World | Layers.ShipHull;
    }

    void ResetInterpolation()
    {
        ResetPhysicsInterpolation();
        Camera?.ResetPhysicsInterpolation();
        _body?.ResetPhysicsInterpolation();
    }

    // ---- Camera and body --------------------------------------------------------------

    void PlaceCameraAndBody()
    {
        Transform3D frame = CurrentMode == Mode.Outside ? Transform3D.Identity : Ship.GlobalTransform;
        Vector3 feet = CurrentMode switch
        {
            Mode.Outside => GlobalPosition,
            Mode.Piloting => ShipLayout.SeatLocal,
            _ => ShipLocalPosition,
        };
        Vector3 eye = CurrentMode == Mode.Piloting ? ShipLayout.SeatEyeLocal : feet + Vector3.Up * HeadHeight;

        var view = new Transform3D(LookBasis(), eye);
        if (ThirdPerson) view = view.TranslatedLocal(new Vector3(0, 0.4f, 3.6f));
        Camera.GlobalTransform = frame * view;

        _body.GlobalTransform = frame * new Transform3D(new Basis(Vector3.Up, _yaw), feet);
        _body.Visible = ThirdPerson;
    }

    void BuildBody()
    {
        _body = new Node3D { Name = "Body", TopLevel = true };
        AddChild(_body);
        _body.AddChild(new MeshInstance3D
        {
            Mesh = new CapsuleMesh { Radius = 0.35f, Height = 1.8f },
            MaterialOverride = new StandardMaterial3D { AlbedoColor = ShipLayout.Yellow, Roughness = 0.6f },
            Position = new Vector3(0, 0.9f, 0),
        });
        _body.AddChild(new MeshInstance3D
        {
            Mesh = new BoxMesh { Size = new Vector3(0.46f, 0.2f, 0.14f) },
            MaterialOverride = new StandardMaterial3D { AlbedoColor = new Color("1b263b"), Metallic = 0.6f, Roughness = 0.1f },
            Position = new Vector3(0, 1.55f, -0.3f),
        });
    }

    // ---- Test hooks -------------------------------------------------------------------

    /// <summary>Puts the player aboard at a ship-local position and velocity. For tests.</summary>
    public void DebugPlaceAboard(Vector3 local, Vector3 localVelocity)
    {
        if (CurrentMode == Mode.Piloting) StandUp();
        CurrentMode = Mode.Aboard;
        SetAboardPhysics();
        GlobalPosition = Ship.GhostOrigin + local;
        Velocity = localVelocity;
        ResetInterpolation();
    }

    /// <summary>Puts the player outside at a world position, standing still. For tests.</summary>
    public void DebugPlaceOutside(Vector3 worldPosition)
    {
        if (CurrentMode == Mode.Piloting) StandUp();
        CurrentMode = Mode.Outside;
        SetOutsidePhysics();
        GlobalPosition = worldPosition;
        Velocity = Vector3.Zero;
        ResetInterpolation();
    }

    /// <summary>Points the view (relative to the ship when aboard). For tests and screenshots.</summary>
    public void DebugSetLook(float yaw, float pitch)
    {
        _yaw = yaw;
        _pitch = pitch;
    }

    /// <summary>Sits the player in the pilot seat. For tests and screenshots.</summary>
    public void DebugSit()
    {
        if (CurrentMode != Mode.Aboard) DebugPlaceAboard(ShipLayout.SeatExitLocal, Vector3.Zero);
        SitDown();
    }

    // ---- Input helpers ----------------------------------------------------------------

    static bool Held(Key key) => Input.IsPhysicalKeyPressed(key);
    static float Axis(Key positive, Key negative) => (Held(positive) ? 1f : 0f) - (Held(negative) ? 1f : 0f);
    static Vector2 MoveInput() => new Vector2(Axis(Key.D, Key.A), Axis(Key.S, Key.W)).LimitLength(1f);
}
