using System;
using System.Collections.Generic;
using Godot;

namespace Saves;

/// <summary>
/// The first-person player from prototypes/walkable-ship: walks inside a moving ship, flies it,
/// or steps off it.
///
/// Aboard, the player's physics body lives in the ship's cabin world and the camera is placed at
/// ship transform × that position, so the ship's motion (and its zone crossings) never touch the
/// character controller. Stepping off moves the body into the zone's world.
///
/// Changes for this prototype: the camera and the visible body follow the player into whichever
/// zone they are in (<see cref="MainWorld"/>); the G and T keys are gone; there is a look ray for
/// building and picking things up; and the seat can be refused while carrying a crate.
/// </summary>
public partial class Player : CharacterBody3D
{
    public enum Mode { Outside, Aboard, Piloting }

    public Mode CurrentMode { get; private set; } = Mode.Aboard;
    public string RecordId { get; init; }
    public Ship Ship;                                  // the ship you are aboard, or last boarded
    public IReadOnlyList<Ship> Ships = Array.Empty<Ship>(); // ships in your zone, which you can board
    public Camera3D Camera { get; private set; }
    public bool ThirdPerson;

    /// <summary>Asked before sitting down; returns why not, or null.</summary>
    public Func<string> SeatRefusal;

    /// <summary>Raised with a short message the HUD should show.</summary>
    public event Action<string> Said;

    const float WalkSpeed = 5.5f, SprintSpeed = 9f, CrouchSpeed = 2.5f, GroundAccel = 45f, AirAccel = 10f;
    const float JumpSpeed = 4.8f, Gravity = 9.8f;
    const float StandHeight = 1.8f, CrouchHeight = 1.1f;      // capsule, feet to top
    const float HeadHeight = 1.6f, CrouchHeadHeight = 0.9f;   // eyes above the feet
    const float ZeroGSpeed = 5f, ZeroGAccel = 7f;
    const float JetpackThrust = 18f, JetpackFuelMax = 3f;
    const float MouseSensitivity = 0.0025f;

    float _yaw, _pitch;   // relative to the ship while aboard, to the zone while outside
    Vector2 _mouse;
    float _jetFuel = JetpackFuelMax;
    float _eyeHeight = HeadHeight;
    CollisionShape3D _capsule;
    Node3D _body;         // what third person (and later, other players) see
    Node3D _mainWorld;

    /// <summary>
    /// The zone the player is in: where the body lives while outside, and where the camera and
    /// visible body always are (a cabin world is never drawn). Setting it moves them there.
    /// </summary>
    public Node3D MainWorld
    {
        get => _mainWorld;
        set
        {
            _mainWorld = value;
            MoveVisual(Camera);
            MoveVisual(_body);
            if (Camera != null && Camera.IsInsideTree()) Camera.MakeCurrent();
        }
    }

    /// <summary>Position in the cabin world while aboard, which is the same as ship-local coordinates.</summary>
    public Vector3 ShipLocalPosition => GlobalPosition;

    public float JetpackFuel => _jetFuel / JetpackFuelMax;
    public bool Crouching { get; private set; }
    public float Yaw => _yaw;

    public Player()
    {
        Camera = new Camera3D { Name = "PlayerCamera", Fov = 80f, Near = 0.05f, Far = 20000f };
        _capsule = new CollisionShape3D
        {
            Name = "Capsule",
            Shape = new CapsuleShape3D { Radius = 0.35f, Height = StandHeight },
            Position = new Vector3(0, StandHeight / 2f, 0),
        };
        AddChild(_capsule);
        FloorMaxAngle = Mathf.DegToRad(50f);
        BuildBody();
    }

    public override void _Ready()
    {
        if (CurrentMode == Mode.Outside) SetOutsidePhysics();
        else SetAboardPhysics();
        MainWorld = _mainWorld; // make sure the camera and body are where they belong
        Input.MouseMode = Input.MouseModeEnum.Captured;
    }

    public override void _ExitTree()
    {
        // The camera and body hang off a zone, not off the player: when the player is freed, so are they.
        if (IsQueuedForDeletion())
        {
            Camera?.QueueFree();
            _body?.QueueFree();
        }
    }

    /// <summary>Called by the main scene: a cabin world or zone gets no input of its own.</summary>
    public void HandleInput(InputEvent e)
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
                if (!ShipLayout.StillAboard(ShipLocalPosition)) StepOff();
                break;

