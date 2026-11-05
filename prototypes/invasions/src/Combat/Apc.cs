using Godot;
using Invasions.Model;
using Invasions.World;

namespace Invasions.Combat;

/// <summary>
/// A ground carrier: drives (or walks) in from the source, stops at the drop point to let its troops
/// out, then sits and gives fire support. Departing, it swings round to face away and drives off. Kill
/// the driver and it rolls to a stop where it is. Subclasses supply the body and its animation.
/// </summary>
public abstract partial class GroundCarrier : CrewedVehicle
{
    float _turnedFor;

    /// <summary>Radians per second it can turn.</summary>
    protected virtual float TurnRate => 0.8f;
    /// <summary>Metres per second squared it can speed up or slow down.</summary>
    protected virtual float Acceleration => 5f;
    /// <summary>Where troops climb out and back in, in the vehicle's space.</summary>
    protected abstract Vector3 ExitLocal { get; }
    protected virtual float ExitSpread => 1f;

    public override Vector3 PickupPoint
    {
        get
        {
            Vector3 exit = ExitLocal with { Y = 0 };
            return GlobalTransform * (exit + exit.Normalized() * 0.6f);
        }
    }
    public override bool ReadyForPickup => Phase == Mode.Pickup && Speed < 0.5f;

    protected override Vector3 ExitPoint()
    {
        float side = World.Rng.RandfRange(-ExitSpread, ExitSpread);
        Vector3 local = ExitLocal;
        // Spread along the exit's own side: across the back for a rear door, along the flank for a side exit.
        Vector3 across = Mathf.Abs(local.X) > Mathf.Abs(local.Z) ? new Vector3(0, 0, side) : new Vector3(side, 0, 0);
        return (GlobalTransform * (local + across)) with { Y = 0.1f };
    }

    protected override void Move(float dt)
    {
        float target = 0f;
        Vector3 heading = -GlobalBasis.Z;
        switch (Phase)
        {
            case Mode.Approach:
            {
                Vector3 to = (Route.Drop - GlobalPosition) with { Y = 0 };
                float distance = to.Length();
                if (distance < 0.6f)
                {
                    Phase = Mode.Unload;
                    break;
                }
                TurnToward(to, TurnRate, dt);
                target = Mathf.Min((float)Plan.Speed, distance * 0.6f + 1.5f);
                break;
            }
            case Mode.Depart:
                // Swing round to face away from the base, then drive off.
                _turnedFor += dt;
                TurnToward(Route.Outward, TurnRate * 0.875f, dt);
                target = _turnedFor < 1.5f ? 3f : (float)Plan.Speed;
                break;
        }

        // A dead driver means no throttle: coast to a stop.
        float accel = Phase == Mode.Stranded ? Acceleration * 1.2f : Acceleration;
        Speed = Mathf.MoveToward(Speed, Phase == Mode.Stranded ? 0f : target, accel * dt);
        GlobalPosition += heading * Speed * dt;

        Animate(dt, Phase is Mode.Unload or Mode.Support or Mode.Pickup or Mode.Stranded);
    }

    /// <summary>Wheels, legs or hover glow, and the ramp or doors (open while stopped to let troops out).</summary>
    protected abstract void Animate(float dt, bool open);
}

/// <summary>
/// A six-wheeled Vharn armoured personnel carrier: a driver in the front hatch, two roof gunners, six
/// troops in the back behind a ramp.
/// </summary>
public partial class Apc : GroundCarrier
{
    Node3D _ramp;
    readonly Node3D[] _wheels = new Node3D[6];
    float _wheelSpin;

    protected override TargetClass HullClass => TargetClass.Hover;
    protected override Vector2 Footprint => new(3.6f, 8.4f);
    protected override Vector3 ExitLocal => new(0, 0.1f, 5.4f);
    public override Vector3 PickupPoint => GlobalTransform * new Vector3(0, 0, 6f);

    protected override void Animate(float dt, bool open)
    {
        _wheelSpin += Speed * dt / 0.62f;
        foreach (Node3D wheel in _wheels) wheel.Rotation = new Vector3(-_wheelSpin, 0, 0);
        _ramp.Rotation = new Vector3(Mathf.MoveToward(_ramp.Rotation.X, open ? 1.35f : 0f, 1.6f * dt), 0, 0);
    }

    protected override void OnStranded() => Fx.SmokeColumn(this, new Vector3(-0.8f, 2.6f, -2.4f), 0.4f);

