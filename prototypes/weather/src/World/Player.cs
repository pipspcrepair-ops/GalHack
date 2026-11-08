using System;
using Godot;
using Weather.Forces;

namespace Weather;

/// <summary>
/// First-person player: walk, sprint, jump, crouch, jetpack, health and suit power. The
/// weather reaches the player only through the force module (wind, vortex) and a few numbers
/// the director sets each tick (movement factor, exposure, solar), so the controller stays a
/// plain controller.
/// </summary>
public partial class Player : CharacterBody3D, IForceBody
{
    const float WalkSpeed = 5.5f, SprintSpeed = 9f, CrouchSpeed = 2.5f, GroundAccel = 45f, AirAccel = 10f;
    const float JumpSpeed = 4.8f, Gravity = 9.8f;
    const float StandHeight = 1.8f, CrouchHeight = 1.1f, HeadHeight = 1.6f, CrouchHeadHeight = 0.9f;
    const float JetpackThrust = 18f, JetpackFuelMax = 3f;
    const float MouseSensitivity = 0.0025f;

    /// <summary>On the ground, wind shoves you by this many seconds of its acceleration: you lean into it, you don't fly.</summary>
    const float WindGrip = 0.35f;

    public Camera3D Camera;
    public ForceModule Forces;
    public Vector3 Spawn;
    public bool ThirdPerson;

    public Health Health { get; } = new(100f);
    public float SuitPower { get; private set; } = 1f;
    public float JetpackFuel => _jetFuel / JetpackFuelMax;
    public bool Crouching { get; private set; }
    public float RespawnIn { get; private set; }

    // Set by the weather every tick.
    public float MoveFactor = 1f;
    public float Exposure;   // extra suit drain, % per second
    public float Solar = 1f;

    /// <summary>Camera shake, decays by itself. The weather adds to it.</summary>
    public float Shake;

    /// <summary>Raised with the damage dealt when a landing hurts.</summary>
    public event Action<float, bool> Landed;

    float _yaw, _pitch;
    Vector2 _mouse;
    float _jetFuel = JetpackFuelMax;
    float _eyeHeight = HeadHeight;
    Vector3 _external;   // this tick's weather acceleration
    CollisionShape3D _capsule;
    Node3D _body;
    float _time;

    float IForceBody.Mass => Specs.PlayerMass;
    public float DragArea => Crouching ? Specs.PlayerCrouchDragArea : Specs.PlayerDragArea;
    Vector3 IForceBody.Position => GlobalPosition + Vector3.Up * 0.9f;
    Vector3 IForceBody.Velocity => Velocity;
    void IForceBody.ApplyForce(Vector3 force) => _external += force / Specs.PlayerMass;
    void IForceBody.ApplyImpulse(Vector3 impulse) => Velocity += impulse / Specs.PlayerMass;

    public bool Caught => Forces?.PhaseOf(this) == VortexPhase.Caught;

    /// <summary>The top of your head, where lightning would find you.</summary>
    public Vector3 HeadTop => GlobalPosition + Vector3.Up * (Crouching ? CrouchHeight : StandHeight);

    public override void _Ready()
    {
        _capsule = new CollisionShape3D
        {
            Name = "Capsule",
            Shape = new CapsuleShape3D { Radius = 0.35f, Height = StandHeight },
            Position = new Vector3(0, StandHeight / 2f, 0),
        };
        AddChild(_capsule);
        CollisionLayer = Layers.Player;
        CollisionMask = Layers.World | Layers.Vehicles;
        FloorMaxAngle = Mathf.DegToRad(50f);
        FloorSnapLength = 0.4f;
        BuildBody();
        if (!Engine.IsEditorHint() && DisplayServer.GetName() != "headless") Input.MouseMode = Input.MouseModeEnum.Captured;
    }

    public void HandleInput(InputEvent e)
    {
        if (e is InputEventMouseMotion motion && Input.MouseMode == Input.MouseModeEnum.Captured)
            _mouse += motion.Relative;
        else if (e is InputEventMouseButton { Pressed: true } && Input.MouseMode != Input.MouseModeEnum.Captured)
            Input.MouseMode = Input.MouseModeEnum.Captured;
        else if (e is InputEventKey { Pressed: true, Echo: false } key)
        {
            if (key.PhysicalKeycode == Key.Escape) Input.MouseMode = Input.MouseModeEnum.Visible;
            if (key.PhysicalKeycode == Key.V) ThirdPerson = !ThirdPerson;
        }
    }

