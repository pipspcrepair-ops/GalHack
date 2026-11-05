using System.Collections.Generic;
using Godot;
using Invasions.Model;
using Invasions.World;

namespace Invasions.Combat;

/// <summary>
/// A Kessari strider: a four-legged machine walker carrying six frames and cutters in its belly. Its
/// pilot rides an open cupola at the front, two gunners work rail and cannon rings on the deck. Slow,
/// tall and hard to miss; kill the pilot and it stops dead on its legs.
/// </summary>
public partial class Strider : GroundCarrier
{
    readonly List<(Node3D hip, Node3D knee, float phase)> _legs = new();
    float _gait;
    Node3D _hatch;

    protected override TargetClass HullClass => TargetClass.Hover;
    protected override Vector2 Footprint => new(4.2f, 7.2f);
    protected override Vector3 ExitLocal => new(0, 0.1f, 4.4f);
    protected override float TurnRate => 0.6f;
    protected override float Acceleration => 3f;

    protected override void Animate(float dt, bool open)
    {
        // Diagonal pairs swing together, more the faster it walks.
        float stride = Mathf.Clamp(Speed / 6f, 0f, 1f);
        _gait += Speed * dt * 0.55f;
        foreach (var (hip, knee, phase) in _legs)
        {
            float s = Mathf.Sin(_gait + phase);
            hip.Rotation = new Vector3(s * 0.38f * stride, 0, 0);
            knee.Rotation = new Vector3(-Mathf.Max(0f, Mathf.Sin(_gait + phase + 0.9f)) * 0.55f * stride, 0, 0);
        }
        _hatch.Rotation = new Vector3(Mathf.MoveToward(_hatch.Rotation.X, open ? -1.2f : 0f, 1.4f * dt), 0, 0);
    }

    protected override void OnStranded() => Fx.SmokeColumn(this, new Vector3(0.6f, 4.6f, -1.6f), 0.4f);

