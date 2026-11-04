using System.Collections.Generic;
using Godot;

namespace DiegeticUi.World;

/// <summary>
/// The bridge of a Foss-style ship, in metres: cream hull, orange and yellow livery, teal
/// trim, a wide window onto a ringed giant. The origin is the middle of the deck, forward
/// (the window) is -Z. 12 m wide, 4.4 m high, from z = -8 (window) to z = 6 (back wall).
///
/// Only the room is built here. The things you use (console, holo-table, constructor,
/// trader, cold store) are placed by <see cref="Main"/> at the anchors below.
/// </summary>
public partial class Bridge : Node3D
{
    public static readonly Color Cream = new("e9dfc4");
    public static readonly Color CreamShade = new("cfc4a8");
    public static readonly Color Orange = new("e85d04");
    public static readonly Color Yellow = new("f4a100");
    public static readonly Color Red = new("c1121f");
    public static readonly Color Teal = new("0a9396");
    public static readonly Color Dark = new("2b2d42");
    public static readonly Color Deck = new("34363f");
    public static readonly Color Ice = new("8fb3cc");
    public static readonly Color Frost = new("e6f3fb");

    // Anchors.
    public static readonly Transform3D ConsoleScreen = new(Basis.FromEuler(new Vector3(Mathf.DegToRad(-24), 0, 0)), new Vector3(0, 1.42f, -5.62f));
    public static readonly Vector3 HoloTable = new(0, 0, -1.3f);
    public static readonly Transform3D ConstructorScreen = new(Basis.FromEuler(new Vector3(0, Mathf.DegToRad(90), 0)), new Vector3(-4.93f, 1.62f, -1.4f));
    public static readonly Transform3D TraderScreen = new(Basis.FromEuler(new Vector3(0, Mathf.DegToRad(-90), 0)), new Vector3(4.98f, 1.6f, -1.4f));
    public static readonly Vector3 Spawn = new(0, 0.05f, 3.4f);

    /// <summary>The refrigerated store in the back-right corner: -60 °C inside.</summary>
    public static readonly Aabb ColdStore = new(new Vector3(3.15f, -0.5f, 2.15f), new Vector3(2.9f, 5f, 3.8f));
    public static readonly Vector3 ColdDoor = new(3.9f, 0.05f, 4.0f); // just inside the doorway

    public static readonly Vector3[] Sirens =
    {
        new(-4.6f, 4.2f, -6.2f), new(4.6f, 4.2f, -6.2f), new(-4.6f, 4.2f, 1.6f), new(2.2f, 4.2f, 1.6f),
        new(0, 4.25f, -7.55f), // over the window, in sight of anyone at the console
    };

    public static readonly Vector3 LeverProp = new(1.18f, 1.0f, -5.45f);
    public static readonly Vector3 ShieldProp = new(-1.18f, 1.0f, -5.45f);

    readonly Dictionary<(Color, bool), StandardMaterial3D> _materials = new();

    public override void _Ready()
    {
        BuildShell();
        BuildWindow();
        BuildConsole();
        BuildConstructor();
        BuildTrader();
        BuildColdStore();
        BuildLights();
    }