    protected override void BuildBody()
    {
        Material red = Palette.Mat(Livery.Main, 0.55f);
        Material maroon = Palette.Mat(Livery.Dark, 0.6f);
        Material bone = Palette.Mat(Livery.Trim, 0.5f);
        Material black = Palette.Mat(Livery.Black, 0.7f);
        Material metal = Palette.Mat(Livery.Metal, 0.4f, 0.5f);
        Material check = Palette.Textured(Palette.VharnCheck, new Vector3(6f, 1f, 1f));

        // Running gear and hull. Forward is -Z.
        Block(new Vector3(0, 0.95f, 0.2f), new Vector3(3.0f, 0.6f, 8.0f), black);
        Block(new Vector3(0, 1.85f, 0.6f), new Vector3(3.4f, 1.3f, 6.4f), red);
        Block(new Vector3(0, 1.55f, -3.3f), new Vector3(3.2f, 0.9f, 1.6f), maroon);
        Block(new Vector3(0, 2.1f, -2.75f), new Vector3(3.3f, 0.5f, 0.9f), bone);
        Block(new Vector3(0, 2.62f, 0.9f), new Vector3(2.8f, 0.25f, 5.2f), maroon);
        Block(new Vector3(1.72f, 1.95f, 0.6f), new Vector3(0.05f, 0.5f, 6.0f), check, solid: false);
        Block(new Vector3(-1.72f, 1.95f, 0.6f), new Vector3(0.05f, 0.5f, 6.0f), check, solid: false);
        Block(new Vector3(0, 1.4f, -4.15f), new Vector3(2.6f, 0.35f, 0.15f), bone, solid: false);           // dozer lip
        Block(new Vector3(1.1f, 1.75f, -4.12f), new Vector3(0.4f, 0.2f, 0.06f), Palette.Glow(Livery.Glow, 4f), solid: false);
        Block(new Vector3(-1.1f, 1.75f, -4.12f), new Vector3(0.4f, 0.2f, 0.06f), Palette.Glow(Livery.Glow, 4f), solid: false);
        Block(new Vector3(1.2f, 3.4f, 3.2f), new Vector3(0.05f, 1.6f, 0.05f), black, solid: false);          // whip antenna
        Block(new Vector3(-0.8f, 2.55f, -2.3f), new Vector3(1.0f, 0.12f, 1.0f), black, solid: false);        // driver's hatch ring

        // Six wheels.
        int w = 0;
        foreach (float x in new[] { -1.75f, 1.75f })
            foreach (float z in new[] { -2.6f, 0.2f, 2.9f })
            {
                var wheel = new Node3D { Position = new Vector3(x, 0.62f, z) };
                AddChild(wheel);
                var tyre = Palette.Cylinder(wheel, Vector3.Zero, 0.62f, 0.5f, black, 14);
                tyre.Rotation = new Vector3(0, 0, Mathf.Pi / 2);
                Palette.Box(wheel, Vector3.Zero, new Vector3(0.52f, 0.5f, 0.14f), metal); // hub bar shows the spin
                _wheels[w++] = wheel;
            }

        // Rear ramp, hinged at the bottom.
        _ramp = new Node3D { Position = new Vector3(0, 0.75f, 3.85f) };
        AddChild(_ramp);
        Palette.Box(_ramp, new Vector3(0, 0.85f, 0), new Vector3(2.4f, 1.7f, 0.14f), maroon);
        Palette.Box(_ramp, new Vector3(0, 0.85f, 0.08f), new Vector3(1.6f, 0.2f, 0.04f), bone);

        // Hull number, Foss-style.
        foreach (float side in new[] { 1f, -1f })
        {
            var number = new Label3D
            {
                Text = $"V-{17 + Plan.Index}",
                Font = Palette.Font,
                FontSize = 96,
                PixelSize = 0.006f,
                Modulate = Livery.Trim,
                OutlineSize = 0,
                Position = new Vector3(side * 1.74f, 2.2f, -1.6f),
                Rotation = new Vector3(0, side * Mathf.Pi / 2, 0),
            };
            AddChild(number);
        }

        // Crew: the driver in the front hatch, gunners on two roof rings.
        CrewFigure(0, this, new Vector3(-0.8f, 2.35f, -2.3f));
        AddGun(new Vector3(0.7f, 2.72f, -0.6f), 0f);
        AddGun(new Vector3(-0.6f, 2.72f, 2.2f), Mathf.Pi);
    }
}
