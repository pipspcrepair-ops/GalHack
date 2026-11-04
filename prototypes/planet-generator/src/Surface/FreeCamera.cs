using Godot;

namespace Planets.Surface;

/// <summary>A free-flying camera: WASD and mouse, Shift for speed, Space and Ctrl for up and down.</summary>
public partial class FreeCamera : Camera3D
{
    const float Speed = 30f, BoostFactor = 8f, Acceleration = 6f, MouseSensitivity = 0.0025f;

    float _yaw, _pitch;
    Vector2 _mouse;
    Vector3 _velocity;

    public override void _Ready()
    {
        PhysicsInterpolationMode = PhysicsInterpolationModeEnum.Off; // moved every rendered frame, not per tick
        Input.MouseMode = Input.MouseModeEnum.Captured;
    }

    public override void _UnhandledInput(InputEvent e)
    {
        if (e is InputEventMouseMotion motion && Input.MouseMode == Input.MouseModeEnum.Captured)
            _mouse += motion.Relative;
        else if (e is InputEventMouseButton { Pressed: true } && Input.MouseMode != Input.MouseModeEnum.Captured)
            Input.MouseMode = Input.MouseModeEnum.Captured;
    }

    /// <summary>Heading in radians (0 = north, clockwise), and pitch (negative looks down).</summary>
    public void SetLook(float heading, float pitch)
    {
        _yaw = -heading;
        _pitch = pitch;
        Rotation = new Vector3(_pitch, _yaw, 0);
    }

    public float Heading => -_yaw;

    public void Fly(float dt)
    {
        _yaw -= _mouse.X * MouseSensitivity;
        _pitch = Mathf.Clamp(_pitch - _mouse.Y * MouseSensitivity, -1.5f, 1.5f);
        _mouse = Vector2.Zero;
        Rotation = new Vector3(_pitch, _yaw, 0);

        var input = new Vector3(
            Axis(Key.D, Key.A),
            Axis(Key.Space, Key.Ctrl),
            Axis(Key.S, Key.W));
        Vector3 wish = Basis * new Vector3(input.X, 0, input.Z) + Vector3.Up * input.Y;
        float speed = Speed * (Input.IsPhysicalKeyPressed(Key.Shift) ? BoostFactor : 1f);
        _velocity = _velocity.Lerp(wish.LimitLength(1f) * speed, 1f - Mathf.Exp(-Acceleration * dt));
        Position += _velocity * dt;
    }

    static float Axis(Key positive, Key negative) =>
        (Input.IsPhysicalKeyPressed(positive) ? 1f : 0f) - (Input.IsPhysicalKeyPressed(negative) ? 1f : 0f);
}