    void BuildShell()
    {
        Solid(new Vector3(0, -0.1f, -1), new Vector3(12.4f, 0.2f, 14.6f), Deck);
        Solid(new Vector3(0, 4.5f, -1), new Vector3(12.4f, 0.2f, 14.6f), CreamShade);
        Solid(new Vector3(-6.1f, 2.2f, -1), new Vector3(0.2f, 4.6f, 14.6f), Cream);
        Solid(new Vector3(6.1f, 2.2f, -1), new Vector3(0.2f, 4.6f, 14.6f), Cream);
        Solid(new Vector3(0, 2.2f, 6.1f), new Vector3(12.4f, 4.6f, 0.2f), Cream);

        // Deck plating: a lighter walkway and a yellow ring round the holo-table.
        Prop(new Vector3(0, 0.005f, -1), new Vector3(3.2f, 0.01f, 13.4f), new Color("3e414c"));
        Prop(HoloTable + new Vector3(0, 0.012f, 0), Disc(1.75f), Yellow);
        Prop(HoloTable + new Vector3(0, 0.018f, 0), Disc(1.6f), Deck);

        // Livery on the side walls: a broad orange band and a yellow pinstripe, as on the hull.
        foreach (float x in new[] { -5.98f, 5.98f })
        {
            Prop(new Vector3(x, 0.45f, -1), new Vector3(0.05f, 0.9f, 14.4f), Dark);
            Prop(new Vector3(x, 2.65f, -1), new Vector3(0.05f, 0.42f, 14.4f), Orange);
            Prop(new Vector3(x, 2.98f, -1), new Vector3(0.05f, 0.09f, 14.4f), Yellow);
        }
        Prop(new Vector3(0, 0.45f, 5.98f), new Vector3(12.2f, 0.9f, 0.05f), Dark);
        Prop(new Vector3(0, 2.65f, 5.98f), new Vector3(12.2f, 0.42f, 0.05f), Orange);
        Prop(new Vector3(0, 2.98f, 5.98f), new Vector3(12.2f, 0.09f, 0.05f), Yellow);

        // Chunky ceiling ribs and light panels.
        for (float z = -6.5f; z <= 5f; z += 2.9f)
        {
            Prop(new Vector3(0, 4.25f, z), new Vector3(12f, 0.3f, 0.35f), Orange);
            foreach (float x in new[] { -5.85f, 5.85f })
                Prop(new Vector3(x, 3.4f, z), new Vector3(0.3f, 1.8f, 0.35f), Orange);
        }
        foreach (float z in new[] { -5f, -2.1f, 0.8f, 3.7f })
            Glow(new Vector3(0, 4.38f, z), new Vector3(2.4f, 0.04f, 1.1f), new Color("fff1d6"), 1.4f);

        // Back wall: a closed hatch with hazard chevrons, so the room reads as part of a ship.
        Prop(new Vector3(-1.2f, 1.25f, 5.96f), new Vector3(2.0f, 2.5f, 0.08f), new Color("44475a"));
        for (int i = 0; i < 6; i++)
            Prop(new Vector3(-2.05f + i * 0.34f, 2.6f, 5.95f), new Vector3(0.17f, 0.2f, 0.08f), i % 2 == 0 ? Yellow : Dark);
        Label(new Vector3(-1.2f, 1.6f, 5.9f), new Vector3(0, 180, 0), "07", 260, Yellow);
    }

    void BuildWindow()
    {
        // Front wall: solid sill and header, with a wide window between.
        Solid(new Vector3(0, 0.45f, -8.1f), new Vector3(12.4f, 0.9f, 0.3f), Cream);
        Solid(new Vector3(0, 4.05f, -8.1f), new Vector3(12.4f, 1.1f, 0.3f), Cream);
        foreach (float x in new[] { -5.6f, 5.6f })
            Solid(new Vector3(x, 2.2f, -8.1f), new Vector3(1.2f, 4.4f, 0.3f), Cream);
        foreach (float x in new[] { -1.75f, 1.75f })
            Solid(new Vector3(x, 2.25f, -8.1f), new Vector3(0.34f, 2.6f, 0.36f), Orange);
        Prop(new Vector3(0, 0.92f, -7.95f), new Vector3(10f, 0.06f, 0.08f), Yellow);
        Prop(new Vector3(0, 3.52f, -7.95f), new Vector3(10f, 0.08f, 0.08f), Dark);

        var glass = new StandardMaterial3D
        {
            AlbedoColor = new Color(0.55f, 0.8f, 0.95f, 0.08f),
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
            Roughness = 0.05f,
            Metallic = 0.4f,
        };
        AddChild(new MeshInstance3D { Mesh = new BoxMesh { Size = new Vector3(10f, 2.6f, 0.04f) }, MaterialOverride = glass, Position = new Vector3(0, 2.25f, -8.1f), CastShadow = GeometryInstance3D.ShadowCastingSetting.Off });

        // Two pilot seats at the glass, with low flight consoles.
        foreach (float x in new[] { -3.3f, 3.3f })
        {
            Prop(new Vector3(x, 0.3f, -7.45f), new Vector3(2.0f, 0.6f, 0.7f), Dark);
            Glow(new Vector3(x - 0.5f, 0.61f, -7.35f), new Vector3(0.5f, 0.02f, 0.25f), Teal, 2f);
            Glow(new Vector3(x + 0.3f, 0.61f, -7.35f), new Vector3(0.2f, 0.02f, 0.2f), Orange, 2f);
            Prop(new Vector3(x, 0.45f, -6.5f), new Vector3(0.7f, 0.12f, 0.7f), Red);
            Prop(new Vector3(x, 0.85f, -6.15f), new Vector3(0.7f, 0.9f, 0.14f), Red);
            Prop(new Vector3(x, 0.2f, -6.5f), new Vector3(0.18f, 0.4f, 0.18f), Dark);
        }
    }

