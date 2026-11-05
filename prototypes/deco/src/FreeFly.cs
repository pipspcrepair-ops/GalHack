using Godot;

namespace DecoShowroom;

/// <summary>
/// A free-flying camera: WASD to move, the mouse to look, Space and Ctrl (or E and Q) to rise and
/// sink, Shift to go fast. Esc frees the mouse; a click captures it again.
/// </summary>
public partial class FreeFly : Camera3D
{
    public float Speed = 4f;
    float _yaw, _pitch;
    public bool Enabled = true;

    public override void _Ready()
    {
        Fov = 70;
        Near = 0.05f;
        Far = 600;
    }

    /// <summary>Puts the camera at <paramref name="from"/> looking at <paramref name="target"/>.</summary>
    public void Place(Vector3 from, Vector3 target)
    {
        Position = from;
        Vector3 d = (target - from).Normalized();
        _yaw = Mathf.Atan2(-d.X, -d.Z);
        _pitch = Mathf.Asin(Mathf.Clamp(d.Y, -1, 1));
        Apply();
    }

    void Apply() => Rotation = new Vector3(_pitch, _yaw, 0);

    public override void _UnhandledInput(InputEvent e)
    {
        if (!Enabled) return;
        if (e is InputEventMouseButton mb && mb.Pressed && Input.MouseMode != Input.MouseModeEnum.Captured)
            Input.MouseMode = Input.MouseModeEnum.Captured;
        else if (e is InputEventMouseMotion mm && Input.MouseMode == Input.MouseModeEnum.Captured)
        {
            _yaw -= mm.Relative.X * 0.003f;
            _pitch = Mathf.Clamp(_pitch - mm.Relative.Y * 0.003f, -1.5f, 1.5f);
            Apply();
        }
        else if (e is InputEventKey k && k.Pressed && k.Keycode == Key.Escape)
            Input.MouseMode = Input.MouseModeEnum.Visible;
    }

    public override void _Process(double delta)
    {
        if (!Enabled) return;
        Vector3 move = Vector3.Zero;
        if (Input.IsKeyPressed(Key.W)) move -= Basis.Z;
        if (Input.IsKeyPressed(Key.S)) move += Basis.Z;
        if (Input.IsKeyPressed(Key.A)) move -= Basis.X;
        if (Input.IsKeyPressed(Key.D)) move += Basis.X;
        if (Input.IsKeyPressed(Key.Space) || Input.IsKeyPressed(Key.E)) move += Vector3.Up;
        if (Input.IsKeyPressed(Key.Ctrl) || Input.IsKeyPressed(Key.Q)) move -= Vector3.Up;
        float speed = Speed * (Input.IsKeyPressed(Key.Shift) ? 4f : 1f);
        if (move != Vector3.Zero) Position += move.Normalized() * speed * (float)delta;
    }
}