    protected override void BuildBody()
    {
        Material steel = Palette.Mat(Livery.Main, 0.35f, 0.6f);
        Material dark = Palette.Mat(Livery.Dark, 0.45f, 0.5f);
        Material brass = Palette.Mat(Livery.Trim, 0.3f, 0.7f);
        Material eye = Palette.Glow(Livery.Glow, 4f);

        // The chassis rides high on its legs. Forward is -Z.
        Block(new Vector3(0, 3.7f, 0.2f), new Vector3(3.0f, 1.5f, 6.2f), steel);
        Block(new Vector3(0, 4.6f, 0.6f), new Vector3(2.6f, 0.3f, 5.0f), dark);
        Block(new Vector3(0, 3.7f, -3.3f), new Vector3(1.8f, 1.1f, 1.2f), dark);                    // sensor head
        Block(new Vector3(0, 3.8f, -3.92f), new Vector3(1.4f, 0.16f, 0.04f), eye, solid: false);
        Block(new Vector3(0.5f, 3.45f, -3.92f), new Vector3(0.3f, 0.3f, 0.04f), eye, solid: false);
        Block(new Vector3(-0.5f, 3.45f, -3.92f), new Vector3(0.3f, 0.3f, 0.04f), eye, solid: false);
        Block(new Vector3(1.52f, 3.7f, 0.2f), new Vector3(0.04f, 0.9f, 5.0f), brass, solid: false); // clan plates
        Block(new Vector3(-1.52f, 3.7f, 0.2f), new Vector3(0.04f, 0.9f, 5.0f), brass, solid: false);
        Block(new Vector3(0, 2.85f, 0.2f), new Vector3(2.2f, 0.2f, 5.0f), dark, solid: false);      // belly
        Block(new Vector3(0, 4.3f, -2.4f), new Vector3(1.2f, 0.12f, 1.2f), dark, solid: false);     // cupola ring
        Block(new Vector3(1.1f, 5.6f, 2.8f), new Vector3(0.04f, 1.8f, 0.04f), brass, solid: false); // mast
        Block(new Vector3(1.1f, 6.5f, 2.8f), new Vector3(0.16f, 0.16f, 0.16f), eye, solid: false);

        // Belly hatch at the back, hinged at the top: troops drop out under the tail.
        _hatch = new Node3D { Position = new Vector3(0, 2.95f, 3.3f) };
        AddChild(_hatch);
        Palette.Box(_hatch, new Vector3(0, -0.8f, 0), new Vector3(1.8f, 1.6f, 0.12f), dark);
        Palette.Box(_hatch, new Vector3(0, -0.8f, 0.07f), new Vector3(1.2f, 0.12f, 0.03f), eye);

        // Four legs: hip, thigh, knee, shin and foot. Each leg is solid where it stands at rest.
        int i = 0;
        foreach (float z in new[] { -2.3f, 2.3f })
            foreach (float x in new[] { -1.75f, 1.75f })
            {
                var hip = new Node3D { Position = new Vector3(x, 3.3f, z) };
                AddChild(hip);
                Palette.Box(hip, new Vector3(0, 0, 0), new Vector3(0.7f, 0.7f, 0.7f), dark);
                Palette.Box(hip, new Vector3(x > 0 ? 0.2f : -0.2f, -0.85f, 0), new Vector3(0.36f, 1.7f, 0.42f), steel);
                var knee = new Node3D { Position = new Vector3(x > 0 ? 0.2f : -0.2f, -1.7f, 0) };
                hip.AddChild(knee);
                Palette.Box(knee, Vector3.Zero, new Vector3(0.5f, 0.5f, 0.5f), brass);
                Palette.Box(knee, new Vector3(0, -0.8f, 0), new Vector3(0.3f, 1.5f, 0.34f), steel);
                Palette.Box(knee, new Vector3(0, -1.55f, -0.1f), new Vector3(0.7f, 0.2f, 0.9f), dark);
                Hull.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = new Vector3(0.6f, 3.2f, 0.6f) }, Position = new Vector3(x * 1.1f, 1.6f, z) });
                // Diagonal pairs in step.
                _legs.Add((hip, knee, (i == 0 || i == 3) ? 0f : Mathf.Pi));
                i++;
            }

        var number = new Label3D
        {
            Text = $"K{40 + Plan.Index}-{(Plan.Index * 7 + 3) % 10}",
            Font = Palette.Font,
            FontSize = 96,
            PixelSize = 0.006f,
            Modulate = Livery.Trim,
            OutlineSize = 0,
            Position = new Vector3(1.55f, 3.9f, -1.0f),
            Rotation = new Vector3(0, Mathf.Pi / 2, 0),
        };
        AddChild(number);

        CrewFigure(0, this, new Vector3(0, 4.15f, -2.4f));
        AddGun(new Vector3(0.75f, 4.75f, -0.8f), 0f);
        AddGun(new Vector3(-0.75f, 4.75f, 1.9f), Mathf.Pi);
    }
}

/// <summary>
/// A Meridian Combine security carrier: a white armoured hover transport on four glowing lift pads, a
/// driver in the cab hatch, two roof gunners, six shielded officers behind the rear doors.
/// </summary>
public partial class HoverTruck : GroundCarrier
{
    Node3D _doorLeft, _doorRight;
    readonly List<MeshInstance3D> _pads = new();
    float _pulse;

    protected override TargetClass HullClass => TargetClass.Hover;
    protected override Vector2 Footprint => new(3.4f, 7.8f);
    protected override Vector3 ExitLocal => new(0, 0.1f, 5.0f);
    protected override float TurnRate => 0.9f;
    protected override float Acceleration => 6f;

    protected override void Animate(float dt, bool open)
    {
        _pulse += dt * 3f;
        float swing = open ? 1.6f : 0f;
        _doorLeft.Rotation = new Vector3(0, Mathf.MoveToward(_doorLeft.Rotation.Y, -swing, 1.8f * dt), 0);
        _doorRight.Rotation = new Vector3(0, Mathf.MoveToward(_doorRight.Rotation.Y, swing, 1.8f * dt), 0);
        foreach (MeshInstance3D pad in _pads)
            pad.Visible = Phase != Mode.Stranded || Mathf.Sin(_pulse * 7f) > 0.6f; // a dead driver: the pads gutter
    }