    void BuildConsole()
    {
        // A standing systems console: a dark desk with an orange lip, its screen tipped back,
        // a real lever on the right and a shield switch on the left.
        Solid(new Vector3(0, 0.5f, -5.5f), new Vector3(3.2f, 1.0f, 0.8f), Dark);
        Prop(new Vector3(0, 1.0f, -5.5f), new Vector3(3.26f, 0.05f, 0.86f), Orange);
        Prop(new Vector3(0, 0.25f, -5.08f), new Vector3(3.0f, 0.06f, 0.02f), Yellow);
        var stand = Prop(new Vector3(0, 1.36f, -5.82f), new Vector3(1.5f, 0.92f, 0.12f), Dark);
        stand.RotationDegrees = new Vector3(-24, 0, 0);
        foreach (Vector3 p in new[] { LeverProp, ShieldProp })
            Prop(p + new Vector3(0, 0.04f, 0), new Vector3(0.42f, 0.08f, 0.42f), new Color("1d1f27"));
        Label(new Vector3(0, 0.62f, -5.09f), Vector3.Zero, "SYSTEMS", 64, Cream);
    }

    void BuildConstructor()
    {
        // A cream fabricator against the port wall: screen on its face, a glowing output bay below.
        Solid(new Vector3(-5.5f, 1.35f, -1.4f), new Vector3(1.1f, 2.7f, 2.8f), Cream);
        Prop(new Vector3(-5.5f, 2.74f, -1.4f), new Vector3(1.14f, 0.1f, 2.84f), Orange);
        Prop(new Vector3(-4.95f, 2.45f, -1.4f), new Vector3(0.04f, 0.16f, 2.8f), Yellow);
        Prop(new Vector3(-4.97f, 0.62f, -1.4f), new Vector3(0.06f, 0.8f, 1.7f), Dark);
        Glow(new Vector3(-4.94f, 0.62f, -1.4f), new Vector3(0.02f, 0.6f, 1.5f), Teal, 1.6f);
        foreach (float z in new[] { -2.75f, -0.05f })
            Prop(new Vector3(-4.97f, 1.35f, z), new Vector3(0.1f, 2.7f, 0.12f), Dark);
        Label(new Vector3(-4.92f, 2.45f, -1.4f), new Vector3(0, 90, 0), "FAB-2   CONSTRUCTOR", 56, Dark);
    }

    void BuildTrader()
    {
        // A dark trade kiosk on the starboard wall, yellow-capped, with a hazard-striped base.
        Solid(new Vector3(5.6f, 1.25f, -1.4f), new Vector3(0.9f, 2.5f, 2.3f), Dark);
        Prop(new Vector3(5.6f, 2.54f, -1.4f), new Vector3(0.94f, 0.1f, 2.34f), Yellow);
        for (int i = 0; i < 7; i++)
            Prop(new Vector3(5.14f, 0.18f, -2.45f + i * 0.35f), new Vector3(0.03f, 0.3f, 0.17f), Yellow);
        Prop(new Vector3(5.12f, 0.9f, -1.4f), new Vector3(0.1f, 0.06f, 1.6f), Yellow);
        Label(new Vector3(5.12f, 2.36f, -1.4f), new Vector3(0, -90, 0), "TRADE LINK", 84, Yellow);
    }

