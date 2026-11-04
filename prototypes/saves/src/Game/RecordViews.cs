using System.Collections.Generic;
using System.Linq;
using GalHack.Persistence;
using GalHack.Records;
using Godot;

namespace Saves;

/// <summary>
/// Where a record's nodes go. In a zone, its body and its look both hang off the zone's root. In
/// a ship's cabin, the body goes in the cabin world (where players aboard walk) and the look hangs
/// off the ship (which is drawn), both at the same ship-local position.
/// </summary>
public readonly record struct Frame(Node BodyParent, Node3D VisualParent, uint Layer, Ship Ship)
{
    public static Frame InZone(Node3D root) => new(root, root, Layers.World, null);
    public static Frame InCabin(Ship ship) => new(ship.CabinWorld, ship, Layers.Interior, ship);
}

/// <summary>
/// The node standing for one record: its look, plus (elsewhere, maybe in a cabin world) its
/// body. Records are the truth and these are only views: when a record changes, its view is
/// thrown away and made again. Only ships and players write anything back.
/// </summary>
public partial class RecordView : Node3D
{
    public string RecordId { get; init; }
    public Node3D Body { get; set; }

    /// <summary>Frees the look and the body together.</summary>
    public void Despawn()
    {
        if (IsInstanceValid(Body)) Body.QueueFree();
        QueueFree();
    }
}

/// <summary>Makes the nodes for structures, crates, loose stacks and scorch marks.</summary>
public static class RecordViews
{
    public static readonly Color[] Paints = { new("e9dfc4"), new("e85d04"), new("0a9396"), new("c1121f") };

    /// <summary>Set on every body, so the look ray can tell which record it hit.</summary>
    public const string RecordMeta = "record_id";

    public static RecordView Spawn(SaveRecord record, Frame frame) => record.Type switch
    {
        RecordTypes.Structure => Structure(record, frame),
        RecordTypes.Container => Crate(record, frame),
        RecordTypes.Drop => Drop(record, frame),
        RecordTypes.Terrain => Scorches(record, frame),
        RecordTypes.Player => StandIn(record, frame),
        _ => Unknown(record, frame),
    };

    static Vector3 V(double[] p) => new((float)p[0], (float)p[1], (float)p[2]);

