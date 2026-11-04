using System.Collections.Generic;
using Godot;

namespace Coop;

/// <summary>
/// What the player asks the world to do. In co-op the host owns ships, so these are requests:
/// the host decides, and the answer comes back as replicated state.
/// </summary>
public interface IPlayerActions
{
    void RequestSeat(Ship ship, bool sit);
    void RequestGravity(Ship ship);
    void RequestStunt(Ship ship, bool on);
    void RequestZoneTransfer();
}

/// <summary>
/// A first-person player who can walk around inside a moving ship, fly it, or step off it
/// (from prototypes/walkable-ship).
///
/// Aboard, the player's physics body lives in the ship's cabin world and the camera is placed
/// at ship transform × that position, so the ship's motion never touches the character
/// controller. Stepping off moves the body into the zone's world, converting position, velocity
/// and view direction into the world frame (adding the ship's velocity at the door); stepping
/// on does the reverse. The camera and the visible body always stay in the zone's world.
///
/// In co-op this is only ever the local player. Its own movement is trusted, so it moves at
/// once; sitting, gravity and stunts go through <see cref="Actions"/> to the host.
/// </summary>
public partial class Player : CharacterBody3D
{
    public enum Mode { Outside, Aboard, Piloting }

    public Mode CurrentMode { get; private set; } = Mode.Aboard;
    public Ship Ship;                  // the ship you are aboard, or last boarded (null if none in this zone)
    public IReadOnlyList<Ship> Ships;  // every ship in your zone
    public Node3D MainWorld;           // the zone's world, where the body lives while outside
    public Camera3D Camera;
    public IPlayerActions Actions;
    public bool ThirdPerson;
    public Color BodyColor = ShipLayout.Yellow;

    /// <summary>Tests drive the player with this instead of the keyboard when set.</summary>
    public Vector2? ScriptedMove;

    const float WalkSpeed = 5.5f, SprintSpeed = 9f, CrouchSpeed = 2.5f, GroundAccel = 45f, AirAccel = 10f;
    const float JumpSpeed = 4.8f, Gravity = 9.8f;
    const float StandHeight = 1.8f, CrouchHeight = 1.1f;      // capsule, feet to top
    public const float HeadHeight = 1.6f, CrouchHeadHeight = 0.9f;   // eyes above the feet
    const float ZeroGSpeed = 5f, ZeroGAccel = 7f;
    const float JetpackThrust = 18f, JetpackFuelMax = 3f;
    const float MouseSensitivity = 0.0025f;
    const float SeatReach = 2.2f;

    float _yaw, _pitch;   // relative to the ship while aboard, to the world while outside
    Vector2 _mouse;
    float _jetFuel = JetpackFuelMax;
    float _eyeHeight = HeadHeight;
    CollisionShape3D _capsule;
    AvatarView _body;     // what third person sees; other players see the same model

    /// <summary>Position in the cabin world while aboard, which is the same as ship-local coordinates.</summary>
    public Vector3 ShipLocalPosition => GlobalPosition;

    /// <summary>Where your feet are in the zone's world.</summary>
    public Vector3 WorldPosition => CurrentMode == Mode.Outside || Ship == null ? GlobalPosition : Ship.GlobalTransform * FeetLocal;

    Vector3 FeetLocal => CurrentMode == Mode.Piloting ? ShipLayout.SeatLocal : ShipLocalPosition;

    public float Yaw => _yaw;
    public float Pitch => _pitch;
    public float JetpackFuel => _jetFuel / JetpackFuelMax;
    public bool Crouching { get; private set; }

    /// <summary>True while asking the host for the pilot seat.</summary>
    public bool WantsSeat { get; private set; }

    public override void _Ready()
    {
        _capsule = new CollisionShape3D
        {
            Name = "Capsule",
            Shape = new CapsuleShape3D { Radius = 0.35f, Height = StandHeight },
            Position = new Vector3(0, StandHeight / 2f, 0),
        };
        AddChild(_capsule);
        FloorMaxAngle = Mathf.DegToRad(50f);
        if (CurrentMode == Mode.Outside) SetOutsidePhysics();
        else SetAboardPhysics();
        _body = new AvatarView(BodyColor, null) { Name = "PlayerBody" };
        MainWorld.AddChild(_body); // a cabin world is never drawn
        if (DisplayServer.GetName() != "headless") Input.MouseMode = Input.MouseModeEnum.Captured;
    }