    void BuildColdStore()
    {
        // Walls with a doorway on the room side, frosted inside, lit cold blue.
        Solid(new Vector3(3.05f, 2.2f, 2.65f), new Vector3(0.2f, 4.4f, 1.1f), Cream);
        Solid(new Vector3(3.05f, 2.2f, 5.35f), new Vector3(0.2f, 4.4f, 1.3f), Cream);
        Solid(new Vector3(3.05f, 3.55f, 4.0f), new Vector3(0.2f, 1.7f, 1.6f), Cream);
        Solid(new Vector3(4.6f, 2.2f, 2.05f), new Vector3(3.1f, 4.4f, 0.2f), Cream);
        Prop(new Vector3(4.6f, 0.012f, 4.1f), new Vector3(2.9f, 0.02f, 3.9f), Frost);
        Prop(new Vector3(4.6f, 2.4f, 5.94f), new Vector3(2.9f, 3.2f, 0.06f), Ice);
        Prop(new Vector3(5.94f, 2.4f, 4.1f), new Vector3(0.06f, 3.2f, 3.6f), Ice);
        // Inside faces of the store's walls, frosted.
        Prop(new Vector3(3.17f, 2.4f, 2.65f), new Vector3(0.03f, 3.2f, 1.0f), Ice);
        Prop(new Vector3(3.17f, 2.4f, 5.35f), new Vector3(0.03f, 3.2f, 1.2f), Ice);
        Prop(new Vector3(3.17f, 3.55f, 4.0f), new Vector3(0.03f, 1.6f, 1.6f), Ice);
        Prop(new Vector3(4.6f, 2.4f, 2.17f), new Vector3(2.9f, 3.2f, 0.03f), Ice);
        // Racks of frozen stores, rimed white on top.
        for (int i = 0; i < 3; i++)
        {
            Prop(new Vector3(5.5f, 0.5f + i * 0.75f, 4.3f), new Vector3(0.7f, 0.06f, 2.8f), new Color("3b4a5a"));
            for (int k = 0; k < 4; k++)
                Prop(new Vector3(5.5f, 0.68f + i * 0.75f, 3.2f + k * 0.7f), new Vector3(0.5f, 0.3f, 0.5f), k % 2 == 0 ? Teal : Frost);
        }
        // A blue light strip round the door, and the sign.
        Glow(new Vector3(2.94f, 2.72f, 4.0f), new Vector3(0.04f, 0.06f, 1.5f), new Color("8fd3ff"), 3f);
        foreach (float z in new[] { 3.25f, 4.75f })
            Glow(new Vector3(2.94f, 1.35f, z), new Vector3(0.04f, 2.7f, 0.06f), new Color("8fd3ff"), 3f);
        Label(new Vector3(2.93f, 3.15f, 4.0f), new Vector3(0, -90, 0), "COLD STORE  -60°C", 60, new Color("8fd3ff"));
    }

    void BuildLights()
    {
        var warm = new Color("ffe2b8");
        AddChild(new OmniLight3D { Position = new Vector3(0, 3.9f, -4.5f), OmniRange = 9, LightEnergy = 1.1f, LightColor = warm });
        AddChild(new OmniLight3D { Position = new Vector3(0, 3.9f, 2.5f), OmniRange = 9, LightEnergy = 1.0f, LightColor = warm });
        AddChild(new OmniLight3D { Position = new Vector3(-4.2f, 1.2f, -1.4f), OmniRange = 3.2f, LightEnergy = 0.8f, LightColor = new Color("2ec4b6") });
        AddChild(new OmniLight3D { Position = new Vector3(4.6f, 3.4f, 4.1f), OmniRange = 5, LightEnergy = 1.2f, LightColor = new Color("8fd3ff") });
    }

    // ---- Helpers ---------------------------------------------------------------------

    static Mesh Disc(float radius) => new CylinderMesh { TopRadius = radius, BottomRadius = radius, Height = 0.01f, RadialSegments = 64 };

    /// <summary>A box you can see and bump into.</summary>
    void Solid(Vector3 centre, Vector3 size, Color colour)
    {
        var body = new StaticBody3D { CollisionLayer = Layers.World, CollisionMask = 0, Position = centre };
        body.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = size } });
        body.AddChild(new MeshInstance3D { Mesh = new BoxMesh { Size = size }, MaterialOverride = Material(colour, false) });
        AddChild(body);
    }

    /// <summary>A box you can only see: trim, livery, decals.</summary>
    MeshInstance3D Prop(Vector3 centre, Vector3 size, Color colour) => Prop(centre, new BoxMesh { Size = size }, colour);

    MeshInstance3D Prop(Vector3 centre, Mesh mesh, Color colour)
    {
        var view = new MeshInstance3D { Mesh = mesh, MaterialOverride = Material(colour, false), Position = centre };
        AddChild(view);
        return view;
    }

    void Glow(Vector3 centre, Vector3 size, Color colour, float energy)
    {
        var m = new StandardMaterial3D { AlbedoColor = colour, EmissionEnabled = true, Emission = colour, EmissionEnergyMultiplier = energy };
        AddChild(new MeshInstance3D { Mesh = new BoxMesh { Size = size }, MaterialOverride = m, Position = centre, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off });
    }

    void Label(Vector3 at, Vector3 rotationDegrees, string text, int size, Color colour)
    {
        AddChild(new Label3D
        {
            Text = text,
            Font = Theme.UiTheme.Display3D,
            FontSize = size,
            PixelSize = 0.0022f,
            Modulate = colour,
            OutlineSize = 0,
            Shaded = true,
            DoubleSided = false,
            Position = at,
            RotationDegrees = rotationDegrees,
        });
    }

    StandardMaterial3D Material(Color colour, bool glow)
    {
        if (_materials.TryGetValue((colour, glow), out var m)) return m;
        m = new StandardMaterial3D { AlbedoColor = colour, Roughness = 0.78f };
        _materials[(colour, glow)] = m;
        return m;
    }
}