    public override void _PhysicsProcess(double delta)
    {
        float dt = (float)delta;
        _time += dt;
        Vector2 mouse = _mouse;
        _mouse = Vector2.Zero;
        _yaw -= mouse.X * MouseSensitivity;
        _pitch = Mathf.Clamp(_pitch - mouse.Y * MouseSensitivity, -1.5f, 1.5f);

        if (Health.Dead)
        {
            RespawnIn -= dt;
            if (RespawnIn <= 0f) Respawn();
            _external = Vector3.Zero;
            PlaceCamera(dt);
            return;
        }

        Move(dt);
        UpdateSuit(dt);
        Health.Tick(dt);
        PlaceCamera(dt);
    }

    void Move(float dt)
    {
        Vector3 v = Velocity;
        bool onFloor = IsOnFloor();
        bool caught = Caught;
        Vector3 push = _external;
        _external = Vector3.Zero;

        if (onFloor && !caught) _jetFuel = Mathf.MoveToward(_jetFuel, JetpackFuelMax, dt);
        Crouch(onFloor && Held(Key.Ctrl));

        Vector2 move = MoveInput();
        float speed = (Crouching ? CrouchSpeed : Held(Key.Shift) ? SprintSpeed : WalkSpeed) * MoveFactor;
        Vector3 wish = new Basis(Vector3.Up, _yaw) * new Vector3(move.X, 0, move.Y) * speed;

        if (caught)
        {
            // In the vortex you have no say: the force module carries your weight and spins you.
            v += (push + Vector3.Down * Gravity) * dt;
        }
        else if (onFloor && push.Y < Gravity * 0.5f)
        {
            // Standing, you lean into the wind: it shifts your footing rather than launching you.
            Vector3 target = wish + new Vector3(push.X, 0, push.Z) * WindGrip;
            v.X = Mathf.MoveToward(v.X, target.X, GroundAccel * dt);
            v.Z = Mathf.MoveToward(v.Z, target.Z, GroundAccel * dt);
        }
        else
        {
            v += push * dt;
            v.Y -= Gravity * dt;
            if (move != Vector2.Zero)
            {
                Vector3 horizontal = new(v.X, 0, v.Z);
                Vector3 steered = horizontal + wish.Normalized() * AirAccel * dt;
                if (steered.Length() > Mathf.Max(horizontal.Length(), speed))
                    steered = steered.LimitLength(Mathf.Max(horizontal.Length(), speed));
                v.X = steered.X;
                v.Z = steered.Z;
            }
        }

        if (Held(Key.Space) && !caught)
        {
            if (onFloor) v.Y = JumpSpeed;
            else if (_jetFuel > 0f && v.Y < 8f)
            {
                v.Y += JetpackThrust * dt;
                _jetFuel -= dt;
            }
        }

        float fallSpeed = -v.Y;
        Velocity = v;
        MoveAndSlide();

        if (!onFloor && IsOnFloor()) Land(fallSpeed);
    }

    /// <summary>Touching down: fall damage, a tornado's fling or not.</summary>
    void Land(float speed)
    {
        bool flung = Forces?.PhaseOf(this) == VortexPhase.Released;
        float dealt = Health.Hit(DamageRules.FallDamage(speed));
        if (dealt > 0f)
        {
            Shake += Mathf.Clamp(dealt / 20f, 0.3f, 2f);
            Landed?.Invoke(dealt, flung);
        }
        if (Health.Dead) Die();
    }

    /// <summary>Lightning, debris: a direct hit from the weather.</summary>
    public float Hurt(float damage)
    {
        float dealt = Health.Hit(damage);
        Shake += 1.5f;
        if (Health.Dead) Die();
        return dealt;
    }

    void Die()
    {
        RespawnIn = 4f;
        Velocity = Vector3.Zero;
    }

    void Respawn()
    {
        Health.Set(Health.Max);
        SuitPower = 1f;
        GlobalPosition = Spawn;
        Velocity = Vector3.Zero;
        ResetPhysicsInterpolation();
    }

