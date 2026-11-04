using Godot;

namespace Weather;

/// <summary>
/// A lone weather mast on the spire hill: banded cream and orange like a 1970s launch gantry,
/// with a copper lightning rod on top. It is the highest point for a long way, so lightning
/// storms keep hitting it.
/// </summary>
public partial class Spire : StaticBody3D
{
    public const float Height = 50f;

    /// <summary>The tip of the rod, in world space, once the spire is in the tree.</summary>
    public Vector3 RodTip => GlobalPosition + Vector3.Up * (Height + 7f);

    OmniLight3D _beacon;
    StandardMaterial3D _beaconMaterial;
    float _time;

    public override void _Ready()
    {
        CollisionLayer = Layers.World;
        CollisionMask = 0;
        var cream = Material(new Color("e9dfc4"));
        var orange = Material(new Color("e85d04"));
        var dark = Material(new Color("2b2d42"));
        var copper = new StandardMaterial3D { AlbedoColor = new Color("c77d3a"), Metallic = 0.8f, Roughness = 0.3f };

        // Footing: a squat dark plinth sunk into the hilltop.
        Box(new Vector3(9f, 3f, 9f), new Vector3(0, 0.5f, 0), dark);
        // The column: bands that narrow upward, alternating cream and orange.
        float y = 2f;
        for (int i = 0; i < 10; i++)
        {
            float w = Mathf.Lerp(4.2f, 1.6f, i / 9f);
            float h = 4.8f;
            Box(new Vector3(w, h, w), new Vector3(0, y + h / 2f, 0), i % 2 == 0 ? cream : orange);
            y += h;
            if (i % 3 == 2) Box(new Vector3(w + 2.4f, 0.4f, w + 2.4f), new Vector3(0, y, 0), dark); // gantry platforms
        }
        // A pod near the top, then the rod.
        Box(new Vector3(3.2f, 2.2f, 3.2f), new Vector3(0, Height - 1.5f, 0), dark);
        AddChild(new MeshInstance3D
        {
            Mesh = new CylinderMesh { TopRadius = 0.18f, BottomRadius = 0.4f, Height = 7.5f, RadialSegments = 8 },
            MaterialOverride = copper,
            Position = new Vector3(0, Height + 3.4f, 0),
        });
        AddChild(new MeshInstance3D
        {
            Mesh = new SphereMesh { Radius = 0.55f, Height = 1.1f, RadialSegments = 12, Rings = 6 },
            MaterialOverride = copper,
            Position = new Vector3(0, Height + 7f, 0),
        });

        _beaconMaterial = new StandardMaterial3D
        {
            AlbedoColor = new Color("ff2a1a"), EmissionEnabled = true, Emission = new Color("ff2a1a"), EmissionEnergyMultiplier = 4f,
        };
        AddChild(new MeshInstance3D
        {
            Mesh = new SphereMesh { Radius = 0.45f, Height = 0.9f, RadialSegments = 12, Rings = 6 },
            MaterialOverride = _beaconMaterial,
            Position = new Vector3(1.8f, Height - 0.2f, 0),
        });
        _beacon = new OmniLight3D { LightColor = new Color("ff3a20"), OmniRange = 14f, LightEnergy = 2f, Position = new Vector3(1.8f, Height, 0) };
        AddChild(_beacon);

        AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = new Vector3(4.2f, Height, 4.2f) }, Position = new Vector3(0, Height / 2f, 0) });
    }

    public override void _Process(double delta)
    {
        _time += (float)delta;
        float on = Mathf.Sin(_time * 3.2f) > 0.55f ? 1f : 0.08f; // an aircraft-warning blink
        _beacon.LightEnergy = 2.5f * on;
        _beaconMaterial.EmissionEnergyMultiplier = 6f * on;
    }

    void Box(Vector3 size, Vector3 at, Material material) =>
        AddChild(new MeshInstance3D { Mesh = new BoxMesh { Size = size }, MaterialOverride = material, Position = at });

    static StandardMaterial3D Material(Color color) => new() { AlbedoColor = color, Roughness = 0.7f };
}
