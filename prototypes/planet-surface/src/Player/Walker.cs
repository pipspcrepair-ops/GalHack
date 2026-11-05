using System;
using Godot;
using PlanetSurface.World;
using Weather.Forces;
using WaterForces = GalHack.Physics.WaterForces;
using WaterRules = GalHack.Physics.PhysicsRules.WaterData;

namespace PlanetSurface.Player;

/// <summary>
/// The survivor on foot, with terrain-digging's feel: walk, sprint, jump, a small jetpack for
/// climbing out of pits and up cliffs, and crouch on Ctrl. Crouching shrinks the capsule and
/// lowers the eye; standing back up waits until there's room overhead, so an overhang can hold
/// you down but never trap you inside it.
///
/// The weather pushes it through the weather prototype's force module: wind as drag on the
/// body (crouching halves it), tornado vortices that catch, carry and fling.
///
/// Water is a volume it goes into (seas and rivers, <see cref="WaterAt"/>), under the shared
/// physics module's water rules: buoyancy and drag on the share of it under the surface, wading
/// in the shallows and swimming once deep enough. Swimming, the move keys stroke along the
/// heading and Space and Ctrl go up and down; left alone it floats up with its head out.
/// </summary>
public partial class Walker : CharacterBody3D, IForceBody
{
    public const float StandHeight = 1.8f, CrouchHeight = 0.9f, Radius = 0.35f;
    const float StandEye = 1.62f, CrouchEye = 0.72f;
    const float WalkSpeed = 5.5f, SprintSpeed = 9f, CrouchSpeed = 2.4f;
    const float GroundAccel = 45f, AirAccel = 10f;
    const float JumpSpeed = 5.4f, Gravity = 13f;
    const float JetpackThrust = 24f, JetpackFuelMax = 3f;
    const float MouseSensitivity = 0.0025f;

    public Camera3D Camera;
    public bool ControlsEnabled = true;
    public bool CrouchOverride;

    /// <summary>Tests can walk without a keyboard: a move direction in the player's own frame (x right, y back).</summary>
    public Vector2? MoveOverride;

    /// <summary>The weather slows walking in deep snow, sand or a storm.</summary>
    public float MoveFactor = 1f;

    /// <summary>Tests can swim without a keyboard: up (+1) or down (-1), in place of Space and Ctrl.</summary>
    public float? VerticalOverride;

    /// <summary>The water surface over a point's column (sea, river), or null on dry ground. Main sets it from the planet.</summary>
    public Func<Vector3, float?> WaterAt;

    /// <summary>The shared physics module's water numbers (shared/Physics/physics.json). Main loads them.</summary>
    public WaterRules Water = new();

    /// <summary>The share of the body under water this tick.</summary>
    public float WaterShare { get; private set; }
    public bool Swimming { get; private set; }

    /// <summary>Head under water: the vitals hook. The suit breathes its own oxygen while this is set.</summary>
    public bool Underwater => WaterShare >= 1f;

    public bool IsCrouching { get; private set; }
    public float CurrentHeight => IsCrouching ? CrouchHeight : StandHeight;
    public float JetpackFuel => _jetFuel / JetpackFuelMax;
    public Vector3 EyePosition => GlobalPosition + Vector3.Up * _eyeHeight;
    public Vector3 LookDirection => LookBasis() * Vector3.Forward;
    public float Yaw => _yaw;
    public float Pitch => _pitch;

    float _yaw, _pitch, _eyeHeight = StandEye;
    float _kick, _kickVelocity, _shake;
    float _jetFuel = JetpackFuelMax;
    Vector2 _mouse;
    Vector3 _externalForce, _externalImpulse;
    CapsuleShape3D _capsule;
    CollisionShape3D _shape;
    CapsuleShape3D _standProbe;
    readonly RandomNumberGenerator _rng = new() { Seed = 7 };

