using Godot;
using ShipHandling.Handling;
using ShipHandling.Physics;

namespace ShipHandling;

/// <summary>
/// Chase camera behind and above the vessel, sized to it, or a cockpit camera at the pilot's
/// eyes. Placed every physics tick after the vessels move; physics interpolation smooths it.
/// The chase view follows a hover vessel's heading only, so the horizon stays put over bumps.
/// </summary>
public partial class FlightCamera : Camera3D
{
    public Vessel Target;
    public bool Cockpit;

    /// <summary>Extra yaw and pitch around the vessel, for screenshots. Radians.</summary>
    public Vector2 Orbit;

    /// <summary>Distance multiplier for the chase view.</summary>
    public float Zoom = 1f;

    Vector3 _position;
    Basis _frame = Basis.Identity;
    bool _snap = true;

    public override void _Ready()
    {
        ProcessPhysicsPriority = 10;
        Fov = 72f;
        Near = 0.1f;
        Far = 9000f;
    }

    public void Snap() => _snap = true;

    public override void _PhysicsProcess(double delta)
    {
        if (Target == null) return;
        float dt = (float)delta;
        Transform3D ship = Target.GlobalTransform;

        if (Cockpit)
        {
            GlobalTransform = ship * new Transform3D(Basis.Identity, Target.Structure.Cockpit);
            if (_snap) ResetPhysicsInterpolation();
            _snap = false;
            return;
        }

        Basis frame = Target.Stats.IsHover ? FlightModel.Frame(ship.Basis, true) : ship.Basis;
        frame = frame * new Basis(Vector3.Up, Orbit.X) * new Basis(Vector3.Right, Orbit.Y);
        float length = Target.Length;
        var offset = new Vector3(0f, length * 0.26f + 2.2f, length * 1.05f + 6f) * Zoom;
        Vector3 wanted = ship.Origin + frame * offset;

        if (_snap)
        {
            _frame = frame;
            _position = wanted;
        }
        else
        {
            // Soft on roll and position so speed reads as the ship pulling away, firm enough to keep it framed.
            _frame = _frame.Slerp(frame, 1f - Mathf.Exp(-6f * dt)).Orthonormalized();
            _position = _position.Lerp(ship.Origin + _frame * offset, 1f - Mathf.Exp(-10f * dt));
        }

        Vector3 look = ship.Origin + _frame * new Vector3(0f, length * 0.1f, -length * 0.5f);
        Vector3 up = _frame.Y;
        if ((look - _position).Normalized().Cross(up).LengthSquared() < 1e-4f) up = Vector3.Up;
        GlobalTransform = new Transform3D(Basis.LookingAt(look - _position, up), _position);
        if (_snap) ResetPhysicsInterpolation();
        _snap = false;
    }
}
