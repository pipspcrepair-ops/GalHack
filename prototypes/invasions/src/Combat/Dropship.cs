using System.Collections.Generic;
using Godot;
using Invasions.Model;
using Invasions.World;

namespace Invasions.Combat;

/// <summary>
/// A flying carrier: flies in low, lands a few hundred metres out, lets its troops out, then hovers off
/// the drop point so the door guns can work. Departing, it climbs away. Kill the pilot and it comes
/// down where it is and stays there. Subclasses supply the body: a ramp (optional), lift jets, the
/// crew's seats.
/// </summary>
public abstract partial class Flyer : CrewedVehicle
{
    const float CruiseAltitude = 70f, Gravity = 14f;

    protected Node3D Ramp;
    protected readonly List<MeshInstance3D> Jets = new();
    float _climb, _fall;
    bool _crashed;

    /// <summary>Where it hovers while giving fire support.</summary>
    protected virtual float HoverAltitude => 22f;
    /// <summary>Where troops climb out, in the flyer's space.</summary>
    protected abstract Vector3 ExitLocal { get; }
    protected virtual float RampOpen => 1.2f;

    public override Vector3 PickupPoint => Route.Drop;
    public override bool ReadyForPickup => Phase == Mode.Pickup && GlobalPosition.Y < 0.4f && Flat(GlobalPosition, Route.Drop) < 2f;

    public bool Landed => GlobalPosition.Y < 0.3f;

    protected override Vector3 ExitPoint()
    {
        float side = World.Rng.RandfRange(-1f, 1f);
        return (GlobalTransform * (ExitLocal + new Vector3(side, 0, 0))) with { Y = 0.1f };
    }

    protected override void Move(float dt)
    {
        Vector3 p = GlobalPosition;
        switch (Phase)
        {
            case Mode.Approach:
            case Mode.Pickup:
            {
                // Fly toward the drop point and settle onto it: lower the closer we get.
                Vector3 to = (Route.Drop - p) with { Y = 0 };
                float distance = to.Length();
                float wantAlt = Mathf.Clamp((distance - 15f) * 0.45f, 0f, CruiseAltitude);
                float wantSpeed = Mathf.Min((float)Plan.Speed, distance * 0.55f + 1f);
                Speed = Mathf.MoveToward(Speed, wantSpeed, 12f * dt);
                if (distance > 0.05f) p += to / distance * Mathf.Min(Speed * dt, distance);
                p.Y = Mathf.MoveToward(p.Y, wantAlt, 11f * dt);
                Vector3 face = distance > 20f ? to : (Route.BaseCentre - p) with { Y = 0 };
                TurnToward(face, 0.9f, dt);
                if (Phase == Mode.Approach && distance < 0.5f && p.Y < 0.05f) Phase = Mode.Unload;
                break;
            }
            case Mode.Support:
                // Hover off the drop point, side on to the base so a door gunner has a line.
                p.Y = Mathf.MoveToward(p.Y, HoverAltitude, 6f * dt);
                Speed = Mathf.MoveToward(Speed, 0f, 10f * dt);
                Vector3 toBase = (Route.BaseCentre - p) with { Y = 0 };
                TurnToward(toBase.Rotated(Vector3.Up, Mathf.Pi / 2), 0.4f, dt);
                break;
            case Mode.Depart:
                _climb = Mathf.MoveToward(_climb, 1f, 0.5f * dt);
                Speed = Mathf.MoveToward(Speed, (float)Plan.Speed, 10f * dt);
                TurnToward(Route.Outward, 0.8f, dt);
                p += -GlobalBasis.Z * Speed * dt * _climb;
                p.Y = Mathf.MoveToward(p.Y, CruiseAltitude, 10f * dt);
                break;
            case Mode.Stranded:
                // No pilot: drop out of the sky and stay down.
                Speed = Mathf.MoveToward(Speed, 0f, 8f * dt);
                p += -GlobalBasis.Z * Speed * dt;
                if (p.Y > 0f)
                {
                    _fall += Gravity * dt;
                    p.Y = Mathf.Max(0f, p.Y - _fall * dt);
                }
                if (p.Y <= 0f && !_crashed)
                {
                    _crashed = true;
                    Rotation = new Vector3(0.08f, Rotation.Y, 0.12f);
                    Fx.Explosion(p + Vector3.Up, 2.5f);
                }
                break;
        }
        GlobalPosition = p;

        bool rampDown = Landed && Phase is Mode.Unload or Mode.Pickup or Mode.Stranded;
        if (Ramp != null) Ramp.Rotation = new Vector3(Mathf.MoveToward(Ramp.Rotation.X, rampDown ? RampOpen : 0f, 1.8f * dt), 0, 0);
        foreach (MeshInstance3D jet in Jets) jet.Visible = Phase != Mode.Stranded && !(Landed && Phase is Mode.Unload or Mode.Pickup);
    }

    /// <summary>A pilot's canopy: see-through, so the pilot can be shot through it.</summary>
    protected static Material Glass(Color tint) => new StandardMaterial3D
    {
        AlbedoColor = new Color(tint, 0.45f),
        Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
        Roughness = 0.05f,
        Metallic = 0.4f,
    };
}

/// <summary>
/// A Vharn dropship: a pilot behind the canopy, two door gunners, eight troops behind a rear ramp.
/// </summary>
public partial class Dropship : Flyer
{
    protected override TargetClass HullClass => TargetClass.SmallVessel;
    protected override Vector2 Footprint => new(9.4f, 10f);
    protected override Vector3 ExitLocal => new(0, 0.1f, 6.6f);