    protected override void OnStranded() => Fx.SmokeColumn(this, new Vector3(0.6f, 2.8f, -2.2f), 0.4f);

    protected override void BuildBody()
    {
        Material white = Palette.Mat(Livery.Main, 0.3f);
        Material navy = Palette.Mat(Livery.Dark, 0.5f);
        Material blue = Palette.Mat(Livery.Trim, 0.4f);
        Material glass = Palette.Mat(new Color("0f1a24"), 0.08f, 0.5f);
        Material glow = Palette.Glow(Livery.Glow, 3.5f);

        // A hull floating on lift pads. Forward is -Z.
        Block(new Vector3(0, 1.45f, 0.3f), new Vector3(3.2f, 1.1f, 7.4f), white);
        Block(new Vector3(0, 2.45f, 1.1f), new Vector3(3.0f, 0.9f, 4.8f), white);
        Block(new Vector3(0, 2.25f, -2.5f), new Vector3(3.0f, 0.5f, 1.6f), navy);
        Block(new Vector3(0, 2.3f, -3.32f), new Vector3(2.6f, 0.5f, 0.06f), glass, solid: false);          // windscreen
        Block(new Vector3(1.62f, 1.6f, 0.3f), new Vector3(0.04f, 0.28f, 7.0f), blue, solid: false);       // corporate stripe
        Block(new Vector3(-1.62f, 1.6f, 0.3f), new Vector3(0.04f, 0.28f, 7.0f), blue, solid: false);
        Block(new Vector3(0, 0.82f, 0.3f), new Vector3(2.8f, 0.16f, 6.8f), navy, solid: false);           // skirt
        Block(new Vector3(0, 2.94f, -1.0f), new Vector3(1.6f, 0.12f, 0.3f), glow, solid: false);          // light bar
        Block(new Vector3(-0.7f, 2.56f, -2.4f), new Vector3(0.9f, 0.1f, 0.9f), navy, solid: false);       // cab hatch ring
        foreach (float x in new[] { -1.1f, 1.1f })
            foreach (float z in new[] { -2.4f, 2.6f })
            {
                Block(new Vector3(x, 0.72f, z), new Vector3(0.9f, 0.2f, 1.5f), navy, solid: false);
                _pads.Add(Block(new Vector3(x, 0.6f, z), new Vector3(0.8f, 0.05f, 1.4f), glow, solid: false));
            }

        // Rear doors on side hinges.
        _doorLeft = new Node3D { Position = new Vector3(-1.3f, 1.4f, 4.02f) };
        _doorRight = new Node3D { Position = new Vector3(1.3f, 1.4f, 4.02f) };
        AddChild(_doorLeft);
        AddChild(_doorRight);
        Palette.Box(_doorLeft, new Vector3(0.65f, 0.5f, 0), new Vector3(1.3f, 1.9f, 0.1f), navy);
        Palette.Box(_doorRight, new Vector3(-0.65f, 0.5f, 0), new Vector3(1.3f, 1.9f, 0.1f), navy);
        Palette.Box(_doorLeft, new Vector3(0.65f, 0.9f, 0.06f), new Vector3(1.0f, 0.12f, 0.02f), glow);
        Palette.Box(_doorRight, new Vector3(-0.65f, 0.9f, 0.06f), new Vector3(1.0f, 0.12f, 0.02f), glow);

        foreach (float side in new[] { 1f, -1f })
        {
            var number = new Label3D
            {
                Text = $"MC SECURITY {101 + Plan.Index}",
                Font = Palette.Font,
                FontSize = 64,
                PixelSize = 0.006f,
                Modulate = Livery.Dark,
                OutlineSize = 0,
                Position = new Vector3(side * 1.63f, 2.45f, 1.2f),
                Rotation = new Vector3(0, side * Mathf.Pi / 2, 0),
            };
            AddChild(number);
        }

        CrewFigure(0, this, new Vector3(-0.7f, 2.35f, -2.4f));
        AddGun(new Vector3(0.7f, 2.9f, -0.2f), 0f);
        AddGun(new Vector3(-0.6f, 2.9f, 2.6f), Mathf.Pi);
    }
}

