using GalHack.Structures.Presentation;
using Godot;

namespace Manufacturing.Game;

/// <summary>A plain first-person walker: WASD, mouse look, Shift to sprint, Space to jump, N to fly.</summary>
public partial class Player : CharacterBody3D
{
    const float Walk = 4.5f, Sprint = 8f, Jump = 4.6f, Gravity = 12f, Look = 0.0022f, EyeHeight = 1.62f;

    public Camera3D Camera { get; private set; }
    public bool Flying { get; private set; }
    float _pitch;

    public override void _Ready()
    {
        CollisionLayer = PhysicsLayers.Player;
        CollisionMask = PhysicsLayers.World | PhysicsLayers.Structures;
        AddChild(new CollisionShape3D { Shape = new CapsuleShape3D { Radius = 0.32f, Height = 1.8f }, Position = new Vector3(0, 0.9f, 0) });
        Camera = new Camera3D { Position = new Vector3(0, EyeHeight, 0), Fov = 72, Near = 0.05f, Current = true };
        AddChild(Camera);
    }

    /// <summary>Stands the player at a spot, looking at a point.</summary>
    public void StandAndLook(Vector3 feet, Vector3 target)
    {
        GlobalPosition = feet;
        Vector3 d = target - (feet + new Vector3(0, EyeHeight, 0));
        Rotation = new Vector3(0, Mathf.Atan2(-d.X, -d.Z), 0);
        _pitch = Mathf.Atan2(d.Y, new Vector2(d.X, d.Z).Length());
        Camera.Rotation = new Vector3(_pitch, 0, 0);
        Velocity = Vector3.Zero;
    }

    public override void _UnhandledInput(InputEvent e)
    {
        if (e is InputEventMouseMotion m && Input.MouseMode == Input.MouseModeEnum.Captured)
        {
            RotateY(-m.Relative.X * Look);
            _pitch = Mathf.Clamp(_pitch - m.Relative.Y * Look, -1.45f, 1.45f);
            Camera.Rotation = new Vector3(_pitch, 0, 0);
        }
        else if (e is InputEventKey { Pressed: true, Echo: false, PhysicalKeycode: Key.N }) Flying = !Flying;
    }

    public override void _PhysicsProcess(double delta)
    {
        float dt = (float)delta;
        var wish = new Vector3(
            (Input.IsPhysicalKeyPressed(Key.D) ? 1 : 0) - (Input.IsPhysicalKeyPressed(Key.A) ? 1 : 0), 0,
            (Input.IsPhysicalKeyPressed(Key.S) ? 1 : 0) - (Input.IsPhysicalKeyPressed(Key.W) ? 1 : 0));
        wish = GlobalBasis * wish;
        wish.Y = 0;
        if (wish.LengthSquared() > 1) wish = wish.Normalized();
        float speed = Input.IsPhysicalKeyPressed(Key.Shift) ? Sprint : Walk;
        Vector3 v = Velocity;
        v.X = wish.X * speed;
        v.Z = wish.Z * speed;
        if (Flying) v.Y = ((Input.IsPhysicalKeyPressed(Key.Space) ? 1 : 0) - (Input.IsPhysicalKeyPressed(Key.Ctrl) ? 1 : 0)) * speed;
        else
        {
            v.Y -= Gravity * dt;
            if (IsOnFloor() && Input.IsPhysicalKeyPressed(Key.Space)) v.Y = Jump;
        }
        Velocity = v;
        MoveAndSlide();
        if (GlobalPosition.Y < -20) GlobalPosition = new Vector3(0, 2, 6); // fell off the world: back to the pad
    }
}
