using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using PlanetSurface.Poi;
using PoiGenerator;
using PoiGenerator.Gen;
using Layers = GalHack.Physics.Layers;

namespace Slice.Places;

/// <summary>
/// A place near the survivor stood up as a real structure in the surface world: its blocks as
/// meshes and collision (on the world layer, so walking, E, the tool and every ray meet them),
/// its doors as sliding leaves, its lift cars, its keycards lying where they fell, the tells of
/// its hidden rooms, the lamps nearest you, its rooms dressed from the decorations library, and
/// its creatures. The node's local space is the place's grid: one unit a cell, cell (x, y, z)
/// spanning [x, x+1], with the grid's origin at the site's origin in the world (the copy nearest
/// the focus on the torus). Built over several frames by <see cref="PoiPlaces"/>; what it
/// remembers lives in its <see cref="PlaceMemory"/>.
/// </summary>
public partial class Place : Node3D
{
    /// <summary>Small chunks: each is a millisecond or two to put in, and a broken wall remeshes only a little.</summary>
    public const int ChunkSize = 8;

    public PlaceMemory Memory { get; private set; }
    public PoiSite Site => Memory.Site;
    public PoiInstance Inst => Memory.Inst;
    public PoiPlaces Places { get; private set; }

    /// <summary>Everything is in: the planet's far drawing of it is hidden and this one answers.</summary>
    public bool Ready { get; internal set; }

    /// <summary>Beyond the drop radius, kept only because its blocks changed: drawn, nothing more.</summary>
    public bool Dormant { get; private set; }

    public StaticBody3D Blocks { get; private set; }
    readonly Dictionary<Int3, (MeshInstance3D Mesh, CollisionShape3D Shape)> _chunks = new();
    BlockMaterials _materials;
    float _glow;

    public static Place Create(PoiPlaces places, PlaceMemory memory)
    {
        var p = new Place { Name = $"Place {memory.Site.Name}", Memory = memory, Places = places };
        p._glow = memory.Inst.State switch { PoiState.Active => 2.4f, PoiState.Infested => 2.0f, _ => 1.6f };
        p._materials = new BlockMaterials(p._glow);
        p.Blocks = new StaticBody3D { Name = "Blocks", CollisionLayer = Layers.World, CollisionMask = 0 };
        p.AddChild(p.Blocks);
        return p;
    }

    /// <summary>The grid's origin in the world, canonical (x, z on the planet).</summary>
    public Vector3 Origin => new(Site.Origin.X, Site.Origin.Y, Site.Origin.Z);

    public Int3 CellAt(Vector3 world)
    {
        Vector3 p = world - GlobalPosition;
        return new Int3(Mathf.FloorToInt(p.X), Mathf.FloorToInt(p.Y), Mathf.FloorToInt(p.Z));
    }

    /// <summary>In the place's rooms, under a roof.</summary>
    public bool Roofed(Vector3 world)
    {
        Int3 c = CellAt(world) + Int3.Up;
        if (!Inst.Layout.Occupied[c]) return false;
        for (int y = c.Y + 1; y <= Inst.Blocks.Max.Y; y++)
            if (!Inst.Blocks.Get(c with { Y = y }).IsAir) return true;
        return false;
    }

    /// <summary>A cell's middle in the world.</summary>
    public Vector3 WorldOf(Int3 c, float up = 0.5f) => GlobalPosition + new Vector3(c.X + 0.5f, c.Y + up, c.Z + 0.5f);

    public Vector3 ToWorld(Vector3 local) => GlobalPosition + local;
    public Vector3 FromWorld(Vector3 world) => world - GlobalPosition;

    // ---- Blocks ---------------------------------------------------------------------------------

    /// <summary>The mesher's options for a near place: doors are the view's own; buried ground hides faces (an empty plug, as the planet's far drawing does).</summary>
    public static MeshOptions Options(Int3 min) => new()
    {
        ChunkSize = ChunkSize,
        Doors = false,
        Collision = true,
        Detail = true,
        Rock = true,
        RockMin = new Int3(min.X + 1, 0, min.Z + 1),
        RockMax = new Int3(min.X, 0, min.Z),
        RockBottom = min.Y,
    };

    /// <summary>Puts one chunk's mesh and collision in (or replaces them).</summary>
    public void SetChunk(ChunkMesh chunk)
    {
        DropChunk(chunk.Chunk);
        var mesh = new MeshInstance3D { Name = $"Chunk {chunk.Chunk}", Mesh = MeshBuild.ToMesh(chunk, _materials.BySurface) };
        AddChild(mesh);
        CollisionShape3D shape = null;
        if (chunk.Collision.Count > 0)
        {
            shape = new CollisionShape3D { Name = $"Chunk {chunk.Chunk}", Shape = MeshBuild.ToShape(chunk) };
            Blocks.AddChild(shape);
        }
        _chunks[chunk.Chunk] = (mesh, shape);
    }

    void DropChunk(Int3 c)
    {
        if (!_chunks.Remove(c, out var old)) return;
        old.Mesh.QueueFree();
        old.Shape?.QueueFree();
    }

    public int ChunkCount => _chunks.Count;

    static int FloorDiv(int a, int b) => a >= 0 ? a / b : (a - b + 1) / b;
    public static Int3 ChunkOf(Int3 cell) => new(FloorDiv(cell.X, ChunkSize), FloorDiv(cell.Y, ChunkSize), FloorDiv(cell.Z, ChunkSize));