/// <summary>
/// A Carrion Reaver hover bike: a rider and two raiders hanging on behind. No guns: it is there to get
/// raiders in fast and out again. The riders climb off at the drop point and the bike waits for them.
/// Shoot the rider and it goes nowhere.
/// </summary>
public partial class HoverBike : GroundCarrier
{
    readonly List<Node3D> _pillion = new();
    MeshInstance3D _glow;

    protected override TargetClass HullClass => TargetClass.Hover;
    protected override Vector2 Footprint => new(1.6f, 4.2f);
    protected override Vector3 ExitLocal => new(1.5f, 0.1f, 0.4f);
    protected override float ExitSpread => 0.8f;
    protected override float TurnRate => 1.6f;
    protected override float Acceleration => 11f;

    protected override void Animate(float dt, bool open) => _glow.Visible = Phase != Mode.Stranded;

    protected override void OnDismounted(int remaining)
    {
        for (int i = 0; i < _pillion.Count; i++) _pillion[i].Visible = i < remaining;
    }

    protected override void OnStranded() => Fx.SmokeColumn(this, new Vector3(0, 1.4f, 1.6f), 0.25f);

    protected override void BuildBody()
    {
        Material rust = Palette.Mat(Livery.Main, 0.8f);
        Material dark = Palette.Mat(Livery.Dark, 0.9f);
        Material hazard = Palette.Mat(Livery.Trim, 0.7f);
        Material metal = Palette.Mat(Livery.Metal, 0.5f, 0.4f);

        // A long scrap-built hover bike. Forward is -Z.
        Block(new Vector3(0, 1.0f, 0.1f), new Vector3(1.0f, 0.5f, 3.8f), rust);
        Block(new Vector3(0, 1.3f, -1.75f), new Vector3(1.3f, 0.6f, 0.9f), hazard);                  // fairing
        Block(new Vector3(0, 1.62f, -2.05f), new Vector3(1.0f, 0.12f, 0.2f), dark, solid: false);   // bars
        Block(new Vector3(0, 1.3f, 0.1f), new Vector3(0.7f, 0.14f, 2.4f), dark, solid: false);      // saddle
        Block(new Vector3(0, 1.2f, 1.75f), new Vector3(1.0f, 0.7f, 0.9f), metal);                   // engine
        Block(new Vector3(0, 1.2f, 2.22f), new Vector3(0.6f, 0.4f, 0.06f), Palette.Glow(Livery.Glow, 5f), solid: false);
        Block(new Vector3(0.62f, 0.75f, 0.1f), new Vector3(0.12f, 0.12f, 3.4f), dark, solid: false); // skids
        Block(new Vector3(-0.62f, 0.75f, 0.1f), new Vector3(0.12f, 0.12f, 3.4f), dark, solid: false);
        Block(new Vector3(0, 1.7f, -1.5f), new Vector3(0.06f, 0.06f, 0.8f), metal, solid: false);   // a spike on the nose
        _glow = Block(new Vector3(0, 0.58f, 0.1f), new Vector3(0.8f, 0.05f, 3.2f), Palette.Glow(Livery.Glow, 2.5f), solid: false);

        CrewFigure(0, this, new Vector3(0, 1.15f, -0.9f));
        // The raiders riding behind (not crew: they climb off and fight).
        for (int i = 0; i < Plan.Troops.Count; i++)
        {
            var rider = new Node3D { Name = $"Pillion{i}", Position = new Vector3(0, 1.15f, 0.05f + i * 0.8f) };
            AddChild(rider);
            Looks.Crew(rider, Plan.Faction, Livery);
            _pillion.Add(rider);
        }
    }
}