    public override void _Ready()
    {
        _capsule = new CapsuleShape3D { Radius = Radius, Height = StandHeight };
        _shape = new CollisionShape3D { Shape = _capsule, Position = new Vector3(0, StandHeight / 2f, 0) };
        AddChild(_shape);
        _standProbe = new CapsuleShape3D { Radius = Radius - 0.03f, Height = StandHeight - 0.1f };
        CollisionLayer = Layers.Player;
        CollisionMask = Layers.Solid | Layers.Props;
        FloorMaxAngle = Mathf.DegToRad(50f);
        FloorSnapLength = 0.35f;
        FloorConstantSpeed = true;
    }

    public override void _UnhandledInput(InputEvent e)
    {
        if (!ControlsEnabled) return;
        if (e is InputEventMouseMotion motion && Input.MouseMode == Input.MouseModeEnum.Captured) _mouse += motion.Relative;
    }

    public override void _PhysicsProcess(double delta)
    {
        float dt = (float)delta;
        Vector2 mouse = _mouse;
        _mouse = Vector2.Zero;
        _yaw -= mouse.X * MouseSensitivity;
        _pitch = Mathf.Clamp(_pitch - mouse.Y * MouseSensitivity, -1.5f, 1.5f);

        UpdateWater();
        UpdateCrouch(!Swimming && (CrouchOverride || (ControlsEnabled && Held(Key.Ctrl))));
        if (Swimming) Swim(dt);
        else Walk(dt);
        PlaceCamera(dt);
    }

    /// <summary>How deep in water the body is, and whether that is deep enough to swim (wading again below the wade depth).</summary>
    void UpdateWater()
    {
        float? surface = WaterAt?.Invoke(GlobalPosition);
        WaterShare = surface is float s ? WaterForces.SubmergedFraction(GlobalPosition.Y, CurrentHeight, s) : 0f;
        bool swimming = WaterShare > 0f && (Swimming ? WaterShare > Water.WadeDepth : WaterShare >= Water.SwimDepth);
        if (swimming == Swimming) return;
        Swimming = swimming;
        // Swimming there is no floor to snap to; wading and walking there is.
        MotionMode = swimming ? MotionModeEnum.Floating : MotionModeEnum.Grounded;
        if (swimming && IsCrouching && CanStand()) SetCrouch(false);
    }

    /// <summary>The water's own forces on the body this tick (buoyancy and drag on the share under), as acceleration.</summary>
    Vector3 WaterAccel(Vector3 v, float dt)
    {
        if (WaterShare <= 0f) return Vector3.Zero;
        float rho = Water.DensityOf(GalHack.Physics.BodyKind.Player);
        var g = new System.Numerics.Vector3(0f, -Gravity, 0f);
        System.Numerics.Vector3 f = WaterForces.Buoyancy(Mass, rho, WaterShare, g, Water)
            + WaterForces.Drag(Mass, DragArea, new System.Numerics.Vector3(v.X, v.Y, v.Z), WaterShare, Water, dt);
        return new Vector3(f.X, f.Y, f.Z) / Mass;
    }

    /// <summary>
    /// Swimming: strokes reach the wished velocity, holding against weight, buoyancy, drag and wind
    /// as far as the swimmer's strength allows (the shared WalkController's rule). Without a wish
    /// there are no strokes, and the water has its way: lighter than water, the body floats up.
    /// </summary>
    void Swim(float dt)
    {
        Vector3 v = Velocity;
        Vector3 outside = Vector3.Down * Gravity + WaterAccel(v, dt) + _externalForce / Mass; // m/s²
        Vector2 move = MoveOverride ?? (ControlsEnabled ? MoveInput() : Vector2.Zero);
        float vertical = VerticalOverride ?? (ControlsEnabled ? Axis(Key.Space, Key.Ctrl) : 0f);
        Vector3 wish = (new Basis(Vector3.Up, _yaw) * new Vector3(move.X, 0f, move.Y) + Vector3.Up * vertical).LimitLength(1f);
        Vector3 accel = outside;
        if (wish.LengthSquared() > 1e-6f)
        {
            float speed = Held(Key.Shift) && ControlsEnabled ? Water.SwimSprintSpeed : Water.SwimSpeed;
            Vector3 next = v.MoveToward(wish * speed, Water.SwimAccel * dt);
            accel += ((next - v) / dt - outside).LimitLength(Water.SwimGrip);
        }
        v += accel * dt + _externalImpulse / Mass;
        _externalForce = Vector3.Zero;
        _externalImpulse = Vector3.Zero;
        Velocity = v;
        MoveAndSlide();
    }

