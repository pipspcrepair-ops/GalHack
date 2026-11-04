using System.Collections.Generic;
using Godot;
using PlanetSurface.World;
using TerrainDigging.Terrain;

namespace PlanetSurface.Player;

/// <summary>
/// Placed building blocks: a plated 2 m block with a hazard stripe, in the Foss-style livery
/// structures and ships use, and a solid collider. Every structure in the store is shown,
/// including the copies a block placed near a seam has on the other side.
/// </summary>
public partial class StructureView : Node3D
{
    readonly Dictionary<int, Node3D> _blocks = new();
    StandardMaterial3D _body, _stripe;

    public override void _Ready()
    {
        _body = new StandardMaterial3D { AlbedoColor = new Color("d8d2c4"), Roughness = 0.55f, Metallic = 0.3f };
        _stripe = new StandardMaterial3D { AlbedoColor = new Color("e0602a"), Roughness = 0.5f };
    }

    /// <summary>Matches the shown blocks to the store's structures (after a place, a load or a planet switch).</summary>
    public void Sync(IReadOnlyList<Structure> structures)
    {
        var seen = new HashSet<int>();
        foreach (Structure s in structures)
        {
            seen.Add(s.Id);
            if (_blocks.ContainsKey(s.Id)) continue;
            _blocks[s.Id] = Build(s);
        }
        var gone = new List<int>();
        foreach (int id in _blocks.Keys) if (!seen.Contains(id)) gone.Add(id);
        foreach (int id in gone)
        {
            _blocks[id].QueueFree();
            _blocks.Remove(id);
        }
    }

    Node3D Build(Structure s)
    {
        Vector3 size = new(s.Box.Size.X, s.Box.Size.Y, s.Box.Size.Z);
        var node = new StaticBody3D { Name = $"Block {s.Id}", CollisionLayer = Layers.Structure, CollisionMask = 0 };
        node.AddChild(new MeshInstance3D { Mesh = new BoxMesh { Size = size * 0.995f }, MaterialOverride = _body });
        node.AddChild(new MeshInstance3D { Mesh = new BoxMesh { Size = new Vector3(size.X * 1.002f, size.Y * 0.18f, size.Z * 1.002f) }, MaterialOverride = _stripe });
        node.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = size } });
        AddChild(node);
        node.Position = new Vector3(s.Box.Min.X, s.Box.Min.Y, s.Box.Min.Z) + size * 0.5f;
        return node;
    }

    public void Clear() => Sync(new List<Structure>());
}
