using Godot;
using Invasions.Model;
using Invasions.World;

namespace Invasions.Combat;

/// <summary>
/// A Kessari lifter: a flat machine hull on four ducted fans, a pilot in the nose pod and two laser
/// gunners on the flanks, eight frames and cutters inside.
/// </summary>
public partial class Lifter : Flyer
{
    protected override TargetClass HullClass => TargetClass.SmallVessel;
    protected override Vector2 Footprint => new(8.6f, 9.4f);
    protected override Vector3 ExitLocal => new(0, 0.1f, 5.8f);

    protected override void OnStranded() => Fx.SmokeColumn(this, new Vector3(0, 3.4f, 0), 0.6f);

    protected override void BuildBody()
    {
        Material steel = Palette.Mat(Livery.Main, 0.35f, 0.6f);
        Material dark = Palette.Mat(Livery.Dark, 0.45f, 0.5f);
        Material brass = Palette.Mat(Livery.Trim, 0.3f, 0.7f);
        Material eye = Palette.Glow(Livery.Glow, 4f);

        Block(new Vector3(0, 2.2f, 0.4f), new Vector3(3.8f, 1.6f, 7.4f), steel);
        Block(new Vector3(0, 3.15f, 0.6f), new Vector3(3.0f, 0.3f, 6.0f), dark);
        Block(new Vector3(0, 2.2f, 0.4f), new Vector3(3.84f, 0.2f, 6.6f), brass, solid: false);
        Block(new Vector3(0, 0.15f, 0.4f), new Vector3(3.0f, 0.3f, 6.0f), dark);                        // landing sled
        // The nose pod: open at the top so the pilot can be seen and shot.
        Block(new Vector3(0, 1.6f, -4.1f), new Vector3(2.2f, 1.2f, 1.4f), dark);
        Block(new Vector3(0, 2.6f, -4.75f), new Vector3(1.8f, 0.8f, 0.05f), Glass(new Color("0c2a33")), solid: false);
        Block(new Vector3(0, 1.75f, -4.82f), new Vector3(1.6f, 0.12f, 0.04f), eye, solid: false);
        // Four ducted lift fans.
        foreach (float x in new[] { -3.1f, 3.1f })
            foreach (float z in new[] { -2.4f, 3.0f })
            {
                Block(new Vector3(x * 0.72f, 2.5f, z), new Vector3(1.4f, 0.3f, 0.5f), dark, solid: false);
                MeshInstance3D duct = Palette.Cylinder(this, new Vector3(x, 2.5f, z), 1.2f, 0.7f, steel, 16);
                duct.Name = "Duct";
                Jets.Add(Palette.Cylinder(this, new Vector3(x, 2.1f, z), 1.0f, 0.06f, Palette.Glow(Livery.Glow, 4f), 16));
                Hull.AddChild(new CollisionShape3D { Shape = new CylinderShape3D { Radius = 1.2f, Height = 0.7f }, Position = new Vector3(x, 2.5f, z) });
            }

        Ramp = new Node3D { Position = new Vector3(0, 0.6f, 4.1f) };
        AddChild(Ramp);
        Palette.Box(Ramp, new Vector3(0, 0.85f, 0), new Vector3(2.4f, 1.7f, 0.14f), dark);
        Palette.Box(Ramp, new Vector3(0, 0.85f, 0.08f), new Vector3(1.6f, 0.12f, 0.03f), eye);

        CrewFigure(0, this, new Vector3(0, 2.0f, -4.1f));
        AddGun(new Vector3(2.05f, 1.2f, -1.4f), -Mathf.Pi / 2);
        AddGun(new Vector3(-2.05f, 1.2f, -1.4f), Mathf.Pi / 2);
    }
}

/// <summary>
/// A Meridian Combine shuttle: a white wedge with blue engines, a pilot behind the canopy, two door
/// gunners and eight officers behind the rear ramp.
/// </summary>
public partial class Shuttle : Flyer
{
    protected override TargetClass HullClass => TargetClass.SmallVessel;
    protected override Vector2 Footprint => new(9.6f, 10.4f);
    protected override Vector3 ExitLocal => new(0, 0.1f, 6.8f);

    protected override void OnStranded() => Fx.SmokeColumn(this, new Vector3(0, 3.6f, -2.5f), 0.6f);

