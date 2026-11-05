using GalHack.Items;
using GalHack.Items.Production;
using GalHack.Physics;
using Godot;
using Slice.Items;
using Slice.Surface;

namespace Slice.Vessels;

/// <summary>
/// A standalone device set down on open ground: the survival constructor, made by the suit
/// fabricator (Empyrion's flow: the suit makes the bare minimum, the placed survival constructor
/// makes the first core, the basics and the next constructor up). It is its own little structure
/// with its own storage (a hopper you load from your bag); its panel is in the Tab menu's Core
/// tab when you stand next to it. It needs no power. Procedural look, no assets.
/// </summary>
public partial class Device : Node3D
{
    public string Label;
    public string ItemId;
    public Constructor Constructor { get; private set; }
    public ItemGrid Grid { get; private set; }
    public SharedStorage Storage => Grid.Storage;
    StaticBody3D _body;
    MeshInstance3D _panel;
    StandardMaterial3D _panelMaterial;

    /// <summary>The hopper's room, storage units.</summary>
    public const double Capacity = 1500;

    public static Device Place(SurfaceZone zone, MachineDef machine, string itemId, Vector3 at, float yaw)
    {
        var storage = new SharedStorage(zone.Data.Items, machine.Name, Capacity);
        var d = new Device { Name = machine.Name.Replace(" ", ""), Label = machine.Name, ItemId = itemId };
        d.Grid = new ItemGrid(storage, 8, 5, growable: true);
        d.Constructor = new Constructor(zone.Data.Items, machine, storage, zone.WorldRules);
        zone.AddChild(d);
        d.GlobalTransform = new Transform3D(new Basis(Vector3.Up, yaw), at);
        zone.Devices.Add(d);
        return d;
    }

    public override void _Ready()
    {
        var shell = new StandardMaterial3D { AlbedoColor = new Color("0a9396"), Roughness = 0.55f, Metallic = 0.3f };
        var dark = new StandardMaterial3D { AlbedoColor = new Color("1b263b"), Roughness = 0.7f };
        _panelMaterial = new StandardMaterial3D { AlbedoColor = new Color("f4a100"), EmissionEnabled = true, Emission = new Color("f4a100"), EmissionEnergyMultiplier = 0.8f };
        AddChild(new MeshInstance3D { Mesh = new BoxMesh { Size = new Vector3(1.1f, 1.15f, 0.8f) }, Position = Vector3.Up * 0.575f, MaterialOverride = shell });
        AddChild(new MeshInstance3D { Mesh = new BoxMesh { Size = new Vector3(1.16f, 0.12f, 0.86f) }, Position = Vector3.Up * 1.2f, MaterialOverride = dark });
        AddChild(new MeshInstance3D { Mesh = new BoxMesh { Size = new Vector3(0.7f, 0.35f, 0.02f) }, Position = new Vector3(0f, 0.85f, -0.41f), MaterialOverride = dark });
        _panel = new MeshInstance3D { Mesh = new BoxMesh { Size = new Vector3(0.62f, 0.08f, 0.02f) }, Position = new Vector3(0f, 0.45f, -0.41f), MaterialOverride = _panelMaterial };
        AddChild(_panel);
        // Hazard feet.
        foreach (float x in new[] { -0.45f, 0.45f })
            AddChild(new MeshInstance3D { Mesh = new BoxMesh { Size = new Vector3(0.18f, 0.08f, 0.9f) }, Position = new Vector3(x, 0.04f, 0f), MaterialOverride = dark });
        _body = new StaticBody3D { Name = "Body", CollisionLayer = Layers.Props, CollisionMask = 0 };
        _body.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = new Vector3(1.1f, 1.25f, 0.8f) }, Position = Vector3.Up * 0.625f });
        AddChild(_body);
    }

    /// <summary>The device's collider (rays that hit it mean it).</summary>
    public GodotObject Collider => _body;

    /// <summary>Runs its constructor; its panel glows while it works.</summary>
    public void Tick(double dt)
    {
        Constructor.Tick(dt);
        if (_panelMaterial != null) _panelMaterial.EmissionEnergyMultiplier = Constructor.Current != null ? 2.2f : 0.6f;
    }

    /// <summary>Where it stands, at the middle of its front.</summary>
    public Vector3 Front => GlobalTransform * new Vector3(0f, 0.8f, -0.45f);
}