    static RecordView Structure(SaveRecord record, Frame frame)
    {
        var data = GameRecords.Read<StructureData>(record);
        Vector3 origin = V(data.Origin);
        var view = new RecordView { RecordId = record.Id, Name = record.Id, Position = origin };
        var body = new StaticBody3D { Name = record.Id + "-body", CollisionLayer = frame.Layer, CollisionMask = 0, Position = origin };
        body.SetMeta(RecordMeta, record.Id);
        // Shared by this structure's blocks only: nothing Godot-side is kept in statics, so nothing outlives the scene.
        var cube = new BoxMesh { Size = Vector3.One };
        var paints = new Dictionary<int, StandardMaterial3D>();
        // One shape per block, in block order: the look ray's shape index is the block's index.
        foreach (int[] b in data.Blocks)
        {
            var center = new Vector3(b[0] + 0.5f, b[1] + 0.5f, b[2] + 0.5f);
            view.AddChild(new MeshInstance3D { Mesh = cube, MaterialOverride = Paint(paints, b[3]), Position = center });
            body.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = Vector3.One }, Position = center });
        }
        if (data.Mode == CoreModes.CapitalShip)
            view.AddChild(Label($"{data.Name.ToUpperInvariant()} · CORE: CAPITAL SHIP", new Vector3(3f, 3.8f, 2.5f), 64));
        return Place(view, body, frame);
    }

    static RecordView Crate(SaveRecord record, Frame frame)
    {
        var data = GameRecords.Read<ContainerData>(record);
        bool derelict = record.Owner == null;
        var transform = new Transform3D(new Basis(Vector3.Up, (float)data.Yaw), V(data.Position));
        var view = new RecordView { RecordId = record.Id, Name = record.Id, Transform = transform };
        var size = new Vector3(1.3f, 0.9f, 0.9f);

        Color shell = derelict ? new Color("5b6c5d") : new Color("e85d04");
        Color band = derelict ? new Color("c1121f") : new Color("0a9396");
        view.AddChild(Box(size, shell, new Vector3(0, size.Y / 2f, 0)));
        view.AddChild(Box(new Vector3(size.X + 0.02f, 0.16f, size.Z + 0.02f), band, new Vector3(0, size.Y * 0.55f, 0)));
        view.AddChild(Box(new Vector3(size.X + 0.04f, 0.08f, size.Z + 0.04f), new Color("e9dfc4"), new Vector3(0, size.Y, 0)));
        string contents = Stacks.Describe(data.Stacks);
        view.AddChild(Label($"{(data.Label ?? "crate").ToUpperInvariant()}\n{contents}", new Vector3(0, size.Y + 0.55f, 0), 32));

        var body = new StaticBody3D { Name = record.Id + "-body", CollisionLayer = frame.Layer, CollisionMask = 0, Transform = transform };
        body.SetMeta(RecordMeta, record.Id);
        body.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = size }, Position = new Vector3(0, size.Y / 2f, 0) });
        return Place(view, body, frame);
    }

    static RecordView Drop(SaveRecord record, Frame frame)
    {
        var data = GameRecords.Read<DropData>(record);
        var view = new RecordView { RecordId = record.Id, Name = record.Id, Position = V(data.Position) };
        var glow = new StandardMaterial3D
        {
            AlbedoColor = new Color("f4a100"),
            EmissionEnabled = true,
            Emission = new Color("f4a100"),
            EmissionEnergyMultiplier = 1.4f,
        };
        Vector3[] spots = { new(0, 0.12f, 0), new(0.22f, 0.1f, 0.1f), new(-0.12f, 0.1f, 0.2f), new(0.05f, 0.32f, 0.05f) };
        for (int i = 0; i < spots.Length; i++)
            view.AddChild(new MeshInstance3D { Mesh = new BoxMesh { Size = Vector3.One * (0.24f - i * 0.02f) }, MaterialOverride = glow, Position = spots[i], RotationDegrees = new Vector3(0, i * 27f, 0) });
        view.AddChild(Label(Stacks.Describe(data.Stacks), new Vector3(0, 0.8f, 0), 28));

        var body = new StaticBody3D { Name = record.Id + "-body", CollisionLayer = Layers.Pickup, CollisionMask = 0, Position = V(data.Position) };
        body.SetMeta(RecordMeta, record.Id);
        body.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = new Vector3(0.7f, 0.6f, 0.7f) }, Position = new Vector3(0, 0.3f, 0) });
        return Place(view, body, frame);
    }

    static RecordView Scorches(SaveRecord record, Frame frame)
    {
        var data = GameRecords.Read<TerrainData>(record);
        var view = new RecordView { RecordId = record.Id, Name = record.Id };
        var burnt = new StandardMaterial3D
        {
            AlbedoColor = new Color(0.16f, 0.08f, 0.05f, 0.55f),
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
            Roughness = 1f,
        };
        foreach (double[] s in data.Scorches)
        {
            view.AddChild(new MeshInstance3D
            {
                Mesh = new CylinderMesh { TopRadius = (float)s[2], BottomRadius = (float)s[2], Height = 0.02f, RadialSegments = 32 },
                MaterialOverride = burnt,
                Position = new Vector3((float)s[0], 0.012f, (float)s[1]),
            });
        }
        return Place(view, null, frame);
    }

    /// <summary>A player who isn't the local one (or any player, in tests): a capsule where their record says.</summary>
    static RecordView StandIn(SaveRecord record, Frame frame)
    {
        var data = GameRecords.Read<PlayerData>(record);
        var view = new RecordView { RecordId = record.Id, Name = record.Id, Position = V(data.Position) };
        view.AddChild(new MeshInstance3D
        {
            Mesh = new CapsuleMesh { Radius = 0.35f, Height = 1.8f },
            MaterialOverride = new StandardMaterial3D { AlbedoColor = ShipLayout.Yellow },
            Position = new Vector3(0, 0.9f, 0),
        });
        return Place(view, null, frame);
    }

    /// <summary>A type this build can't show is still shown, as a marked box: never dropped.</summary>
    static RecordView Unknown(SaveRecord record, Frame frame)
    {
        var view = new RecordView { RecordId = record.Id, Name = record.Id };
        view.AddChild(Box(Vector3.One * 0.5f, Colors.Magenta, Vector3.Zero));
        view.AddChild(Label($"unknown record {record.Id} ({record.Type})", new Vector3(0, 0.8f, 0), 24));
        return Place(view, null, frame);
    }

    static RecordView Place(RecordView view, StaticBody3D body, Frame frame)
    {
        frame.VisualParent.AddChild(view);
        if (body != null)
        {
            frame.BodyParent.AddChild(body);
            view.Body = body;
        }
        return view;
    }

    static StandardMaterial3D Paint(Dictionary<int, StandardMaterial3D> made, int paint)
    {
        if (!made.TryGetValue(paint, out StandardMaterial3D m))
        {
            m = new StandardMaterial3D { AlbedoColor = Paints[Mathf.PosMod(paint, Paints.Length)], Roughness = 0.8f };
            made[paint] = m;
        }
        return m;
    }

    static MeshInstance3D Box(Vector3 size, Color color, Vector3 position) => new()
    {
        Mesh = new BoxMesh { Size = size },
        MaterialOverride = new StandardMaterial3D { AlbedoColor = color, Roughness = 0.7f },
        Position = position,
    };

    public static Label3D Label(string text, Vector3 position, int fontSize) => new()
    {
        Text = text,
        Position = position,
        FontSize = fontSize,
        PixelSize = 0.006f,
        Billboard = BaseMaterial3D.BillboardModeEnum.Enabled,
        Modulate = new Color("ffe8c2"),
        OutlineModulate = new Color(0.08f, 0.05f, 0.03f, 0.9f),
        OutlineSize = 10,
        NoDepthTest = false,
    };

    /// <summary>Every view and body for a record in a list of spawned nodes, for tests.</summary>
    public static IEnumerable<RecordView> All(IEnumerable<Node> nodes) => nodes.OfType<RecordView>();
}