    protected override void BuildBody()
    {
        Material white = Palette.Mat(Livery.Main, 0.3f);
        Material navy = Palette.Mat(Livery.Dark, 0.5f);
        Material blue = Palette.Mat(Livery.Trim, 0.4f);
        Material glow = Palette.Glow(Livery.Glow, 4f);

        Block(new Vector3(0, 2.1f, 0.4f), new Vector3(3.4f, 2.4f, 8.8f), white);
        Block(new Vector3(0, 0.8f, 0.4f), new Vector3(3.0f, 0.4f, 8.0f), navy);
        Block(new Vector3(0, 3.45f, 1.0f), new Vector3(2.4f, 0.3f, 6.2f), white);
        Block(new Vector3(1.72f, 2.0f, 0.4f), new Vector3(0.04f, 0.3f, 8.4f), blue, solid: false);
        Block(new Vector3(-1.72f, 2.0f, 0.4f), new Vector3(0.04f, 0.3f, 8.4f), blue, solid: false);
        // Cockpit: chin, pillars and roof, open behind the canopy.
        Block(new Vector3(0, 1.35f, -4.6f), new Vector3(3.0f, 1.1f, 1.4f), white);
        Block(new Vector3(1.35f, 2.45f, -4.6f), new Vector3(0.3f, 1.0f, 1.4f), navy);
        Block(new Vector3(-1.35f, 2.45f, -4.6f), new Vector3(0.3f, 1.0f, 1.4f), navy);
        Block(new Vector3(0, 3.0f, -4.6f), new Vector3(3.0f, 0.12f, 1.4f), navy);
        Block(new Vector3(0, 2.45f, -5.32f), new Vector3(2.4f, 0.9f, 0.06f), Glass(new Color("0f1a24")), solid: false);
        Block(new Vector3(0, 4.3f, 3.9f), new Vector3(0.25f, 1.6f, 2.0f), white);                         // fin
        Block(new Vector3(0, 4.5f, 4.2f), new Vector3(0.27f, 0.3f, 1.2f), blue, solid: false);
        foreach (float x in new[] { -1.3f, 1.3f }) Block(new Vector3(x, 0.15f, 0.4f), new Vector3(0.3f, 0.3f, 7.0f), navy);
        foreach (float side in new[] { -1f, 1f })
        {
            Block(new Vector3(side * 2.7f, 2.6f, 1.6f), new Vector3(2.0f, 0.3f, 3.0f), white);
            Block(new Vector3(side * 4.0f, 2.6f, 1.4f), new Vector3(1.3f, 1.3f, 4.0f), navy);
            Block(new Vector3(side * 4.0f, 2.6f, 3.42f), new Vector3(0.9f, 0.9f, 0.05f), glow, solid: false);
            Jets.Add(Block(new Vector3(side * 4.0f, 1.9f, 1.4f), new Vector3(0.9f, 0.1f, 3.4f), glow, solid: false));
        }

        Ramp = new Node3D { Position = new Vector3(0, 0.75f, 4.82f) };
        AddChild(Ramp);
        Palette.Box(Ramp, new Vector3(0, 1.0f, 0), new Vector3(2.6f, 2.0f, 0.14f), navy);
        Palette.Box(Ramp, new Vector3(0, 1.0f, 0.08f), new Vector3(2.0f, 0.14f, 0.04f), glow);

        var number = new Label3D
        {
            Text = $"MERIDIAN · {7 + Plan.Index:00}",
            Font = Palette.Font,
            FontSize = 72,
            PixelSize = 0.006f,
            Modulate = Livery.Dark,
            Position = new Vector3(1.73f, 2.6f, -1.6f),
            Rotation = new Vector3(0, Mathf.Pi / 2, 0),
        };
        AddChild(number);

        CrewFigure(0, this, new Vector3(0, 1.9f, -4.6f));
        AddGun(new Vector3(2.3f, 1.5f, -2.0f), -Mathf.Pi / 2);
        AddGun(new Vector3(-2.3f, 1.5f, -2.0f), Mathf.Pi / 2);
    }
}

/// <summary>
/// A Carrion Reaver gunship: a scrap-built brawler with two big engine pods, a pilot under a bubble
/// canopy, a cannon and a rocket pod on the back, and four raiders who drop off the ramp.
/// </summary>
public partial class Gunship : Flyer
{
    protected override TargetClass HullClass => TargetClass.SmallVessel;
    protected override Vector2 Footprint => new(9.0f, 9.0f);
    protected override Vector3 ExitLocal => new(0, 0.1f, 5.8f);
    protected override float HoverAltitude => 16f;

