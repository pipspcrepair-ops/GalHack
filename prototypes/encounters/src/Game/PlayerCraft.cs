using Godot;

namespace Encounters;

/// <summary>
/// The player's skiff, with simple arcade flight: the mouse steers, W and S set the throttle
/// (which holds when you let go, so crossing a few kilometres doesn't need a held key), A and D
/// roll, Shift boosts. Velocity swings round to the nose with a little drift. A chase camera
/// follows, or V puts you in the cockpit.
/// </summary>
public partial class PlayerCraft : Node3D
{
    public const float CruiseSpeed = 260f, BoostSpeed = 700f;
    const float Accel = 150f, TurnRate = 1.3f, RollRate = 1.8f, MouseSensitivity = 0.003f, Grip = 2.5f;

    public Camera3D Camera { get; private set; }
    public bool Cockpit;
    public float Speed => _velocity.Length();
    public Vector3 Velocity => _velocity;

    /// <summary>Holds the camera on a fixed transform, for screenshots.</summary>
    public Transform3D? CameraOverride;

    Vector3 _velocity;
    float _throttle;
    Vector2 _mouse;
    Node3D _model;
    MeshInstance3D[] _flares;

    public override void _Ready()
    {
        ProcessPhysicsPriority = -10; // move before the director reads where the player is
        Hull hull = HullLibrary.Get("skiff");
        _model = new MeshInstance3D { Mesh = hull.Mesh, MaterialOverride = Visuals.Hull };
        AddChild(_model);
        _flares = new MeshInstance3D[hull.Engines.Count];
        for (int i = 0; i < _flares.Length; i++)
        {
            var (at, size) = hull.Engines[i];
            _flares[i] = Visuals.Flare(new Color(1.8f, 0.9f, 0.35f), size * 1.7f);
            _flares[i].Position = at + Vector3.Back * 0.4f;
            _model.AddChild(_flares[i]);
        }
        Camera = new Camera3D { Name = "Camera", Fov = 72f, Near = 0.5f, Far = 60000f, TopLevel = true };
        AddChild(Camera);
        Camera.MakeCurrent();
    }

    public void HandleInput(InputEvent e)
    {
        if (e is InputEventMouseMotion motion && Input.MouseMode == Input.MouseModeEnum.Captured)
            _mouse += motion.Relative;
    }

    /// <summary>Puts the skiff somewhere at rest, looking at a point, and snaps the camera behind it.</summary>
    public void Place(Vector3 position, Vector3 lookAt, float speed = 0f)
    {
        GlobalTransform = new Transform3D(Basis.LookingAt((lookAt - position).Normalized(), Vector3.Up), position);
        _throttle = speed;
        _velocity = -GlobalBasis.Z * speed;
        _mouse = Vector2.Zero;
        PlaceCamera(1f);
        ResetPhysicsInterpolation();
        Camera.ResetPhysicsInterpolation();
    }

    public override void _PhysicsProcess(double delta)
    {
        float dt = (float)delta;
        Vector2 mouse = _mouse;
        _mouse = Vector2.Zero;

        float pitch = -mouse.Y * MouseSensitivity + Axis(Key.Up, Key.Down) * TurnRate * dt;
        float yaw = -mouse.X * MouseSensitivity + Axis(Key.Left, Key.Right) * TurnRate * dt;
        float roll = Axis(Key.A, Key.D) * RollRate * dt;
        Basis = (Basis * new Basis(Vector3.Right, pitch) * new Basis(Vector3.Up, yaw) * new Basis(Vector3.Back, roll)).Orthonormalized();

        bool boost = Input.IsPhysicalKeyPressed(Key.Shift);
        if (Input.IsPhysicalKeyPressed(Key.W) || boost) _throttle += Accel * dt * (boost ? 2f : 1f);
        if (Input.IsPhysicalKeyPressed(Key.S)) _throttle -= Accel * dt;
        float top = boost ? BoostSpeed : CruiseSpeed;
        _throttle = Mathf.Clamp(_throttle, -40f, Mathf.Max(top, Mathf.Min(_throttle, BoostSpeed)));
        if (!boost && _throttle > CruiseSpeed) _throttle = Mathf.MoveToward(_throttle, CruiseSpeed, Accel * dt);

        Vector3 wanted = -GlobalBasis.Z * _throttle;
        _velocity = _velocity.Lerp(wanted, 1f - Mathf.Exp(-Grip * dt)); // arcade grip, a little drift
        Position += _velocity * dt;

        float glow = Mathf.Clamp(Mathf.Abs(_throttle) / CruiseSpeed, 0.15f, 1.6f);
        foreach (MeshInstance3D flare in _flares) flare.Scale = Vector3.One * glow;

        PlaceCamera(1f - Mathf.Exp(-10f * dt));
    }

    void PlaceCamera(float blend)
    {
        if (CameraOverride is Transform3D fixedView)
        {
            Camera.GlobalTransform = fixedView;
            _model.Visible = !Cockpit;
            return;
        }
        _model.Visible = !Cockpit;
        Transform3D target = Cockpit
            ? GlobalTransform * new Transform3D(Basis.Identity, new Vector3(0, 1.3f, -1.6f))
            : new Transform3D(GlobalBasis * new Basis(Vector3.Right, -0.04f), GlobalTransform * new Vector3(0, 5f, 21f));
        Transform3D current = Camera.GlobalTransform;
        Camera.GlobalTransform = new Transform3D(
            current.Basis.Orthonormalized().Slerp(target.Basis.Orthonormalized(), blend),
            current.Origin.Lerp(target.Origin, blend));
    }

    static float Axis(Key positive, Key negative) =>
        (Input.IsPhysicalKeyPressed(positive) ? 1f : 0f) - (Input.IsPhysicalKeyPressed(negative) ? 1f : 0f);
}