    protected override void OnStranded() => Fx.SmokeColumn(this, new Vector3(0, 3.6f, -3.5f), 0.6f);

    protected override void BuildBody()
    {
        Material red = Palette.Mat(Livery.Main, 0.5f);
        Material maroon = Palette.Mat(Livery.Dark, 0.6f);
        Material bone = Palette.Mat(Livery.Trim, 0.45f);
        Material black = Palette.Mat(Livery.Black, 0.7f);
        Material check = Palette.Textured(Palette.VharnCheck, new Vector3(3f, 1f, 1f));
        Material glass = Glass(new Color(0.08f, 0.1f, 0.12f));

        // Fuselage on skids. Forward is -Z; the skids touch the ground at y = 0.
        Block(new Vector3(0, 2.05f, 0.3f), new Vector3(3.4f, 2.5f, 8.6f), red);
        Block(new Vector3(0, 3.45f, 0.6f), new Vector3(2.6f, 0.4f, 6.4f), maroon);
        // Cockpit: a solid chin, side pillars and a roof, open behind the canopy so the pilot can be shot.
        Block(new Vector3(0, 1.4f, -4.6f), new Vector3(3.0f, 1.2f, 1.4f), bone);
        Block(new Vector3(1.35f, 2.5f, -4.6f), new Vector3(0.3f, 1.0f, 1.4f), bone);
        Block(new Vector3(-1.35f, 2.5f, -4.6f), new Vector3(0.3f, 1.0f, 1.4f), bone);
        Block(new Vector3(0, 3.06f, -4.6f), new Vector3(3.0f, 0.12f, 1.4f), bone);
        Block(new Vector3(0, 2.5f, -5.32f), new Vector3(2.4f, 0.9f, 0.06f), glass, solid: false);        // canopy: shoot the pilot through it
        Block(new Vector3(0, 1.05f, -5.45f), new Vector3(2.6f, 0.4f, 0.4f), maroon);
        Block(new Vector3(1.72f, 2.3f, 1.6f), new Vector3(0.05f, 0.6f, 4.6f), check, solid: false);
        Block(new Vector3(-1.72f, 2.3f, 1.6f), new Vector3(0.05f, 0.6f, 4.6f), check, solid: false);
        Block(new Vector3(0, 4.5f, 3.9f), new Vector3(0.3f, 1.9f, 2.2f), red);                              // tail fin
        Block(new Vector3(0, 4.9f, 4.3f), new Vector3(0.32f, 0.5f, 1.4f), check, solid: false);
        foreach (float x in new[] { -1.3f, 1.3f })
        {
            Block(new Vector3(x, 0.15f, 0.2f), new Vector3(0.3f, 0.3f, 7.0f), black);                        // skids
            Block(new Vector3(x, 0.55f, -2.2f), new Vector3(0.18f, 0.7f, 0.18f), black, solid: false);
            Block(new Vector3(x, 0.55f, 2.6f), new Vector3(0.18f, 0.7f, 0.18f), black, solid: false);
        }

        // Stub wings and engine pods with downward jets.
        foreach (float side in new[] { -1f, 1f })
        {
            Block(new Vector3(side * 2.6f, 2.9f, 0.9f), new Vector3(1.9f, 0.35f, 2.8f), black);
            Block(new Vector3(side * 4.0f, 2.9f, 0.9f), new Vector3(1.4f, 1.5f, 3.8f), bone);
            Block(new Vector3(side * 4.0f, 3.7f, 0.9f), new Vector3(1.42f, 0.18f, 3.82f), maroon, solid: false);
            Block(new Vector3(side * 4.0f, 2.85f, -1.05f), new Vector3(1.0f, 1.0f, 0.12f), Palette.Mat(Palette.Black), solid: false);
            Jets.Add(Block(new Vector3(side * 4.0f, 2.08f, 0.9f), new Vector3(1.0f, 0.12f, 3.0f), Palette.Glow(Palette.Fire, 5f), solid: false));
        }

        // Ramp at the back, hinged at the bottom.
        Ramp = new Node3D { Position = new Vector3(0, 0.75f, 4.65f) };
        AddChild(Ramp);
        Palette.Box(Ramp, new Vector3(0, 1.0f, 0), new Vector3(2.6f, 2.0f, 0.14f), maroon);
        Palette.Box(Ramp, new Vector3(0, 1.0f, 0.08f), new Vector3(2.0f, 0.18f, 0.04f), bone);

        var number = new Label3D
        {
            Text = $"DS {40 + Plan.Index}",
            Font = Palette.Font,
            FontSize = 96,
            PixelSize = 0.006f,
            Modulate = Livery.Trim,
            Position = new Vector3(1.74f, 2.6f, -2.2f),
            Rotation = new Vector3(0, Mathf.Pi / 2, 0),
        };
        AddChild(number);

        // Crew: the pilot behind the canopy, a door gunner each side.
        CrewFigure(0, this, new Vector3(0, 1.95f, -4.6f));
        AddGun(new Vector3(2.3f, 1.55f, -2.0f), -Mathf.Pi / 2);
        AddGun(new Vector3(-2.3f, 1.55f, -2.0f), Mathf.Pi / 2);
    }
}