    void Walk(float dt)
    {
        Vector3 v = Velocity;
        bool onFloor = IsOnFloor();
        if (onFloor) _jetFuel = Mathf.MoveToward(_jetFuel, JetpackFuelMax, dt);
        else v.Y -= Gravity * dt;

        Vector2 move = MoveOverride ?? (ControlsEnabled ? MoveInput() : Vector2.Zero);
        float speed = (IsCrouching ? CrouchSpeed : Held(Key.Shift) && ControlsEnabled ? SprintSpeed : WalkSpeed) * MoveFactor;
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

        // Weather: wind drag and vortex pulls from the force module, as acceleration.
        v += _externalForce / Mass * dt + _externalImpulse / Mass;

        // Wading: the water drags at the body; buoyancy only lightens it while its feet are down.
        Vector3 water = WaterAccel(v, dt);
        if (onFloor) water.Y = 0f;
        v += water * dt;
        _externalForce = Vector3.Zero;
        _externalImpulse = Vector3.Zero;

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

    /// <summary>Whether a standing capsule would fit here right now.</summary>
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

    /// <summary>A small upward kick (degrees) and optional shake, for digging, blasts and thunder.</summary>
    public void Kick(float degrees, float shake = 0f)
    {
        _kickVelocity += Mathf.DegToRad(degrees) * 18f;
        _shake = Mathf.Max(_shake, shake);
    }

    void PlaceCamera(float dt)
    {
        if (Camera == null) return;
        _eyeHeight = Mathf.MoveToward(_eyeHeight, IsCrouching ? CrouchEye : StandEye, 5f * dt);
        _kickVelocity += (-_kick * 420f - _kickVelocity * 30f) * dt;
        _kick += _kickVelocity * dt;
        _shake = Mathf.MoveToward(_shake, 0f, dt * 1.8f);
        float jitter = _shake * _shake;
        var shake = new Vector3(_rng.RandfRange(-1f, 1f), _rng.RandfRange(-1f, 1f), 0f) * jitter * 0.05f;
        Basis view = new Basis(Vector3.Up, _yaw + shake.X) * new Basis(Vector3.Right, _pitch + _kick + shake.Y);
        Camera.GlobalTransform = new Transform3D(view, EyePosition);
    }

    Basis LookBasis() => new Basis(Vector3.Up, _yaw) * new Basis(Vector3.Right, _pitch);

    /// <summary>Moves the player (stuck rescue, load, seams, tests), keeping the view.</summary>
    public void Teleport(Vector3 feet, bool keepVelocity = false)
    {
        GlobalPosition = feet;
        if (!keepVelocity) Velocity = Vector3.Zero;
        ResetPhysicsInterpolation();
        Camera?.ResetPhysicsInterpolation();
    }

    public void SetLook(float yaw, float pitch)
    {
        _yaw = yaw;
        _pitch = pitch;
    }

    public void ForceCrouch() => SetCrouch(true);

    // ---- The weather's force module ----

    public float Mass => global::Weather.Specs.PlayerMass;
    public float DragArea => IsCrouching ? global::Weather.Specs.PlayerCrouchDragArea : global::Weather.Specs.PlayerDragArea;
    Vector3 IForceBody.Position => GlobalPosition + Vector3.Up * 0.9f;
    Vector3 IForceBody.Velocity => Velocity;
    public void ApplyForce(Vector3 force) => _externalForce += force;
    public void ApplyImpulse(Vector3 impulse) => _externalImpulse += impulse;

    static bool Held(Key key) => Input.IsPhysicalKeyPressed(key);
    static float Axis(Key positive, Key negative) => (Held(positive) ? 1f : 0f) - (Held(negative) ? 1f : 0f);
    static Vector2 MoveInput() => new Vector2(Axis(Key.D, Key.A), Axis(Key.S, Key.W)).LimitLength(1f);
}
