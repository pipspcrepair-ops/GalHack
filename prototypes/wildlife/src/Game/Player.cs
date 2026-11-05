using System;
using Godot;

namespace Wildlife;

/// <summary>
/// First-person explorer: walk, sprint (Shift), jump (Space), crouch (hold Ctrl), a blaster
/// for self-defence (left mouse), and E to butcher a kill into your pack. Creatures see the
/// player through <see cref="Target"/>: crouching makes you less alarming, the hovercraft far
/// more, and nothing can bite you while you sit in it.
/// </summary>
public partial class Player : CharacterBody3D
{
    public Camera3D Camera;
    public Health Health;
    public readonly Storage Pack = new(60);
    public bool Driving;
    public float Recoil;

    const float WalkSpeed = 5.5f, SprintSpeed = 9f, CrouchSpeed = 2.5f, GroundAccel = 45f, AirAccel = 10f;
    const float JumpSpeed = 5f, Gravity = 12f;
    const float StandHeight = 1.8f, CrouchHeight = 1.1f, HeadHeight = 1.6f, CrouchHeadHeight = 0.95f;
    const float MouseSensitivity = 0.0025f;

    float _yaw, _pitch, _eyeHeight = HeadHeight;
    Vector2 _mouse;
    CollisionShape3D _capsule;
    Node3D _gun;
    PlayerTarget _target;

    public bool Crouching { get; private set; }
    public bool Sprinting { get; private set; }
    public ITarget Target => _target ??= new PlayerTarget(this);
    public float Yaw => _yaw;
    public Vector3 LookDirection => (new Basis(Vector3.Up, _yaw) * new Basis(Vector3.Right, _pitch)) * Vector3.Forward;
    public Vector3 Eye => GlobalPosition + Vector3.Up * _eyeHeight;

    public override void _Ready()
    {
        _capsule = new CollisionShape3D
        {
            Name = "Capsule",
            Shape = new CapsuleShape3D { Radius = 0.35f, Height = StandHeight },
            Position = new Vector3(0, StandHeight / 2f, 0),
        };
        AddChild(_capsule);
        CollisionLayer = 0;
        CollisionMask = Layers.World | Layers.Vessel;
        FloorMaxAngle = Mathf.DegToRad(50f);
        Health = new Health(100f);
        BuildGun();
    }

    public void HandleMouse(Vector2 relative) => _mouse += relative;

    /// <summary>Walks and looks. Called by Main from _PhysicsProcess while on foot.</summary>
    public void Step(float dt)
    {
        Vector2 mouse = _mouse;
        _mouse = Vector2.Zero;
        _yaw -= mouse.X * MouseSensitivity;
        _pitch = Mathf.Clamp(_pitch - mouse.Y * MouseSensitivity, -1.45f, 1.45f);

        Vector3 v = Velocity;
        bool onFloor = IsOnFloor();
        if (!onFloor) v.Y -= Gravity * dt;
        Crouch(onFloor && Held(Key.Ctrl));
        Vector2 move = new Vector2(Axis(Key.D, Key.A), Axis(Key.S, Key.W)).LimitLength(1f);
        Sprinting = !Crouching && Held(Key.Shift) && move.Y < 0f;
        float speed = Crouching ? CrouchSpeed : Sprinting ? SprintSpeed : WalkSpeed;
        Vector3 wish = new Basis(Vector3.Up, _yaw) * new Vector3(move.X, 0, move.Y) * speed;
        float accel = onFloor ? GroundAccel : AirAccel;
        v.X = Mathf.MoveToward(v.X, wish.X, accel * dt);
        v.Z = Mathf.MoveToward(v.Z, wish.Z, accel * dt);
        if (onFloor && Held(Key.Space) && !Crouching) v.Y = JumpSpeed;
        Velocity = v;
        MoveAndSlide();

        _eyeHeight = Mathf.MoveToward(_eyeHeight, Crouching ? CrouchHeadHeight : HeadHeight, 5f * dt);
        Recoil = Mathf.MoveToward(Recoil, 0f, dt * 6f);
    }

    public void PlaceCamera()
    {
        Camera.GlobalTransform = new Transform3D(new Basis(Vector3.Up, _yaw) * new Basis(Vector3.Right, _pitch + Recoil * 0.05f), Eye);
        _gun.Visible = !Driving;
        _gun.Position = new Vector3(0.28f, -0.26f, -0.5f + Recoil * 0.08f);
        _gun.Rotation = new Vector3(Recoil * 0.25f, 0f, 0f);
    }

    public void SetLook(float yaw, float pitch)
    {
        _yaw = yaw;
        _pitch = pitch;
    }

    public void Teleport(Vector3 feet)
    {
        GlobalPosition = feet;
        Velocity = Vector3.Zero;
        ResetPhysicsInterpolation();
        Camera?.ResetPhysicsInterpolation();
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

    /// <summary>A chunky retro blaster: cream body, orange stripe, glowing coil. Rides on the camera.</summary>
    void BuildGun()
    {
        _gun = new Node3D { Name = "Blaster" };
        var cream = new StandardMaterial3D { AlbedoColor = new Color("f1e2c0"), Roughness = 0.5f };
        var orange = new StandardMaterial3D { AlbedoColor = new Color("e8641e"), Roughness = 0.5f };
        var dark = new StandardMaterial3D { AlbedoColor = new Color("23262e"), Roughness = 0.4f, Metallic = 0.5f };
        var coil = new StandardMaterial3D { AlbedoColor = new Color("7cf9ff"), EmissionEnabled = true, Emission = new Color("7cf9ff"), EmissionEnergyMultiplier = 2f };
        void Box(Vector3 size, Vector3 pos, Material m) => _gun.AddChild(new MeshInstance3D
        {
            Mesh = new BoxMesh { Size = size }, MaterialOverride = m, Position = pos, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        });
        Box(new Vector3(0.11f, 0.12f, 0.42f), Vector3.Zero, cream);
        Box(new Vector3(0.115f, 0.035f, 0.42f), new Vector3(0, 0.02f, 0), orange);
        Box(new Vector3(0.07f, 0.07f, 0.22f), new Vector3(0, 0.01f, -0.3f), dark);
        Box(new Vector3(0.08f, 0.16f, 0.08f), new Vector3(0, -0.12f, 0.1f), dark);
        Box(new Vector3(0.05f, 0.05f, 0.14f), new Vector3(0, 0.09f, -0.05f), coil);
    }

    public Node3D Muzzle => _gun;

    static bool Held(Key key) => Input.IsPhysicalKeyPressed(key);
    static float Axis(Key positive, Key negative) => (Held(positive) ? 1f : 0f) - (Held(negative) ? 1f : 0f);

    /// <summary>How the wildlife sees the player.</summary>
    sealed class PlayerTarget : ITarget
    {
        readonly Player _p;
        public PlayerTarget(Player p) => _p = p;
        public string Name => "Player 1";
        public Vector3 Position => _p.Driving && _p.Vehicle != null ? _p.Vehicle.GlobalPosition : _p.GlobalPosition;
        public Vector3 Forward => _p.LookDirection;
        public Health Health => _p.Health;
        public bool Attackable => !_p.Driving && !_p.Health.Downed;
        public float ThreatScale => _p.Driving ? 2.2f : _p.Crouching ? 0.6f : _p.Sprinting ? 1.3f : 1f;
    }

    public Node3D Vehicle;
}