    protected override void OnStranded() => Fx.SmokeColumn(this, new Vector3(0, 3.2f, 0), 0.6f);

    protected override void BuildBody()
    {
        Material rust = Palette.Mat(Livery.Main, 0.8f);
        Material dark = Palette.Mat(Livery.Dark, 0.9f);
        Material metal = Palette.Mat(Livery.Metal, 0.5f, 0.4f);
        Material hazard = Palette.Textured(Palette.Hazard, new Vector3(3f, 1f, 1f));
        Material glow = Palette.Glow(Livery.Glow, 5f);

        Block(new Vector3(0, 2.0f, 0.4f), new Vector3(3.0f, 2.2f, 7.6f), rust);
        Block(new Vector3(0, 3.25f, 0.9f), new Vector3(2.2f, 0.3f, 5.4f), dark);
        Block(new Vector3(0, 1.6f, -3.9f), new Vector3(2.6f, 1.4f, 1.6f), metal);                         // armoured nose
        Block(new Vector3(0, 1.6f, -4.72f), new Vector3(2.4f, 0.5f, 0.06f), hazard, solid: false);
        Block(new Vector3(0.7f, 1.2f, -4.75f), new Vector3(0.3f, 0.5f, 0.05f), dark, solid: false);      // teeth
        Block(new Vector3(-0.7f, 1.2f, -4.75f), new Vector3(0.3f, 0.5f, 0.05f), dark, solid: false);
        Block(new Vector3(0, 2.7f, -3.5f), new Vector3(1.6f, 0.9f, 1.2f), Glass(new Color("2a0d08")), solid: false); // bubble canopy
        Block(new Vector3(0, 3.2f, -3.5f), new Vector3(1.7f, 0.1f, 1.3f), dark, solid: false);
        Block(new Vector3(0, 0.15f, 0.4f), new Vector3(2.6f, 0.3f, 6.0f), dark);                          // skids
        foreach (float side in new[] { -1f, 1f })
        {
            Block(new Vector3(side * 2.4f, 2.5f, 0.8f), new Vector3(1.8f, 0.35f, 2.2f), dark);
            Block(new Vector3(side * 3.8f, 2.4f, 0.8f), new Vector3(1.6f, 1.6f, 4.2f), metal);
            Block(new Vector3(side * 3.8f, 3.25f, 0.8f), new Vector3(1.62f, 0.12f, 4.22f), hazard, solid: false);
            Block(new Vector3(side * 3.8f, 2.4f, 2.92f), new Vector3(1.0f, 1.0f, 0.05f), glow, solid: false);
            Jets.Add(Block(new Vector3(side * 3.8f, 1.55f, 0.8f), new Vector3(1.1f, 0.1f, 3.4f), glow, solid: false));
            for (int i = 0; i < 3; i++)                                                                    // scrap spikes
                Block(new Vector3(side * 1.55f, 2.9f, -1.6f + i * 1.4f), new Vector3(0.1f, 0.6f, 0.1f), metal, solid: false);
        }

        Ramp = new Node3D { Position = new Vector3(0, 0.6f, 4.2f) };
        AddChild(Ramp);
        Palette.Box(Ramp, new Vector3(0, 0.9f, 0), new Vector3(2.4f, 1.8f, 0.14f), dark);
        Palette.Box(Ramp, new Vector3(0, 0.9f, 0.08f), new Vector3(1.8f, 0.3f, 0.03f), hazard);

        var number = new Label3D
        {
            Text = Plan.Index % 2 == 0 ? "GUTTER SAINT" : "LAST MERCY",
            Font = Palette.Font,
            FontSize = 72,
            PixelSize = 0.006f,
            Modulate = Livery.Trim,
            Position = new Vector3(1.52f, 2.2f, -0.6f),
            Rotation = new Vector3(0, Mathf.Pi / 2, 0),
        };
        AddChild(number);

        CrewFigure(0, this, new Vector3(0, 2.2f, -3.5f));
        AddGun(new Vector3(0, 3.4f, -1.4f), 0f);
        AddGun(new Vector3(0, 3.4f, 2.6f), Mathf.Pi);
    }
}
