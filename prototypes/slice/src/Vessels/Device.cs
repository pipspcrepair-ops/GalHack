using System.Collections.Generic;
using System.Linq;
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

    /// <summary>The climate node's item: a small burner on the ground, its hopper its only fittings, no constructor.</summary>
    public const string ClimateItem = "climate-node";

    public bool IsClimate => ItemId == ClimateItem;

    /// <summary>The water pump's item: stands in open water, burns fuel from its tank and pumps water into it, or into a chosen core's storage in reach.</summary>
    public const string PumpItem = "water-pump";

    public bool IsPump => ItemId == PumpItem;

    /// <summary>The core this pump sends its water to (the player's choice), or null: the water stays in the pump's own tank.</summary>
    public Vessel Target;

    /// <summary>Water pumped since the pump was set down, units.</summary>
    public int Pumped;

    double _pumpLeft;

    /// <summary>A climate node with fuel in its burner: the air round it holds steady (<see cref="Slice.Player.Suit"/>).</summary>
    public bool Burning => _burnLeft > 0.0;

    double _burnLeft;
    SurfaceZone _zone;

    public static Device PlaceClimate(SurfaceZone zone, Vector3 at, float yaw)
    {
        var storage = new SharedStorage(zone.Data.Items, "Climate node", 200);
        var d = new Device { Name = "ClimateNode", Label = "Climate node", ItemId = ClimateItem, _zone = zone };
        d.Grid = new ItemGrid(storage, 4, 2, growable: false);
        zone.AddChild(d);
        d.GlobalTransform = new Transform3D(new Basis(Vector3.Up, yaw), at);
        zone.Devices.Add(d);
        return d;
    }

    public static Device PlacePump(SurfaceZone zone, Vector3 at, float yaw)
    {
        var rules = zone.Data.Rules.WaterPump;
        var storage = new SharedStorage(zone.Data.Items, "Water pump", rules.Capacity + rules.FuelRoom);
        var d = new Device { Name = "WaterPump", Label = "Water pump", ItemId = PumpItem, _zone = zone };
        d.Grid = new ItemGrid(storage, 4, 3, growable: false);
        zone.AddChild(d);
        d.GlobalTransform = new Transform3D(new Basis(Vector3.Up, yaw), at);
        zone.Devices.Add(d);
        return d;
    }

    /// <summary>
    /// Why a spot is no place for a pump, or null if it is: it stands in open water (a lake, river or sea, at a
    /// wading depth), never lava, never ice, never dry ground.
    /// </summary>
    public static string PumpSite(SurfaceZone zone, Vector3 at)
    {
        var rules = zone.Data.Rules.WaterPump;
        if (zone.Water.IsLava) return "LAVA · NOT WATER";
        double x = Planets.Gen.Mathx.WrapX(at.X, zone.Planet.Width), z = Planets.Gen.Mathx.Wrap(at.Z, zone.Planet.Height);
        if (zone.Planet.Palette.BiomeAt(x, z) == Planets.Gen.Biome.FrozenSea) return "ICE · NOT OPEN WATER";
        if (!zone.Water.Under(at + Vector3.Up * 0.05f, out float depth)) return "NEEDS OPEN WATER";
        if (depth < rules.MinDepth) return "TOO SHALLOW";
        if (depth > rules.MaxDepth) return "TOO DEEP";
        return null;
    }

    /// <summary>The cores this pump could send to: structures with a core whose area (the build area rule) holds it, nearest first.</summary>
    public List<Vessel> CoresInRadius() =>
        _zone.Vessels.Where(v => Slice.Construction.StructureArea.Linkable(v) && Slice.Construction.StructureArea.Contains(v, GlobalPosition, _zone.Data.Rules.Build.Area))
            .OrderBy(v => Slice.Construction.StructureArea.Distance(v, GlobalPosition)).ToList();

    /// <summary>The target, if it is still a core in reach.</summary>
    public Vessel LiveTarget => Target != null && CoresInRadius().Contains(Target) ? Target : null;

    /// <summary>Steps the target through each core in the radius, then NONE.</summary>
    public void CycleTarget()
    {
        var near = CoresInRadius();
        int at = Target == null ? -1 : near.IndexOf(Target);
        Target = at + 1 < near.Count ? near[at + 1] : null;
    }

    /// <summary>Water in the pump's own tank.</summary>
    public int Water => (int)Storage.Count("water");

    bool HasRoom(Vessel t) => t.Storage.CapacitySu - t.Storage.UsedSu >= _zone.Data.Items.Item("water").VolumeSu;

    /// <summary>Pumps while there is fuel and somewhere for the water to go: the target core while it is in reach with room, else the tank.</summary>
    void Pump(double dt)
    {
        // Long steps (a test fast-forwarding) run in slices, so fuel is spent as it burns.
        while (dt > 0) { double step = System.Math.Min(dt, 5.0); dt -= step; PumpStep(step); }
    }

    void PumpStep(double dt)
    {
        var rules = _zone.Data.Rules.WaterPump;
        Vessel target = LiveTarget;
        bool needs = (target != null && HasRoom(target)) || Water < rules.Capacity;
        if (needs) Burn(dt, rules.Fuels);
        else _burnLeft = System.Math.Max(0.0, _burnLeft - dt);
        if (!Burning || !needs) return;
        _pumpLeft += dt;
        while (_pumpLeft >= rules.SecondsPerWater)
        {
            _pumpLeft -= rules.SecondsPerWater;
            target = LiveTarget;
            if (target != null && HasRoom(target) && target.Storage.Add("water", 1) > 0) Pumped++;
            else if (Water < rules.Capacity && Storage.Add("water", 1) > 0) Pumped++;
            else { _pumpLeft = 0; break; }
        }
    }

    /// <summary>Burns on, taking the next unit of fuel from the hopper when the last runs out.</summary>
    void Burn(double dt) => Burn(dt, _zone.Data.Rules.ClimateNode.Fuels);

    void Burn(double dt, IReadOnlyDictionary<string, double> fuels)
    {
        _burnLeft = System.Math.Max(0.0, _burnLeft - dt);
        if (_burnLeft > 0.0) return;
        foreach (var (fuel, seconds) in fuels)
            if (Storage.Count(fuel) > 0 && Storage.Remove(fuel, 1) > 0)
            {
                _burnLeft = seconds;
                return;
            }
    }

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

    /// <summary>
    /// A device's look, meshes only: the climate node is a squat vented drum on a plinth, its ring lit
    /// while it burns; anything else a constructor's cabinet on hazard feet. Procedural, no assets.
    /// The inventory icon is this same model.
    /// </summary>
    static Node3D Look(string itemId, out MeshInstance3D panel, out StandardMaterial3D panelMaterial)
    {
        var root = new Node3D { Name = "Look" };
        var dark = new StandardMaterial3D { AlbedoColor = new Color("1b263b"), Roughness = 0.7f };
        MeshInstance3D Add(Mesh mesh, Vector3 at, Material material)
        {
            var m = new MeshInstance3D { Mesh = mesh, Position = at, MaterialOverride = material };
            root.AddChild(m);
            return m;
        }
        if (itemId == PumpItem)
        {
            var body = new StandardMaterial3D { AlbedoColor = new Color("3a6ea5"), Roughness = 0.5f, Metallic = 0.45f };
            panelMaterial = new StandardMaterial3D { AlbedoColor = new Color("48cae4"), EmissionEnabled = true, Emission = new Color("48cae4"), EmissionEnergyMultiplier = 0.15f };
            // A sealed motor drum on a flat footing, an intake pipe beside it and a spout over the drum.
            Add(new BoxMesh { Size = new Vector3(1.0f, 0.1f, 0.8f) }, Vector3.Up * 0.05f, dark);
            Add(new CylinderMesh { TopRadius = 0.3f, BottomRadius = 0.34f, Height = 0.7f }, new Vector3(-0.2f, 0.45f, 0f), body);
            panel = Add(new CylinderMesh { TopRadius = 0.33f, BottomRadius = 0.33f, Height = 0.07f }, new Vector3(-0.2f, 0.62f, 0f), panelMaterial);
            Add(new CylinderMesh { TopRadius = 0.1f, BottomRadius = 0.1f, Height = 0.9f }, new Vector3(0.28f, 0.55f, 0f), body);
            Add(new BoxMesh { Size = new Vector3(0.45f, 0.1f, 0.1f) }, new Vector3(0.05f, 1.0f, 0f), body);
            Add(new CylinderMesh { TopRadius = 0.06f, BottomRadius = 0.06f, Height = 0.2f }, new Vector3(-0.15f, 0.9f, 0f), dark);
            return root;
        }
        if (itemId == ClimateItem)
        {
            var drum = new StandardMaterial3D { AlbedoColor = new Color("6c757d"), Roughness = 0.5f, Metallic = 0.5f };
            panelMaterial = new StandardMaterial3D { AlbedoColor = new Color("2ec4b6"), EmissionEnabled = true, Emission = new Color("2ec4b6"), EmissionEnergyMultiplier = 0.15f };
            Add(new CylinderMesh { TopRadius = 0.55f, BottomRadius = 0.55f, Height = 0.12f }, Vector3.Up * 0.06f, dark);
            Add(new CylinderMesh { TopRadius = 0.32f, BottomRadius = 0.42f, Height = 0.9f }, Vector3.Up * 0.57f, drum);
            panel = Add(new CylinderMesh { TopRadius = 0.36f, BottomRadius = 0.37f, Height = 0.08f }, Vector3.Up * 0.72f, panelMaterial);
            Add(new CylinderMesh { TopRadius = 0.18f, BottomRadius = 0.3f, Height = 0.14f }, Vector3.Up * 1.09f, dark);
            return root;
        }
        var shell = new StandardMaterial3D { AlbedoColor = new Color("0a9396"), Roughness = 0.55f, Metallic = 0.3f };
        panelMaterial = new StandardMaterial3D { AlbedoColor = new Color("f4a100"), EmissionEnabled = true, Emission = new Color("f4a100"), EmissionEnergyMultiplier = 0.8f };
        Add(new BoxMesh { Size = new Vector3(1.1f, 1.15f, 0.8f) }, Vector3.Up * 0.575f, shell);
        Add(new BoxMesh { Size = new Vector3(1.16f, 0.12f, 0.86f) }, Vector3.Up * 1.2f, dark);
        Add(new BoxMesh { Size = new Vector3(0.7f, 0.35f, 0.02f) }, new Vector3(0f, 0.85f, -0.41f), dark);
        panel = Add(new BoxMesh { Size = new Vector3(0.62f, 0.08f, 0.02f) }, new Vector3(0f, 0.45f, -0.41f), panelMaterial);
        // Hazard feet.
        foreach (float x in new[] { -0.45f, 0.45f })
            Add(new BoxMesh { Size = new Vector3(0.18f, 0.08f, 0.9f) }, new Vector3(x, 0.04f, 0f), dark);
        return root;
    }

    /// <summary>The model of a device item, as the world and the inventory icon show it.</summary>
    public static ItemModel Model(bool climate) => new() { Root = Look(climate ? ClimateItem : "survival-constructor", out _, out _) };

    /// <summary>The water pump's model.</summary>
    public static ItemModel PumpModel() => new() { Root = Look(PumpItem, out _, out _) };

    public override void _Ready()
    {
        AddChild(Look(ItemId, out _panel, out _panelMaterial));
        _body = new StaticBody3D { Name = "Body", CollisionLayer = Layers.Props, CollisionMask = 0 };
        _body.AddChild(IsClimate
            ? new CollisionShape3D { Shape = new CylinderShape3D { Radius = 0.5f, Height = 1.2f }, Position = Vector3.Up * 0.6f }
            : IsPump
            ? new CollisionShape3D { Shape = new BoxShape3D { Size = new Vector3(1.0f, 1.1f, 0.8f) }, Position = Vector3.Up * 0.55f }
            : new CollisionShape3D { Shape = new BoxShape3D { Size = new Vector3(1.1f, 1.25f, 0.8f) }, Position = Vector3.Up * 0.625f });
        AddChild(_body);
    }

    /// <summary>The device's collider (rays that hit it mean it).</summary>
    public GodotObject Collider => _body;

    /// <summary>Runs its constructor; its panel glows while it works.</summary>
    public void Tick(double dt)
    {
        if (IsClimate)
        {
            Burn(dt);
            _panelMaterial.EmissionEnergyMultiplier = Burning ? 2.2f : 0.15f;
            return;
        }
        if (IsPump)
        {
            Pump(dt);
            _panelMaterial.EmissionEnergyMultiplier = Burning ? 2.2f : 0.15f;
            return;
        }
        Constructor.Tick(dt);
        if (_panelMaterial != null) _panelMaterial.EmissionEnergyMultiplier = Constructor.Current != null ? 2.2f : 0.6f;
    }

    /// <summary>Where it stands, at the middle of its front.</summary>
    public Vector3 Front => GlobalTransform * new Vector3(0f, 0.8f, -0.45f);
}
