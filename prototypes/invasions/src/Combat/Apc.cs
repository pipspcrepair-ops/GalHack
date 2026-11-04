using Godot;
using Invasions.World;

namespace Invasions.Combat;

/// <summary>
/// A six-wheeled Vharn armoured personnel carrier: a driver in the front hatch, two roof gunners, six
/// troops in the back. Drives in from the source, drops the ramp a few hundred metres out, then sits
/// and gives fire support. Kill the driver and it rolls to a stop where it is.
/// </summary>
public partial class Apc : CrewedVehicle
{
    Node3D _ramp;
    readonly Node3D[] _wheels = new Node3D[6];
    float _wheelSpin, _turnedFor;

    public override Vector3 PickupPoint => GlobalTransform * new Vector3(0, 0, 6f);
    public override bool ReadyForPickup => Phase == Mode.Pickup && Speed < 0.5f;

    protected override Vector3 ExitPoint()
    {
        float side = World.Rng.RandfRange(-1f, 1f);
        return GlobalTransform * new Vector3(side, 0.1f, 5.4f);
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
                TurnToward(to, 0.8f, dt);
                target = Mathf.Min((float)Plan.Speed, distance * 0.6f + 1.5f);
                break;
            }
            case Mode.Depart:
                // Swing round to face away from the base, then drive off.
                _turnedFor += dt;
                TurnToward(Route.Outward, 0.7f, dt);
                target = _turnedFor < 1.5f ? 3f : (float)Plan.Speed;
                break;
        }

        // A dead driver means no throttle: coast to a stop.
        float accel = Phase == Mode.Stranded ? 6f : 5f;
        Speed = Mathf.MoveToward(Speed, Phase == Mode.Stranded ? 0f : target, accel * dt);
        GlobalPosition += heading * Speed * dt;

        _wheelSpin += Speed * dt / 0.62f;
        foreach (Node3D wheel in _wheels) wheel.Rotation = new Vector3(-_wheelSpin, 0, 0);

        bool rampDown = Phase is Mode.Unload or Mode.Support or Mode.Pickup or Mode.Stranded;
        _ramp.Rotation = new Vector3(Mathf.MoveToward(_ramp.Rotation.X, rampDown ? 1.35f : 0f, 1.6f * dt), 0, 0);
    }

    protected override void OnStranded() => Fx.SmokeColumn(this, new Vector3(-0.8f, 2.6f, -2.4f), 0.4f);

    protected override void BuildBody()
    {
        Material red = Palette.Mat(Palette.Oxblood, 0.55f);
        Material maroon = Palette.Mat(Palette.Maroon, 0.6f);
        Material bone = Palette.Mat(Palette.Bone, 0.5f);
        Material black = Palette.Mat(Palette.Black, 0.7f);
        Material metal = Palette.Mat(Palette.Gunmetal, 0.4f, 0.5f);
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
        Block(new Vector3(1.1f, 1.75f, -4.12f), new Vector3(0.4f, 0.2f, 0.06f), Palette.Glow(Palette.Acid, 4f), solid: false);
        Block(new Vector3(-1.1f, 1.75f, -4.12f), new Vector3(0.4f, 0.2f, 0.06f), Palette.Glow(Palette.Acid, 4f), solid: false);
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
                Modulate = Palette.Bone,
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