    /// <summary>
    /// Some cells changed: the chunks they touch (and the ones whose faces and shading border them)
    /// are meshed again from the grid, here and now. A chunk is small, so this is a moment's work.
    /// </summary>
    public void Remesh(IEnumerable<Int3> cells)
    {
        var chunks = new HashSet<Int3>();
        foreach (Int3 c in cells)
            for (int dz = -1; dz <= 1; dz++)
            for (int dy = -1; dy <= 1; dy++)
            for (int dx = -1; dx <= 1; dx++)
                chunks.Add(ChunkOf(c + new Int3(dx, dy, dz)));
        foreach (Int3 ch in chunks)
        {
            var window = new ChunkWindow(Inst.Blocks, ch * ChunkSize, ChunkSize);
            List<ChunkMesh> built = Mesher.Build(window, Inst.Layout.Occupied, Options(window.Min));
            ChunkMesh mesh = built.FirstOrDefault(m => m.Chunk == ch);
            if (mesh == null || mesh.Surfaces.All(s => s.Indices.Count == 0) && mesh.Collision.Count == 0) DropChunk(ch);
            else SetChunk(mesh);
        }
        Memory.Modified = true;
    }

    // ---- The rest of the place, once the blocks are in ----------------------------------------

    internal IEnumerable<int> BuildFixtures()
    {
        foreach (int x in BuildDoors()) yield return x;
        BuildLifts();
        yield return 0;
        BuildKeys();
        BuildTells();
        yield return 0;
        BuildLights();
        yield return 0;
    }

    /// <summary>Beyond the drop radius but kept (its blocks changed): drawn only, nothing collides or lights.</summary>
    public void SetDormant(bool dormant)
    {
        if (Dormant == dormant) return;
        Dormant = dormant;
        foreach (Node n in GetChildren())
            if (n is CollisionObject3D body) body.ProcessMode = dormant ? ProcessModeEnum.Disabled : ProcessModeEnum.Inherit;
            else if (n is Light3D || n is CpuParticles3D || n.Name.ToString().StartsWith("Deco") || n.Name == "Keys" || n.Name == "Creatures") ((Node3D)n).Visible = !dormant;
        foreach (Door d in _doors) d.Root.ProcessMode = dormant ? ProcessModeEnum.Disabled : ProcessModeEnum.Inherit;
    }

    public override void _PhysicsProcess(double delta)
    {
        if (!Ready || Dormant) return;
        float dt = (float)delta;
        TickLifts(dt);
        TickDoors(dt);
    }

    public override void _Process(double delta)
    {
        if (!Ready || Dormant) return;
        TickLights((float)delta);
    }
}

public partial class Place
{
    /// <summary>
    /// Walking routes in a place (the generator's own A* over its cells), with the dressing's
    /// solid pieces standing in as obstacles, so a route goes round a desk rather than into it.
    /// </summary>
    public static PoiGenerator.Gen.NavGrid NavOf(PlaceMemory m) => NavOf(m, m.Rooms);

    public static PoiGenerator.Gen.NavGrid NavOf(PlaceMemory m, Dictionary<int, PlaceRoom> rooms) =>
        new(Routed(m, rooms), m.Inst.Layout.Occupied, m.Inst.Extras.Lifts, false);

    /// <summary>The place's blocks as a walking route sees them, the dressing's solid pieces standing in.</summary>
    public static IBlockStructure Routed(PlaceMemory m, Dictionary<int, PlaceRoom> rooms = null) =>
        new Overlay(m.Inst.Blocks, new HashSet<Int3>((rooms ?? m.Rooms)?.Values.SelectMany(r => r.Cells) ?? Enumerable.Empty<Int3>()));

    public PoiGenerator.Gen.NavGrid Nav() => NavOf(Memory);

    /// <summary>The blocks with the dressing's solid pieces standing in.</summary>
    sealed class Overlay : IBlockStructure
    {
        readonly BlockGrid _g;
        readonly HashSet<Int3> _blocked;
        static readonly Block Crate = new(BlockType.Crate, PoiGenerator.Gen.Material.Timber, new Rgb(0, 0, 0), Role.Prop);
        public Overlay(BlockGrid g, HashSet<Int3> blocked) { _g = g; _blocked = blocked; }
        public Int3 Min => _g.Min;
        public Int3 Size => _g.Size;
        public bool InBounds(Int3 p) => _g.InBounds(p);
        public Block Get(Int3 p)
        {
            Block b = _g.Get(p);
            return _blocked.Contains(p) && (b.IsAir || b.Type == BlockType.Growth) ? Crate : b;
        }
        public void Set(Int3 p, Block b) => _g.Set(p, b);
        public int CountBlocks() => _g.CountBlocks();
    }
}

/// <summary>
/// One chunk's box of a place's grid, read through to the whole grid, so the mesher builds that
/// chunk alone with its neighbours' faces and shading seen exactly as in the whole build.
/// </summary>
public sealed class ChunkWindow : IBlockStructure
{
    readonly IBlockStructure _grid;
    public Int3 Min { get; }
    public Int3 Size { get; }

    public ChunkWindow(IBlockStructure grid, Int3 min, int size)
    {
        _grid = grid;
        Min = min;
        Size = new Int3(size, size, size);
    }

    public bool InBounds(Int3 p)
    {
        Int3 l = p - Min;
        return (uint)l.X < (uint)Size.X && (uint)l.Y < (uint)Size.Y && (uint)l.Z < (uint)Size.Z;
    }

    public Block Get(Int3 p) => _grid.Get(p);
    public void Set(Int3 p, Block b) => _grid.Set(p, b);
    public int CountBlocks() => 0;
}
