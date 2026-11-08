using System;
using System.Collections.Generic;
using System.Linq;
using GalHack.Items;
using Godot;
using Wildlife;

namespace Slice.Items;

/// <summary>What a ground container is.</summary>
public enum LootKind
{
    /// <summary>A creature's body: its meat, hide and parts.</summary>
    Corpse,
    /// <summary>A dead player's backpack: their bag (the toolbar and the survival tool stay with them). Marked on the compass and map.</summary>
    Backpack,
    /// <summary>Things set down (dropped out of the inventory, or what a full storage couldn't take).</summary>
    Drop,
    /// <summary>
    /// A place's cache (a locker, a crate, a vault's strongbox): part of the place, drawn by it, and
    /// never taken away, empty or not (Empyrion's POI containers stay where they stand).
    /// </summary>
    Cache,
}

/// <summary>A container on the ground: a grid of slots over its own storage, at a place in the zone.</summary>
public sealed class Lootable
{
    public int Id { get; init; }
    public LootKind Kind { get; init; }
    public string Name { get; init; }
    public Vector3 Position;
    public float Radius = 0.6f;
    public ItemGrid Grid { get; init; }
    public SharedStorage Storage => Grid.Storage;
    /// <summary>The creature this is the body of, while the wildlife still draws it.</summary>
    public Creature Creature;
    public Node3D Node;
    public bool IsEmpty => Storage.IsEmpty;
}

/// <summary>
/// Every container lying in the world: creature bodies, dead players' backpacks, and things set
/// down. Each is a grid like any storage (Empyrion's loot window), opened with E. Nothing here is
/// ever deleted while it holds anything: a container goes only once it is empty (and closed).
/// </summary>
public partial class Lootables : Node3D
{
    public ItemCatalogue Items;
    public readonly List<Lootable> All = new();
    int _nextId = 1;

    /// <summary>The container open in the inventory view right now (not removed while open).</summary>
    public Lootable Open;

    /// <summary>A container was emptied and taken away.</summary>
    public event Action<Lootable> Removed;

    public Lootable Create(LootKind kind, string name, Vector3 at, int columns, int rows, Bill contents = null)
    {
        var storage = SharedStorage.Unbounded(Items, name);
        var loot = new Lootable
        {
            Id = _nextId++, Kind = kind, Name = name, Position = at,
            Grid = new ItemGrid(storage, columns, rows, growable: true),
        };
        if (contents != null) storage.AddAll(contents);
        loot.Grid.Reconcile();
        loot.Node = Look(kind);
        AddChild(loot.Node);
        loot.Node.GlobalPosition = at;
        All.Add(loot);
        return loot;
    }

    /// <summary>Puts items into the drop container at a spot (one within 2.5 m takes them), or a new one.</summary>
    public Lootable Spill(Bill items, Vector3 at, string name = "Dropped items")
    {
        if (items == null || items.IsEmpty) return null;
        Lootable near = All.Where(l => l.Kind == LootKind.Drop && l.Position.DistanceTo(at) < 2.5f).OrderBy(l => l.Position.DistanceTo(at)).FirstOrDefault();
        if (near == null) return Create(LootKind.Drop, name, at, 8, 8, items);
        near.Storage.AddAll(items);
        near.Grid.Reconcile();
        return near;
    }

    /// <summary>
    /// The container whose body a ray meets first within reach, or null. Each is a ball round its middle a
    /// little bigger than what's drawn (a cache with its lid, a body's bulk): wherever the look lands on it,
    /// from any side or from above, it's that container, as long as nothing solid stands nearer. The ball is
    /// met where the ray enters it, not at its middle: a look that stops on a cache's own lid (a fifth of a
    /// metre short of its middle and more) is a look at the cache. <paramref name="reach"/> is how far the
    /// look goes before something solid stops it; distance is where it meets the container.
    /// </summary>
    public Lootable Pick(Vector3 eye, Vector3 dir, float reach, out float distance)
    {
        Lootable best = null;
        distance = reach;
        foreach (Lootable l in All)
        {
            Vector3 c = l.Position + Vector3.Up * (l.Kind == LootKind.Corpse ? l.Radius * 0.6f : 0.25f);
            float r = l.Radius + 0.25f, along = (c - eye).Dot(dir), off = (eye + dir * along - c).Length();
            if (off > r) continue;
            float enter = along - MathF.Sqrt(r * r - off * off);
            if (along < 0f || enter > distance) continue;
            distance = MathF.Max(0f, enter);
            best = l;
        }
        return best;
    }

