using System;
using Godot;

namespace PlanetSurface.Player;

/// <summary>
/// A free-fly camera for looking around the planet: WASD and the mouse, Shift for 100 m/s,
/// Space and Ctrl to rise and sink. It never goes below the ground's surface.
/// </summary>
public partial class FreeFly : Node
{
    public const float Speed = 18f, FastSpeed = 100f;
    const float MouseSensitivity = 0.0025f;

    public Camera3D Camera;
    public Func<float, float, float> Ground;  // surface height at (x, z)
    public bool Active;

    /// <summary>Tests and screenshots fly without a keyboard: a velocity in world space, m/s.</summary>
    public Vector3? VelocityOverride;

    float _yaw, _pitch;
    Vector2 _mouse;

    public float Yaw => _yaw;

    public void Begin(Vector3 position, float yaw, float pitch)
    {
        Active = true;
        _yaw = yaw;
        _pitch = pitch;
        Camera.GlobalPosition = position;
        Camera.ResetPhysicsInterpolation();
        Apply();
    }

    public override void _UnhandledInput(InputEvent e)
    {
        if (Active && e is InputEventMouseMotion motion && Input.MouseMode == Input.MouseModeEnum.Captured) _mouse += motion.Relative;
    }

    public override void _Process(double delta)
    {
        if (!Active || Camera == null) return;
        float dt = (float)delta;
        _yaw -= _mouse.X * MouseSensitivity;
        _pitch = Mathf.Clamp(_pitch - _mouse.Y * MouseSensitivity, -1.55f, 1.55f);
        _mouse = Vector2.Zero;

        Vector3 velocity;
        if (VelocityOverride is Vector3 v) velocity = v;
        else
        {
            var input = new Vector3(Axis(Key.D, Key.A), Axis(Key.Space, Key.Ctrl), Axis(Key.S, Key.W));
            Basis look = new Basis(Vector3.Up, _yaw) * new Basis(Vector3.Right, _pitch);
            velocity = (look * new Vector3(input.X, 0, input.Z) + Vector3.Up * input.Y).LimitLength(1f)
                * (Input.IsPhysicalKeyPressed(Key.Shift) ? FastSpeed : Speed);
        }
        Vector3 p = Camera.GlobalPosition + velocity * dt;
        if (Ground != null) p.Y = Mathf.Max(p.Y, Ground(p.X, p.Z) + 1.5f);
        Camera.GlobalPosition = p;
        Apply();
    }

    void Apply() => Camera.GlobalBasis = new Basis(Vector3.Up, _yaw) * new Basis(Vector3.Right, _pitch);

    public void SetLook(float yaw, float pitch)
    {
        _yaw = yaw;
        _pitch = pitch;
        Apply();
    }

    static float Axis(Key positive, Key negative) => (Input.IsPhysicalKeyPressed(positive) ? 1f : 0f) - (Input.IsPhysicalKeyPressed(negative) ? 1f : 0f);
}