            case Mode.Outside:
                Look(mouse);
                Walk(dt, outside: true);
                foreach (Ship ship in Ships)
                {
                    if (!ship.IsInsideTree() || ship.GetParent() != _mainWorld) continue;
                    Vector3 local = ship.GlobalTransform.AffineInverse() * GlobalPosition;
                    if (!ShipLayout.ShouldBoard(local)) continue;
                    StepOn(ship, local);
                    break;
                }
                break;
        }

        _eyeHeight = Mathf.MoveToward(_eyeHeight, Crouching ? CrouchHeadHeight : HeadHeight, 5f * dt);
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
        Crouch(onFloor && Held(Key.Ctrl)); // in the air, Ctrl is left alone

        Vector2 move = MoveInput();
        float speed = Crouching ? CrouchSpeed : Held(Key.Shift) ? SprintSpeed : WalkSpeed;
        Vector3 wish = new Basis(Vector3.Up, _yaw) * new Vector3(move.X, 0, move.Y) * speed;
        if (onFloor)
        {
            v.X = Mathf.MoveToward(v.X, wish.X, GroundAccel * dt);
            v.Z = Mathf.MoveToward(v.Z, wish.Z, GroundAccel * dt);
        }
        else if (move != Vector2.Zero)
        {
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

    void Float(float dt)
    {
        MotionMode = MotionModeEnum.Floating;
        Crouch(false);
        Vector2 move = MoveInput();
        float vertical = Axis(Key.Space, Key.Ctrl);
        Basis look = LookBasis();
        Vector3 wish = (look * new Vector3(move.X, 0, move.Y) + look.Y * vertical).LimitLength(1f) * ZeroGSpeed;
        Velocity = Velocity.MoveToward(wish, ZeroGAccel * dt);
        MoveAndSlide();
    }

    void Crouch(bool want)
    {
        if (want) SetCrouching(true);
        else if (Crouching && !TestMove(GlobalTransform, Vector3.Up * (StandHeight - CrouchHeight)))
            SetCrouching(false);
    }

    void SetCrouching(bool crouching)
    {
        if (crouching == Crouching) return;
        Crouching = crouching;
        float height = crouching ? CrouchHeight : StandHeight;
        ((CapsuleShape3D)_capsule.Shape).Height = height;
        _capsule.Position = new Vector3(0, height / 2f, 0);
    }

    void Look(Vector2 mouse)
    {
        _yaw -= mouse.X * MouseSensitivity;
        _pitch = Mathf.Clamp(_pitch - mouse.Y * MouseSensitivity, -1.5f, 1.5f);
    }

    Basis LookBasis() => new Basis(Vector3.Up, _yaw) * new Basis(Vector3.Right, _pitch);

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
        else if (CurrentMode == Mode.Aboard && ShipLocalPosition.DistanceTo(ShipLayout.SeatLocal) < 2.2f)
        {
            string refusal = SeatRefusal?.Invoke();
            if (refusal != null) Said?.Invoke(refusal);
            else SitDown();
        }
    }

    void SitDown()
    {
        CurrentMode = Mode.Piloting;
        CollisionLayer = 0;
        CollisionMask = 0;
        SetCrouching(false);
        Velocity = Vector3.Zero;
        GlobalPosition = ShipLayout.SeatLocal;
        _yaw = 0f;
        _pitch = 0f;
        Ship.Piloted = true;
    }

    void StandUp()
    {
        Ship.Piloted = false;
        Ship.ThrustInput = Vector3.Zero;
        Ship.TurnInput = Vector3.Zero;
        Ship.Boost = false;
        PlaceAboard(ShipLayout.SeatExitLocal, Vector3.Zero);
    }

    // ---- Changing frames --------------------------------------------------------------

    /// <summary>Raised after stepping on or off a ship, so the game can move the player's record.</summary>
    public event Action FrameChanged;

    void StepOff()
    {
        Vector3 worldPos = Ship.GlobalTransform * ShipLocalPosition;
        Vector3 worldVel = Ship.PointVelocity(worldPos) + Ship.GlobalBasis * Velocity;
        SetLookFrom(Ship.GlobalBasis * (LookBasis() * Vector3.Forward));
        PlaceOutside(worldPos, worldVel);
        FrameChanged?.Invoke();
    }

    void StepOn(Ship ship, Vector3 local)
    {
        Basis toShip = ship.GlobalBasis.Inverse();
        Vector3 localVel = toShip * (Velocity - ship.PointVelocity(GlobalPosition));
        SetLookFrom(toShip * (LookBasis() * Vector3.Forward));
        Ship = ship;
        PlaceAboard(local, localVel);
        FrameChanged?.Invoke();
    }

    void ReturnAboard()
    {
        if (CurrentMode == Mode.Piloting || Ship == null || Ship.GetParent() != _mainWorld) return;
        bool wasOutside = CurrentMode == Mode.Outside;
        PlaceAboard(ShipLayout.SpawnLocal, Vector3.Zero);
        if (wasOutside) FrameChanged?.Invoke();
    }

    void PlaceAboard(Vector3 local, Vector3 localVelocity)
    {
        CurrentMode = Mode.Aboard;
        SetAboardPhysics();
        MoveTo(Ship.CabinWorld);
        GlobalPosition = local;
        Velocity = localVelocity;
        ResetInterpolation();
    }

    void PlaceOutside(Vector3 worldPosition, Vector3 worldVelocity)
    {
        CurrentMode = Mode.Outside;
        SetOutsidePhysics();
        MoveTo(_mainWorld);
        GlobalPosition = worldPosition;
        Velocity = worldVelocity;
        ResetInterpolation();
    }

    void MoveTo(Node world)
    {
        Node parent = GetParent();
        if (parent == world) return;
        if (parent == null) world.AddChild(this);
        else Reparent(world, false);
    }

    void MoveVisual(Node3D visual)
    {
        if (visual == null || _mainWorld == null || visual.GetParent() == _mainWorld) return;
        if (visual.GetParent() == null) _mainWorld.AddChild(visual);
        else visual.Reparent(_mainWorld, false);
        visual.ResetPhysicsInterpolation();
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
        if (!Camera.IsInsideTree()) return;
        Transform3D frame = CurrentMode == Mode.Outside ? Transform3D.Identity : Ship.GlobalTransform;
        Vector3 feet = CurrentMode switch
        {
            Mode.Outside => GlobalPosition,
            Mode.Piloting => ShipLayout.SeatLocal,
            _ => ShipLocalPosition,
        };
        Vector3 eye = CurrentMode == Mode.Piloting ? ShipLayout.SeatEyeLocal : feet + Vector3.Up * _eyeHeight;

        var view = new Transform3D(LookBasis(), eye);
        if (ThirdPerson) view = view.TranslatedLocal(CurrentMode == Mode.Piloting ? new Vector3(0, 9f, 44f) : new Vector3(0, 0.4f, 3.6f));
        Camera.GlobalTransform = frame * view;

        var squash = Basis.FromScale(new Vector3(1f, _eyeHeight / HeadHeight, 1f));
        _body.GlobalTransform = frame * new Transform3D(new Basis(Vector3.Up, _yaw) * squash, feet);
        _body.Visible = ThirdPerson && CurrentMode != Mode.Piloting;
    }

    void BuildBody()
    {
        _body = new Node3D { Name = "PlayerBody" };
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

    // ---- Look ray, for building and picking things up -----------------------------------

    /// <summary>
    /// Where the player is looking, in the world their body is in: the cabin world (ship-local
    /// coordinates, with <c>Frame</c> the ship) when aboard, the zone's world otherwise.
    /// </summary>
    public (PhysicsDirectSpaceState3D Space, Vector3 From, Vector3 Direction, Ship Frame) LookRay()
    {
        Vector3 feet = CurrentMode == Mode.Piloting ? ShipLayout.SeatLocal : GlobalPosition;
        Vector3 eye = CurrentMode == Mode.Piloting ? ShipLayout.SeatEyeLocal : feet + Vector3.Up * _eyeHeight;
        return (GetWorld3D().DirectSpaceState, eye, LookBasis() * Vector3.Forward, CurrentMode == Mode.Outside ? null : Ship);
    }

    // ---- Placement, for spawning from records and for tests ------------------------------

    /// <summary>Puts the player aboard a ship at a ship-local position, facing <paramref name="yaw"/>.</summary>
    public void SpawnAboard(Ship ship, Vector3 local, float yaw)
    {
        if (CurrentMode == Mode.Piloting) StandUp();
        Ship = ship;
        _yaw = yaw;
        _pitch = 0f;
        PlaceAboard(local, Vector3.Zero);
    }

    /// <summary>Puts the player outside in the current zone, standing still.</summary>
    public void SpawnOutside(Vector3 position, float yaw)
    {
        if (CurrentMode == Mode.Piloting) StandUp();
        _yaw = yaw;
        _pitch = 0f;
        PlaceOutside(position, Vector3.Zero);
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
        if (CurrentMode != Mode.Aboard) PlaceAboard(ShipLayout.SeatExitLocal, Vector3.Zero);
        SitDown();
    }

    // ---- Input helpers ----------------------------------------------------------------

    static bool Held(Key key) => Input.IsPhysicalKeyPressed(key);
    static float Axis(Key positive, Key negative) => (Held(positive) ? 1f : 0f) - (Held(negative) ? 1f : 0f);
    static Vector2 MoveInput() => new Vector2(Axis(Key.D, Key.A), Axis(Key.S, Key.W)).LimitLength(1f);
}