    /// <summary>Where a creature's body is in the zone, while the wildlife still draws it (set by the game).</summary>
    public Func<Creature, Vector3> BodyAt;

    /// <summary>The floor under a point that someone on foot can reach (set by the game): a body that came down on a ledge or a ceiling ends on it.</summary>
    public Func<Vector3, Vector3> Settle;

    /// <summary>Empty ones that aren't open go; a body goes where its creature's body goes (it falls off a ledge, it lets go of a ceiling), and once the creature is cleared away it shows as a bundle on the floor below.</summary>
    public void Tick()
    {
        for (int i = All.Count - 1; i >= 0; i--)
        {
            Lootable l = All[i];
            if (l.IsEmpty && l != Open && l.Kind != LootKind.Cache)
            {
                l.Node.QueueFree();
                All.RemoveAt(i);
                if (l.Creature != null) l.Creature.MeatLeft = 0f; // the wildlife clears the body away
                Removed?.Invoke(l);
                continue;
            }
            if (l.Creature == null) continue;
            if (l.Creature.Removed)
            {
                l.Creature = null;
                if (Settle != null) l.Position = Settle(l.Position);
                l.Node.GlobalPosition = l.Position;
                l.Node.GetNode<Node3D>("Bundle").Visible = true;
            }
            else if (BodyAt != null && BodyAt(l.Creature) is var at && at.DistanceSquaredTo(l.Position) > 1e-4f)
            {
                l.Position = at;
                l.Node.GlobalPosition = at;
            }
        }
    }

    public void Shift(Vector3 by)
    {
        foreach (Lootable l in All)
        {
            l.Position += by;
            l.Node.GlobalPosition = l.Position;
        }
    }

    /// <summary>Procedural looks: a hide-wrapped bundle for a body once its creature is gone, a pack with a beacon for a backpack, a crate for a drop.</summary>
    static Node3D Look(LootKind kind)
    {
        var root = new Node3D { Name = $"Loot{kind}" };
        if (kind == LootKind.Cache) return root; // the place draws its own caches
        Color body = kind switch { LootKind.Backpack => new Color("e85d04"), LootKind.Drop => new Color("6c757d"), _ => new Color("7f5539") };
        var bundle = new MeshInstance3D
        {
            Name = "Bundle",
            Mesh = kind == LootKind.Drop ? new BoxMesh { Size = new Vector3(0.6f, 0.45f, 0.45f) } : new CapsuleMesh { Radius = 0.22f, Height = 0.75f },
            MaterialOverride = new StandardMaterial3D { AlbedoColor = body, Roughness = 0.8f },
            Position = Vector3.Up * 0.22f, RotationDegrees = kind == LootKind.Drop ? Vector3.Zero : new Vector3(0, 0, 90),
            Visible = kind != LootKind.Corpse,
        };
        root.AddChild(bundle);
        // A body is its own mark: it lies where the creature fell, with nothing drawn round it.
        if (kind == LootKind.Corpse) return root;
        // A beacon so it can be found again: amber for things set down, orange for a backpack.
        Color beacon = kind == LootKind.Backpack ? new Color("ff8c1a") : new Color("ffd166");
        root.AddChild(new MeshInstance3D
        {
            Name = "Ring", Mesh = new TorusMesh { InnerRadius = 0.55f, OuterRadius = 0.62f, Rings = 24, RingSegments = 6 },
            MaterialOverride = new StandardMaterial3D { ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded, AlbedoColor = new Color(beacon, 0.8f), Transparency = BaseMaterial3D.TransparencyEnum.Alpha },
            Position = Vector3.Up * 0.04f, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        });
        if (kind == LootKind.Backpack)
            root.AddChild(new OmniLight3D { LightColor = beacon, LightEnergy = 1.2f, OmniRange = 4f, Position = Vector3.Up * 0.8f });
        return root;
    }
}
