using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using PlanetSurface.Poi;
using PoiGenerator;
using PoiGenerator.Gen;
using System.Threading.Tasks;
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
    /// <summary>Chunks: each is a millisecond or two to put in, and a broken wall remeshes only one or two.</summary>
    public const int ChunkSize = 16;

    /// <summary>The cubes it stands in: each has its own body, and is drawn near or left to the planet's merged drawing as one.</summary>
    public const int GroupSize = PoiLayer.GroupSize;

    public PlaceMemory Memory { get; private set; }
    public PoiSite Site => Memory.Site;
    public PoiInstance Inst => Memory.Inst;
    public PoiPlaces Places { get; private set; }

    /// <summary>Everything is in: the planet's far drawing of it is hidden and this one answers.</summary>
    public bool Ready { get; internal set; }

    /// <summary>The planet's far drawing of it has given way (it may arrive after a place stood up while loading).</summary>
    internal bool FarHidden;

    /// <summary>Beyond the drop radius, kept only because its blocks changed: drawn, nothing more.</summary>
    public bool Dormant { get; private set; }

    /// <summary>One of the cubes a place stands in.</summary>
    sealed class Group
    {
        public Int3 Key;
        public StaticBody3D Body;
        /// <summary>Its full-detail meshes are in and the planet's merged cube is hidden.</summary>
        public bool Near;
        /// <summary>Its blocks changed: it keeps its own meshes, whatever the distance.</summary>
        public bool Modified;
        public Task<List<ChunkMesh>> Meshing;
        public int Next;
        /// <summary>How far its full detail has dissolved in over the merged cube (0..1), and which way it is going (+1, -1, 0: still).</summary>
        public float Blend;
        public int Way;
        public readonly List<Door> Doors = new();
    }

    readonly Dictionary<Int3, Group> _groups = new();
    readonly Dictionary<Int3, (MeshInstance3D Mesh, CollisionShape3D Shape)> _chunks = new();
    BlockMaterials _materials;
    float _glow;

    public static Place Create(PoiPlaces places, PlaceMemory memory)
    {
        var p = new Place { Name = $"Place {memory.Site.Name}", Memory = memory, Places = places };
        p._glow = memory.Inst.State switch { PoiState.Active => 2.4f, PoiState.Infested => 2.0f, _ => 1.6f };
        p._materials = new BlockMaterials(p._glow);
        p.PhysicsInterpolationMode = PhysicsInterpolationModeEnum.Off; // it stands still; only its doors and lifts move, a tick at a time
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
    public static MeshOptions Options(Int3 min, bool collision = true)
    {
        MeshOptions o = WalkableMesh.On(min); // doors are the view's own: Place.Fixtures' leaves, solid until opened
        o.ChunkSize = ChunkSize;
        o.Collision = collision;
        return o;
    }

    /// <summary>The whole place's collision at stand-up: the same faces, merged, drawn nowhere.</summary>
    public static MeshOptions Solid(Int3 min)
    {
        MeshOptions o = Options(min);
        o.Merged = true;
        o.Drawn = false;
        return o;
    }

    static int FloorDiv(int a, int b) => a >= 0 ? a / b : (a - b + 1) / b;
    public static Int3 ChunkOf(Int3 cell) => new(FloorDiv(cell.X, ChunkSize), FloorDiv(cell.Y, ChunkSize), FloorDiv(cell.Z, ChunkSize));
    static Int3 GroupOf(Int3 chunk) { int k = GroupSize / ChunkSize; return new(FloorDiv(chunk.X, k), FloorDiv(chunk.Y, k), FloorDiv(chunk.Z, k)); }

    Group GroupAt(Int3 key)
    {
        if (_groups.TryGetValue(key, out Group g)) return g;
        g = new Group { Key = key };
        _groups[key] = g;
        return g;
    }

    /// <summary>
    /// Puts one cube's collision in, a chunk a step: the physics builds each chunk's shape (its faces' tree) as it joins
    /// the body, so a dense cube's all at once, its body made whole before it joined the world, was a hitch of tens of ms.
    /// </summary>
    public IEnumerable<int> SetGroupShapes(Int3 key, IEnumerable<ChunkMesh> chunks)
    {
        Group g = GroupAt(key);
        g.Body = new StaticBody3D { Name = $"Blocks {key}", CollisionLayer = Layers.World, CollisionMask = 0 };
        AddChild(g.Body);
        foreach (ChunkMesh c in chunks)
        {
            if (c.Collision.Count == 0) continue;
            var shape = new CollisionShape3D { Name = $"Chunk {c.Chunk}", Shape = MeshBuild.ToShape(c) };
            g.Body.AddChild(shape);
            _chunks[c.Chunk] = (_chunks.GetValueOrDefault(c.Chunk).Mesh, shape);
            yield return 0;
        }
    }

    void SetChunkMesh(ChunkMesh chunk)
    {
        var old = _chunks.GetValueOrDefault(chunk.Chunk);
        old.Mesh?.QueueFree();
        MeshInstance3D mesh = null;
        if (chunk.Surfaces.Any(s => s.Indices.Count > 0))
        {
            mesh = new MeshInstance3D { Name = $"Chunk {chunk.Chunk}", Mesh = MeshBuild.ToMesh(chunk, _materials.BySurface) };
            AddChild(mesh);
            // A cube still coming in shows nothing until all of it is in, and then dissolves in (ShowNear).
            Group g = GroupAt(GroupOf(chunk.Chunk));
            if (!g.Near || g.Way != 0) PlanetSurface.Fx.Fade.Set(mesh, 0f, g.Way != 0 ? g.Blend : 0f);
        }
        _chunks[chunk.Chunk] = (mesh, old.Shape);
    }

    void SetChunkShape(ChunkMesh chunk)
    {
        var old = _chunks.GetValueOrDefault(chunk.Chunk);
        old.Shape?.QueueFree();
        CollisionShape3D shape = null;
        if (chunk.Collision.Count > 0)
        {
            Group g = GroupAt(GroupOf(chunk.Chunk));
            if (g.Body == null)
            {
                g.Body = new StaticBody3D { Name = $"Blocks {g.Key}", CollisionLayer = Layers.World, CollisionMask = 0 };
                AddChild(g.Body);
            }
            shape = new CollisionShape3D { Name = $"Chunk {chunk.Chunk}", Shape = MeshBuild.ToShape(chunk) };
            g.Body.AddChild(shape);
        }
        _chunks[chunk.Chunk] = (old.Mesh, shape);
    }

    public int ChunkCount => _chunks.Count;

    /// <summary>
    /// Some cells changed: the chunks they touch (and the ones whose faces and shading border them)
    /// are meshed again from the grid, here and now. A chunk is small, so this is a moment's work.
    /// </summary>
    public void Remesh(IEnumerable<Int3> cells)
    {
        Memory.Fauna?.Forget(cells);
        foreach (Int3 ch in Mesher.ChunksTouching(cells, ChunkSize))
        {
            Group g = GroupAt(GroupOf(ch));
            g.Modified = true;
            ChunkMesh mesh = Mesher.BuildChunk(Inst.Blocks, Inst.Layout.Occupied, ch, ChunkSize, m => Options(m)) ?? new ChunkMesh { Chunk = ch };
            if (g.Near) SetChunkMesh(mesh);
            SetChunkShape(mesh);
        }
        Memory.Modified = true;
        Places.Zone.Light.Touch();
    }

    // ---- Near and far -----------------------------------------------------------------------------

    int _tierTimer, _meshing;

    /// <summary>How far a point (local) is from a cube.</summary>
    static float Away(Int3 key, Vector3 p)
    {
        float lox = key.X * GroupSize, loy = key.Y * GroupSize, loz = key.Z * GroupSize;
        float dx = MathF.Max(0f, MathF.Max(lox - p.X, p.X - lox - GroupSize));
        float dy = MathF.Max(0f, MathF.Max(loy - p.Y, p.Y - loy - GroupSize));
        float dz = MathF.Max(0f, MathF.Max(loz - p.Z, p.Z - loz - GroupSize));
        return MathF.Sqrt(dx * dx + dy * dy + dz * dz);
    }

    /// <summary>
    /// The cubes round the eye drawn in full, from a worker, a few milliseconds' work a frame; the
    /// planet's merged cube gives way in the frame the last of a cube's meshes is in, and comes back in
    /// the frame they go, so there is never a hole or a double.
    /// </summary>
    void TickTiers()
    {
        var clock = System.Diagnostics.Stopwatch.StartNew();
        foreach (Group g in _groups.Values)
        {
            if (g.Meshing is not { IsCompleted: true }) continue;
            if (g.Meshing.IsFaulted)
            {
                GD.PushError($"could not mesh {Site.Name} at {g.Key}: {g.Meshing.Exception?.GetBaseException()}");
                g.Meshing = null;
                g.Next = 0;
                _meshing--;
                continue;
            }
            List<ChunkMesh> built = g.Meshing.Result;
            while (g.Next < built.Count && clock.Elapsed.TotalMilliseconds < Places.BudgetMs) SetChunkMesh(built[g.Next++]);
            if (g.Next < built.Count) break;
            g.Meshing = null;
            g.Next = 0;
            _meshing--;
            ShowNear(g, true);
        }
        if (--_tierTimer > 0) return;
        _tierTimer = 8;
        Vector3 eye = FromWorld(Places.EyeAt());
        PlaceRules rules = Places.Rules;
        foreach (Group g in _groups.Values)
        {
            if (g.Meshing != null || g.Modified) continue;
            if (Dormant && (g.Near || g.Way != 0)) DropNear(g, instant: true);
            else if (g.Near && g.Way == 0 && Away(g.Key, eye) > rules.NearDrop) DropNear(g);
        }
        if (Dormant) return;
        while (_meshing < 4)
        {
            Group next = null;
            float best = rules.NearRadius;
            foreach (Group g in _groups.Values)
            {
                if (g.Near || g.Meshing != null || g.Way != 0) continue;
                float d = g.Modified ? 0f : Away(g.Key, eye);
                if (d < best) { best = d; next = g; }
            }
            if (next == null) break;
            MeshNear(next);
        }
    }

    void MeshNear(Group g)
    {
        BlockGrid blocks = Inst.Blocks;
        CellMask occupied = Inst.Layout.Occupied;
        Int3 min = g.Key * GroupSize;
        _meshing++;
        g.Meshing = Task.Run(() => Mesher.Build(new ChunkWindow(blocks, min, GroupSize), occupied, Options(min, collision: false)));
    }

    /// <summary>Each cube's meshes, there and then (tests and the warm-up).</summary>
    internal void MeshNearNow(Int3 key)
    {
        Group g = GroupAt(key);
        if (g.Near) return;
        var built = Mesher.Build(new ChunkWindow(Inst.Blocks, key * GroupSize, GroupSize), Inst.Layout.Occupied, Options(key * GroupSize, collision: false));
        foreach (ChunkMesh c in built) SetChunkMesh(c);
        ShowNear(g, true, instant: true);
    }

    /// <summary>How long a cube's full detail and the planet's merged cube take to dissolve one into the other, s.</summary>
    const float NearBlendSeconds = 0.45f;

    readonly List<Group> _fading = new();

    /// <summary>
    /// A cube's full detail comes in over the planet's merged cube, or gives way to it: the two
    /// dissolve one into the other through the fade pattern (PlanetSurface.Fx.Fade), so detail never
    /// swaps in hard; collision is the full detail's throughout. Instant for tests, the warm-up and a dormant place.
    /// </summary>
    void ShowNear(Group g, bool near, bool instant = false)
    {
        if (g.Near != near || g.Way != 0)
            PlanetSurface.Fx.PopLog.Note("detail", near ? "full detail in" : "full detail out", Away(g.Key, FromWorld(Places.EyeAt())), GroupSize, instant ? 1f : 1f / (60f * NearBlendSeconds));
        g.Near = near;
        if (instant)
        {
            g.Blend = near ? 1f : 0f;
            EndBlend(g, near ? 1 : -1);
            return;
        }
        g.Way = near ? 1 : -1;
        Places.Zone.Pois.HideGroup(Site, g.Key, false);
        foreach (Door d in g.Doors) d.Root.Visible = true;
        Bands(g);
        if (!_fading.Contains(g)) _fading.Add(g);
    }

    IEnumerable<MeshInstance3D> MeshesOf(Group g)
    {
        foreach (var (ch, c) in _chunks)
            if (c.Mesh != null && GroupOf(ch) == g.Key) yield return c.Mesh;
    }

    void Bands(Group g)
    {
        foreach (MeshInstance3D m in MeshesOf(g)) PlanetSurface.Fx.Fade.Set(m, 0f, g.Blend);
        foreach (Door d in g.Doors) PlanetSurface.Fx.Fade.Set(d.Root, 0f, g.Blend);
        Places.Zone.Pois.FadeGroup(Site, g.Key, g.Blend);
    }

    void StepFades(float dt)
    {
        if (_fading.Count == 0) return;
        foreach (Group g in _fading)
        {
            g.Blend = Mathf.Clamp(g.Blend + g.Way * dt / NearBlendSeconds, 0f, 1f);
            if (g.Way > 0 ? g.Blend >= 1f : g.Blend <= 0f) EndBlend(g, g.Way);
            else Bands(g);
        }
        _fading.RemoveAll(g => g.Way == 0);
    }

    /// <summary>A cube's blend is over: in (the merged cube hidden) or out (its full-detail meshes gone).</summary>
    void EndBlend(Group g, int way)
    {
        g.Way = 0;
        foreach (MeshInstance3D m in MeshesOf(g)) PlanetSurface.Fx.Fade.Clear(m);
        foreach (Door d in g.Doors)
        {
            PlanetSurface.Fx.Fade.Clear(d.Root);
            d.Root.Visible = way > 0;
        }
        Places.Zone.Pois.FadeGroup(Site, g.Key, 0f, done: true);
        Places.Zone.Pois.HideGroup(Site, g.Key, way > 0);
        if (way > 0) return;
        foreach (Int3 ch in _chunks.Keys.ToList())
        {
            if (GroupOf(ch) != g.Key) continue;
            var c = _chunks[ch];
            c.Mesh?.QueueFree();
            _chunks[ch] = (null, c.Shape);
        }
    }

    void DropNear(Group g, bool instant = false) => ShowNear(g, false, instant);

    /// <summary>The place is going back to the planet's drawing: every cube of it shows there again.</summary>
    internal void GiveBack()
    {
        foreach (Group g in _groups.Values)
        {
            Places.Zone.Pois.FadeGroup(Site, g.Key, 0f, done: true);
            Places.Zone.Pois.HideGroup(Site, g.Key, false);
        }
        PlanetSurface.Fx.Fade.Forget(this);
    }

    /// <summary>Cubes drawn in full now (tests).</summary>
    public int NearGroups => _groups.Values.Count(g => g.Near);
    public int Groups => _groups.Count;

    // ---- The rest of the place, once the blocks are in ----------------------------------------

    internal IEnumerable<int> BuildFixtures()
    {
        // The water it holds by design (a channel, a basin): a surface over the floor, never a block.
        if (PoiGenerator.MeshBuild.Water(Inst) is { } water) AddChild(water);
        foreach (int x in BuildDoors()) yield return x;
        BuildLifts();
        yield return 0;
        BuildKeys();
        BuildTells();
        BuildFittings();
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
        if (!Ready) return;
        TickTiers();
        StepFades((float)delta);
        StepDressFades((float)delta);
        if (Dormant) return;
        TickLights((float)delta);
        TickDress();
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

    /// <summary>
    /// Where the place's people walk: the walking routes with the dressing standing in, but only on
    /// foot: no lifts and no ladders (they ride and climb neither), so a route never takes anyone
    /// straight up or down through a floor, and never over the furniture or a crate.
    /// </summary>
    public static PoiGenerator.Gen.NavGrid FootNavOf(PlaceMemory m)
    {
        var blocked = new HashSet<Int3>(m.Rooms?.Values.SelectMany(r => r.Cells) ?? Enumerable.Empty<Int3>());
        BlockGrid g = m.Inst.Blocks;
        Int3 min = g.Min, size = g.Size;
        for (int y = min.Y; y < min.Y + size.Y; y++)
            for (int z = min.Z; z < min.Z + size.Z; z++)
                for (int x = min.X; x < min.X + size.X; x++)
                {
                    var c = new Int3(x, y, z);
                    if (g.Get(c).Type == BlockType.Ladder) blocked.Add(c);
                }
        return new(new Overlay(g, blocked, foot: true), m.Inst.Layout.Occupied, null, false);
    }

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
        static readonly Block Piece = new(BlockType.Barricade, PoiGenerator.Gen.Material.Timber, new Rgb(0, 0, 0), Role.Prop);
        /// <summary>On foot only: a piece of the dressing, or a crate, is gone round, never stood on.</summary>
        readonly bool _foot;
        public Overlay(BlockGrid g, HashSet<Int3> blocked, bool foot = false) { _g = g; _blocked = blocked; _foot = foot; }
        public Int3 Min => _g.Min;
        public Int3 Size => _g.Size;
        public bool InBounds(Int3 p) => _g.InBounds(p);
        public Block Get(Int3 p)
        {
            Block b = _g.Get(p);
            // A piece stands in its cell over whatever doesn't stop a walker there (air, a floor plate, litter).
            if (_foot) return (_blocked.Contains(p) && !BlockInfo.IsFullCube(b.Type)) || b.Type == BlockType.Crate ? Piece : b;
            return _blocked.Contains(p) && (!BlockInfo.Collides(b.Type) || b.Type == BlockType.Growth) ? Crate : b;
        }
        public void Set(Int3 p, Block b) => _g.Set(p, b);
        public int CountBlocks() => _g.CountBlocks();
    }
}