    /// <summary>Suit power runs the heater and cooler: exposure drains it, solar panels top it up. Empty, exposure hurts.</summary>
    void UpdateSuit(float dt)
    {
        float drain = (0.15f + Exposure) / 100f;
        float charge = Solar * 0.45f / 100f;
        SuitPower = Mathf.Clamp(SuitPower + (charge - drain) * dt, 0f, 1f);
        if (SuitPower <= 0f && Exposure > 0f) Health.Hit(Exposure * 0.8f * dt);
        if (Health.Dead) Die();
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
        float height = crouching ? CrouchHeight : StandHeight;
        ((CapsuleShape3D)_capsule.Shape).Height = height;
        _capsule.Position = new Vector3(0, height / 2f, 0);
    }

    Basis LookBasis() => new Basis(Vector3.Up, _yaw) * new Basis(Vector3.Right, _pitch);

    public Vector3 LookDirection => LookBasis() * Vector3.Forward;

    void PlaceCamera(float dt)
    {
        _eyeHeight = Mathf.MoveToward(_eyeHeight, Crouching ? CrouchHeadHeight : HeadHeight, 5f * dt);
        if (Health.Dead) _eyeHeight = Mathf.MoveToward(_eyeHeight, 0.4f, 2f * dt);
        Shake = Mathf.MoveToward(Shake, 0f, dt * 1.5f);
        float s = Mathf.Min(Shake, 2f) * 0.02f;
        var jitter = new Vector3(Mathf.Sin(_time * 37f) * s, Mathf.Sin(_time * 29f + 1f) * s, 0);
        var spin = Caught ? new Basis(Vector3.Forward, Mathf.Sin(_time * 3f) * 0.25f) : Basis.Identity; // the world reels when you're caught
        var view = new Transform3D(LookBasis() * spin, GlobalPosition + Vector3.Up * _eyeHeight + LookBasis() * jitter);
        if (ThirdPerson) view = view.TranslatedLocal(new Vector3(0, 0.6f, 5f));
        Camera.GlobalTransform = view;
        _body.Visible = ThirdPerson;
        _body.Rotation = new Vector3(0, _yaw, 0);
        _body.Scale = new Vector3(1, _eyeHeight / HeadHeight, 1);
    }

    void BuildBody()
    {
        _body = new Node3D { Name = "Body" };
        AddChild(_body);
        _body.AddChild(new MeshInstance3D
        {
            Mesh = new CapsuleMesh { Radius = 0.35f, Height = 1.8f },
            MaterialOverride = new StandardMaterial3D { AlbedoColor = new Color("f4a100"), Roughness = 0.6f },
            Position = new Vector3(0, 0.9f, 0),
        });
        _body.AddChild(new MeshInstance3D
        {
            Mesh = new BoxMesh { Size = new Vector3(0.46f, 0.2f, 0.14f) },
            MaterialOverride = new StandardMaterial3D { AlbedoColor = new Color("1b263b"), Metallic = 0.6f, Roughness = 0.1f },
            Position = new Vector3(0, 1.55f, -0.3f),
        });
        _body.AddChild(new MeshInstance3D
        {
            Mesh = new BoxMesh { Size = new Vector3(0.5f, 0.6f, 0.25f) },
            MaterialOverride = new StandardMaterial3D { AlbedoColor = new Color("2b2d42"), Roughness = 0.7f },
            Position = new Vector3(0, 1.2f, 0.3f),
        });
    }

    // ---- Test and screenshot hooks ----------------------------------------------------

    public void DebugPlace(Vector3 position, float yaw, float pitch)
    {
        GlobalPosition = position;
        Velocity = Vector3.Zero;
        _yaw = yaw;
        _pitch = pitch;
        ResetPhysicsInterpolation();
    }

    static bool Held(Key key) => Input.IsPhysicalKeyPressed(key);
    static float Axis(Key positive, Key negative) => (Held(positive) ? 1f : 0f) - (Held(negative) ? 1f : 0f);
    static Vector2 MoveInput() => new Vector2(Axis(Key.D, Key.A), Axis(Key.S, Key.W)).LimitLength(1f);
}