    /// <summary>
    /// Called by the game. While aboard, the body is inside a cabin world's SubViewport, which
    /// gets no input of its own.
    /// </summary>
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
                case Key.G: if (Ship != null) Actions?.RequestGravity(Ship); break;
                case Key.T: if (Ship != null) Actions?.RequestStunt(Ship, !Ship.StuntAutopilot); break;
                case Key.V: ThirdPerson = !ThirdPerson; break;
                case Key.R: ReturnAboard(); break;
                case Key.F6: Actions?.RequestZoneTransfer(); break;
            }
        }
    }

    public override void _PhysicsProcess(double delta)
    {
        float dt = (float)delta;
        Vector2 mouse = _mouse;
        _mouse = Vector2.Zero;

        if (CurrentMode != Mode.Outside && !IsInstanceValid(Ship)) ShipLost();

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
                if (Ships == null) break;
                foreach (Ship ship in Ships)
                {
                    if (!IsInstanceValid(ship)) continue;
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
        Crouch(false); // Ctrl means down in zero-g
        Vector2 move = MoveInput();
        float vertical = Axis(Key.Space, Key.Ctrl);
        Basis look = LookBasis();
        Vector3 wish = (look * new Vector3(move.X, 0, move.Y) + look.Y * vertical).LimitLength(1f) * ZeroGSpeed;
        Velocity = Velocity.MoveToward(wish, ZeroGAccel * dt);
        MoveAndSlide();
    }

    /// <summary>Crouch while asked to; stand up again only where there is headroom.</summary>
    void Crouch(bool want)
    {
        if (want) SetCrouching(true);
        else if (Crouching && !TestMove(GlobalTransform, Vector3.Up * (StandHeight - CrouchHeight)))
            SetCrouching(false);
    }

    /// <summary>Shrinks or grows the capsule from the top, so the feet stay put.</summary>
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

    /// <summary>Shoving a crate: the ship decides whether that happens here or on the host.</summary>
    void PushCargo()
    {
        for (int i = 0; i < GetSlideCollisionCount(); i++)
        {
            KinematicCollision3D hit = GetSlideCollision(i);
            if (hit.GetCollider() is RigidBody3D crate)
                Ship.PushCrate(Ship.CrateIndex(crate), -hit.GetNormal() * 1.5f);
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
        if (CurrentMode == Mode.Piloting)
        {
            StandUp();
            Actions?.RequestSeat(Ship, false);
        }
        else if (CurrentMode == Mode.Aboard && ShipLocalPosition.DistanceTo(ShipLayout.SeatLocal) < SeatReach)
        {
            WantsSeat = true;
            Actions?.RequestSeat(Ship, true);
        }
    }

    /// <summary>The host gave you the pilot seat.</summary>
    public void EnterSeat()
    {
        WantsSeat = false;
        if (CurrentMode != Mode.Aboard) return;
        CurrentMode = Mode.Piloting;
        CollisionLayer = 0;
        CollisionMask = 0;
        SetCrouching(false); // Ctrl means down in the seat
        Velocity = Vector3.Zero;
        GlobalPosition = ShipLayout.SeatLocal;
        _yaw = 0f;
        _pitch = 0f;
    }

    /// <summary>The host says someone else (or no one) is flying now.</summary>
    public void LeaveSeat()
    {
        WantsSeat = false;
        if (CurrentMode == Mode.Piloting) StandUp();
    }

    /// <summary>The host turned the seat request down.</summary>
    public void SeatDenied() => WantsSeat = false;

    void StandUp()
    {
        Ship.ThrustInput = Vector3.Zero;
        Ship.TurnInput = Vector3.Zero;
        Ship.Boost = false;
        PlaceAboard(ShipLayout.SeatExitLocal, Vector3.Zero);
    }

    // ---- Changing frames --------------------------------------------------------------

    /// <summary>Leave the ship: keep the ship's velocity at this point, plus your own.</summary>
    void StepOff()
    {
        Vector3 worldPos = Ship.GlobalTransform * ShipLocalPosition;
        Vector3 worldVel = Ship.PointVelocity(worldPos) + Ship.GlobalBasis * Velocity;
        SetLookFrom(Ship.GlobalBasis * (LookBasis() * Vector3.Forward));
        PlaceOutside(worldPos, worldVel);
    }

    /// <summary>Board a ship: subtract its velocity at this point. It becomes your ship.</summary>
    void StepOn(Ship ship, Vector3 local)
    {
        Basis toShip = ship.GlobalBasis.Inverse();
        Vector3 localVel = toShip * (Velocity - ship.PointVelocity(GlobalPosition));
        SetLookFrom(toShip * (LookBasis() * Vector3.Forward));
        Ship = ship;
        PlaceAboard(local, localVel);
    }

    /// <summary>Your ship vanished from under you (it left your zone): drop off where it was.</summary>
    void ShipLost()
    {
        WantsSeat = false;
        Ship = null;
        Vector3 at = _body != null ? _body.GlobalPosition : Vector3.Zero;
        PlaceOutside(at + Vector3.Up * 0.5f, Vector3.Zero);
    }

    void ReturnAboard()
    {
        if (CurrentMode == Mode.Piloting || !IsInstanceValid(Ship)) return;
        PlaceAboard(ShipLayout.SpawnLocal, Vector3.Zero);
    }

    /// <summary>Moves the body into the ship's cabin world, at a ship-local position.</summary>
    void PlaceAboard(Vector3 local, Vector3 localVelocity)
    {
        CurrentMode = Mode.Aboard;
        SetAboardPhysics();
        MoveTo(Ship.CabinWorld);
        GlobalPosition = local;
        Velocity = localVelocity;
        ResetInterpolation();
    }

    /// <summary>Moves the body into the zone's world, at a world position.</summary>
    void PlaceOutside(Vector3 worldPosition, Vector3 worldVelocity)
    {
        CurrentMode = Mode.Outside;
        SetOutsidePhysics();
        MoveTo(MainWorld);
        GlobalPosition = worldPosition;
        Velocity = worldVelocity;
        ResetInterpolation();
    }

    /// <summary>Reparents the body; it takes the physics space of the world it joins.</summary>
    void MoveTo(Node world)
    {
        if (GetParent() == world) return;
        if (GetParent() == null) world.AddChild(this);
        else Reparent(world, false);
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
        if (!IsInsideTree()) return;
        ResetPhysicsInterpolation();
        if (IsInstanceValid(Camera) && Camera.IsInsideTree()) Camera.ResetPhysicsInterpolation();
        if (IsInstanceValid(_body) && _body.IsInsideTree()) _body.ResetPhysicsInterpolation();
    }

    // ---- Zones ------------------------------------------------------------------------

    /// <summary>
    /// Moves you into another zone's world, standing outside at <paramref name="worldPosition"/>.
    /// The camera and your visible body move with you.
    /// </summary>
    public void EnterZone(Node3D world, IReadOnlyList<Ship> ships, Vector3 worldPosition, float yaw)
    {
        if (CurrentMode == Mode.Piloting) StandUp();
        WantsSeat = false;
        Ship = null;
        Ships = ships;
        MainWorld = world;
        if (Camera.GetParent() != world) Camera.Reparent(world, false);
        if (IsInstanceValid(_body) && _body.GetParent() != world) _body.Reparent(world, false);
        Camera.MakeCurrent();
        _yaw = yaw;
        _pitch = 0f;
        PlaceOutside(worldPosition, Vector3.Zero);
    }

    // ---- Camera and body --------------------------------------------------------------

    void PlaceCameraAndBody()
    {
        bool aboard = CurrentMode != Mode.Outside && Ship != null;
        Transform3D frame = aboard ? Ship.GlobalTransform : Transform3D.Identity;
        Vector3 feet = aboard ? FeetLocal : GlobalPosition;
        Vector3 eye = CurrentMode == Mode.Piloting ? ShipLayout.SeatEyeLocal : feet + Vector3.Up * _eyeHeight;

        var view = new Transform3D(LookBasis(), eye);
        if (ThirdPerson) view = view.TranslatedLocal(new Vector3(0, 0.4f, 3.6f));
        Camera.GlobalTransform = frame * view;

        _body.Place(frame, feet, _yaw, _pitch, _eyeHeight / HeadHeight);
        _body.Visible = ThirdPerson;
    }

    // ---- Spawning ---------------------------------------------------------------------

    /// <summary>Puts a new player aboard <paramref name="ship"/>. Set MainWorld, Camera and Ships first.</summary>
    public void SpawnAboard(Ship ship, Vector3 local)
    {
        Ship = ship;
        PlaceAboard(local, Vector3.Zero);
    }

    /// <summary>Puts a new player outside, in MainWorld. Set MainWorld, Camera and Ships first.</summary>
    public void SpawnOutside(Vector3 worldPosition, float yaw)
    {
        _yaw = yaw;
        PlaceOutside(worldPosition, Vector3.Zero);
    }

    // ---- Test hooks -------------------------------------------------------------------

    /// <summary>Puts the player aboard a ship at a ship-local position and velocity. For tests.</summary>
    public void DebugPlaceAboard(Ship ship, Vector3 local, Vector3 localVelocity)
    {
        if (CurrentMode == Mode.Piloting) StandUp();
        Ship = ship;
        PlaceAboard(local, localVelocity);
    }

    /// <summary>Puts the player outside at a world position, standing still. For tests.</summary>
    public void DebugPlaceOutside(Vector3 worldPosition)
    {
        if (CurrentMode == Mode.Piloting) StandUp();
        PlaceOutside(worldPosition, Vector3.Zero);
    }

    /// <summary>Points the view (relative to the ship when aboard). For tests and screenshots.</summary>
    public void DebugSetLook(float yaw, float pitch)
    {
        _yaw = yaw;
        _pitch = pitch;
    }

    // ---- Input helpers ----------------------------------------------------------------

    static bool Held(Key key) => Input.IsPhysicalKeyPressed(key);
    static float Axis(Key positive, Key negative) => (Held(positive) ? 1f : 0f) - (Held(negative) ? 1f : 0f);
    Vector2 MoveInput() => ScriptedMove ?? new Vector2(Axis(Key.D, Key.A), Axis(Key.S, Key.W)).LimitLength(1f);
}
