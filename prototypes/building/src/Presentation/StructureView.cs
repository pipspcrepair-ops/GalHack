using System.Collections.Generic;
using System.Linq;
using Godot;
using GalHack.Structures;

namespace GalHack.Structures.Presentation;

/// <summary>Physics layers used by structures and whatever stands on them.</summary>
public static class PhysicsLayers
{
    public const uint World = 1u << 0;
    public const uint Structures = 1u << 1;
    public const uint Player = 1u << 2;
}

/// <summary>
/// Draws and collides one <see cref="Structure"/>. It is a rigid body: frozen in place while the
/// structure has a core (a landed ship, a base), free to fall and tumble once it is an unowned
/// wreck. Each 16³ chunk has one mesh and its own group of box and hull colliders, and only
/// chunks the structure marks dirty are rebuilt.
/// </summary>
public partial class StructureView : RigidBody3D
{
    const int MaxLights = 24;

    public Structure Structure { get; private set; }
    public StructureStats Stats { get; private set; }
    public float CellSize => Structure.Grid.CellSize;

    readonly Dictionary<Int3, ChunkNode> _chunks = new();
    readonly ChunkMeshData _scratch = new();
    readonly List<OmniLight3D> _lights = new();
    int _syncedVersion = -1;
    string _lightKey = "";

    sealed class ChunkNode
    {
        public MeshInstance3D Mesh;
        public readonly List<CollisionShape3D> Shapes = new();
    }

    public static StructureView Create(Structure s) => new() { Structure = s, Name = s.Name.Replace(" ", "") };

    public override void _Ready()
    {
        CollisionLayer = PhysicsLayers.Structures;
        CollisionMask = PhysicsLayers.World | PhysicsLayers.Structures;
        FreezeMode = FreezeModeEnum.Static;
        Freeze = !Structure.IsWreck;
        AngularDamp = 0.6f;
        foreach (Int3 c in Structure.Chunks.Keys) Structure.DirtyChunks.Add(c);
        Sync();
    }

    public override void _Process(double delta) => Sync();

    /// <summary>Rebuilds dirty chunks and refreshes stats, mass and lights. Called right after edits so feedback is instant.</summary>
    public void Sync()
    {
        if (Structure.DirtyChunks.Count > 0)
        {
            foreach (Int3 c in Structure.DirtyChunks.ToArray()) RebuildChunk(c);
            Structure.DirtyChunks.Clear();
        }
        if (_syncedVersion == Structure.Version && Stats != null) return;
        _syncedVersion = Structure.Version;
        Stats = StructureStats.Compute(Structure);
        Mass = Mathf.Max(50f, (float)Stats.Mass * 1000f);
        bool wreck = Structure.IsWreck;
        if (Freeze == wreck) Freeze = !wreck; // a wreck falls; claiming it with a core pins it where it lies
        UpdateLights();
    }

    void RebuildChunk(Int3 c)
    {
        if (!_chunks.TryGetValue(c, out ChunkNode node))
        {
            if (!Structure.Chunks.ContainsKey(c)) return;
            node = new ChunkNode { Mesh = new MeshInstance3D { Name = $"Chunk{c.X}_{c.Y}_{c.Z}" } };
            AddChild(node.Mesh);
            _chunks[c] = node;
        }

        foreach (CollisionShape3D shape in node.Shapes)
        {
            RemoveChild(shape);
            shape.QueueFree();
        }
        node.Shapes.Clear();

        if (!Structure.Chunks.ContainsKey(c))
        {
            node.Mesh.QueueFree();
            _chunks.Remove(c);
            return;
        }

        ChunkMesher.Build(Structure, c, _scratch);
        node.Mesh.Mesh = ChunkMesher.ToMesh(_scratch);
        foreach (var (center, size) in _scratch.Boxes)
            node.Shapes.Add(new CollisionShape3D { Shape = new BoxShape3D { Size = size }, Position = center });
        foreach (Vector3[] hull in _scratch.Hulls)
            node.Shapes.Add(new CollisionShape3D { Shape = new ConvexPolygonShape3D { Points = hull } });
        foreach (CollisionShape3D shape in node.Shapes) AddChild(shape);
    }

    /// <summary>Light blocks cast real light, up to a limit per structure.</summary>
    void UpdateLights()
    {
        var lights = Structure.Blocks()
            .Where(x => !x.block.Off && Structure.TypeOf(x.block).IsLight)
            .Take(MaxLights)
            .ToList();
        string key = string.Join(";", lights.Select(l => $"{l.pos}/{l.block.Rot}"));
        if (key == _lightKey) return;
        _lightKey = key;
        foreach (OmniLight3D l in _lights) l.QueueFree();
        _lights.Clear();
        foreach (var (pos, block) in lights)
        {
            Vector3 forward = MeshBuffers.ToGodot(Orientations.Matrix(block.Rot).Apply(new System.Numerics.Vector3(0, 0, -0.25f)));
            var light = new OmniLight3D
            {
                Position = (CellCenter(pos) / CellSize + forward) * CellSize,
                LightColor = ChunkMesher.Rgb(Structure.TypeOf(block).GlowColor),
                LightEnergy = 1.6f,
                OmniRange = CellSize * 7f,
                OmniAttenuation = 1.3f,
            };
            AddChild(light);
            _lights.Add(light);
        }
    }

    // ---- Coordinates ----------------------------------------------------------------------

    public Vector3 CellCenter(Int3 c) => new Vector3(c.X, c.Y, c.Z) * CellSize;
    public Vector3 CellToGlobal(Int3 c) => GlobalTransform * CellCenter(c);

    /// <summary>The cell holding a point given in the structure's local metres.</summary>
    public Int3 LocalToCell(Vector3 local) => Int3.Round(new System.Numerics.Vector3(local.X, local.Y, local.Z) / CellSize);

    /// <summary>The transform of one cell in the world, for ghosts and effects.</summary>
    public Transform3D CellTransform(Int3 c) => GlobalTransform * new Transform3D(Basis.Identity, CellCenter(c));
}
