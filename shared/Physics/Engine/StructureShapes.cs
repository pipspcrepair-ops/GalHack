using System.Collections.Generic;
using System.Linq;
using Godot;

namespace GalHack.Physics.Engine;

/// <summary>
/// A structure's collision shapes on its one body, kept per chunk (see
/// <see cref="StructureCollision"/>). Applying new chunk colliders swaps only the chunks whose
/// hash changed; every other chunk keeps its shape nodes, so the engine keeps their cooked
/// shapes and only re-links the body's compound.
///
/// The same per-chunk colliders also furnish a cabin world's motionless copy of the interior.
/// </summary>
public sealed class StructureShapes
{
    CollisionObject3D _owner;
    readonly Dictionary<ChunkKey, (ulong hash, List<CollisionShape3D> shapes)> _chunks = new();

    public StructureShapes(CollisionObject3D owner) => _owner = owner;

    public CollisionObject3D Owner => _owner;
    public int ShapeCount { get; private set; }

    /// <summary>Chunks whose shapes were swapped by the last <see cref="Apply"/>.</summary>
    public int LastRebuilt { get; private set; }

    /// <summary>
    /// Brings the shapes in line with these chunks' colliders. Chunks not listed are kept,
    /// except those in <paramref name="removed"/>, which lose their shapes.
    /// </summary>
    public void Apply(IEnumerable<ChunkColliders> chunks, IEnumerable<ChunkKey> removed = null)
    {
        // A still body (a base, a wreck, a cabin world) is out of its space while its shapes change: in it, Jolt rebuilt the body's
        // whole compound for every shape added, so a crashed freighter's chunk of 300 shapes on a body of thousands took 60 to 100 ms
        // a tick for a thousand ticks. Out of it, the compound is built once as it goes back. (A moving body keeps its space: it has
        // a velocity and contacts to keep.)
        Rid space = default;
        bool still = _owner is StaticBody3D && _owner.IsInsideTree();
        if (still)
        {
            space = PhysicsServer3D.BodyGetSpace(_owner.GetRid());
            PhysicsServer3D.BodySetSpace(_owner.GetRid(), new Rid());
        }
        try { Swap(chunks, removed); }
        finally { if (still) PhysicsServer3D.BodySetSpace(_owner.GetRid(), space); }
    }

    void Swap(IEnumerable<ChunkColliders> chunks, IEnumerable<ChunkKey> removed)
    {
        LastRebuilt = 0;
        if (removed != null)
            foreach (ChunkKey key in removed)
                if (_chunks.Remove(key, out var old))
                {
                    Free(old.shapes);
                    LastRebuilt++;
                }

        foreach (ChunkColliders c in chunks)
        {
            if (_chunks.TryGetValue(c.Chunk, out var had))
            {
                if (had.hash == c.Hash) continue;
                Free(had.shapes);
            }
            if (c.IsEmpty)
            {
                _chunks.Remove(c.Chunk);
                LastRebuilt++;
                continue;
            }
            var shapes = new List<CollisionShape3D>(c.Boxes.Count + c.Hulls.Count);
            foreach (BoxCollider box in c.Boxes)
                shapes.Add(new CollisionShape3D { Shape = new BoxShape3D { Size = box.Size.ToGodot() }, Position = box.Center.ToGodot() });
            foreach (HullCollider hull in c.Hulls)
                shapes.Add(new CollisionShape3D { Shape = new ConvexPolygonShape3D { Points = hull.Points.Select(p => p.ToGodot()).ToArray() } });
            foreach (CollisionShape3D s in shapes) _owner.AddChild(s);
            ShapeCount += shapes.Count;
            _chunks[c.Chunk] = (c.Hash, shapes);
            LastRebuilt++;
        }
    }

    /// <summary>Moves every shape to another body (a vessel becoming a free wreck keeps its exact shapes).</summary>
    public void MoveTo(CollisionObject3D owner)
    {
        foreach (var (_, shapes) in _chunks.Values)
            foreach (CollisionShape3D s in shapes)
                s.Reparent(owner, false);
        _owner = owner;
    }

    void Free(List<CollisionShape3D> shapes)
    {
        foreach (CollisionShape3D s in shapes)
        {
            _owner.RemoveChild(s);
            s.QueueFree();
        }
        ShapeCount -= shapes.Count;
    }
}

/// <summary>
/// A structure's cabin world (walkable-ship's proven approach): a never-drawn SubViewport with
/// its own World3D, holding a motionless copy of the structure's colliders at structure-local
/// coordinates. Anyone aboard walks here, so however the ship flies, the character controller
/// only ever sees a still room with the core's gravity pointing at the floor.
/// </summary>
public partial class CabinWorld : SubViewport
{
    public StaticBody3D Cabin { get; private set; }
    public StructureShapes Shapes { get; private set; }

    public CabinWorld() { }

    public static CabinWorld Create(string name)
    {
        var world = new CabinWorld
        {
            Name = name,
            OwnWorld3D = true,
            RenderTargetUpdateMode = UpdateMode.Disabled,
            Size = new Vector2I(2, 2),
        };
        world.Cabin = new StaticBody3D { Name = "Cabin", CollisionLayer = Layers.Interior, CollisionMask = 0 };
        world.AddChild(world.Cabin);
        world.Shapes = new StructureShapes(world.Cabin);
        return world;
    }

    /// <summary>The cabin's own gravity for engine bodies in it (loose cargo); characters get theirs from the core.</summary>
    public void SetGravity(CoreGravity core)
    {
        if (World3D == null) return;
        Rid space = World3D.Space;
        PhysicsServer3D.AreaSetParam(space, PhysicsServer3D.AreaParameter.Gravity, core.On ? core.Strength : 0f);
        PhysicsServer3D.AreaSetParam(space, PhysicsServer3D.AreaParameter.GravityVector, Vector3.Down);
    }
}
