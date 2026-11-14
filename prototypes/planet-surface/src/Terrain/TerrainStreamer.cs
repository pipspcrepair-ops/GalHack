using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading.Tasks;
using GalHack.Plants;
using GalHack.Terrain;
using Godot;
using Planets.Gen;
using Planets.Look;
using Planets.Surface;
using PlanetSurface.World;
using TerrainDigging.Terrain;
using Layers = GalHack.Physics.Layers;

namespace PlanetSurface.Terrain;

/// <summary>
/// Streams the planet around a focus point (the player, or the free-fly camera):
/// - <b>Near:</b> diggable 1 m voxel chunks (terrain-digging's Surface Nets) in a square of
///   columns around the focus. Column heights are computed on a worker, then each chunk is
///   meshed on a worker from an immutable snapshot of the store. Collision exists only close to
///   the player, where something can touch it.
/// - <b>Far:</b> heightfield chunks from the same source at 2, 4 and 8 m out to about a kilometre,
///   then a horizon mesh straight from the planet generator's world map.
/// Each level cuts a hole where the finer one is complete, so they never overlap; first it dives
/// under the finer one's edge, so no gap between them can show the sky or what lies past them.
///
/// The main thread only takes snapshots and swaps finished meshes in, under a per-frame time
/// budget. Voxel chunks live in the terrain's frame (see <see cref="PlanetTerrain"/>); when the
/// player crosses a seam the frame shifts by a whole planet and the loaded chunks are re-keyed and
/// moved, never rebuilt. Far chunks are keyed canonically and drawn at their copy nearest the focus.
/// </summary>
public partial class TerrainStreamer : Node3D
{
    const int S = ChunkStore.Size;

    /// <summary>The focus's own speed and way, m/s (the game sets it). What lies ahead goes first, and at speed the far rings run on ahead of it.</summary>
    public Vector3 Heading;

    /// <summary>How long ahead of a moving focus the far rings stand, s, and the most that comes to, m.</summary>
    const float FarLeadSeconds = 2.5f, FarLeadMost = 256f, FarLeadFrom = 15f;

    /// <summary>How many 16 m columns of diggable voxels stream each way round the focus; past them, the far rings. It can change while streaming (a game's view-distance setting): the ring of wanted columns is laid out again.</summary>
    public int VoxelRadius
    {
        get => _voxelRadius;
        set
        {
            if (value == _voxelRadius) return;
            _voxelRadius = value;
            _ring.Clear();
        }
    }
    int _voxelRadius = 5;
    public int CollisionRadius = 2;
    public bool CollisionEnabled = true;
    public double UploadBudgetMs = 3.0;
    /// <summary>The far rings, in 128 m chunks each way, and the last ring of levels 0 and 1: as far as the detail distance (<see cref="DetailReach"/>).</summary>
    public static int FarRadius => DetailReach.FarRadius;
    public static int FarLod0Radius => DetailReach.Lod0Radius;
    public static int FarLod1Radius => DetailReach.Lod1Radius;

    /// <summary>The horizon, m: from here out the far field is solid (<see cref="HorizonReach"/>, less a rebuild's way and the rim's thinning).</summary>
    public const float HorizonDistance = HorizonReach - HorizonRebuild - 600f;
    const float HorizonSpacing = 64f, HorizonRebuild = 1200f, HorizonSink = 1000f;

    /// <summary>
    /// How far the horizon mesh runs from its centre, m. Whatever lies past it is the far field a game
    /// draws behind the land (the globe's sheet): from 600 m inside this edge land and sea thin away
    /// into it (past_rim in noise.gdshaderinc). The mesh is at most <see cref="HorizonRebuild"/> off
    /// the eye, so the far field has to be solid from 5,800 m out.
    /// </summary>
    public const float HorizonReach = 7600f;

    /// <summary>
    /// Where the world starts to be round, m from the eye: past it the far land, the sea, the far
    /// field and the clouds all sink over the horizon together on the far land's curve
    /// (<see cref="RoundRadius"/>; round_sink in noise.gdshaderinc, the sky's clouds, the sheet). It is
    /// past everything drawn in full or that collides (the far rings, places' near blocks, roads'
    /// furniture: under 1,400 m), so nothing that stands on the ground moves but the far meshes of
    /// places, which sink with it (PoiLayer.SinkFar). Any further out and the horizon would stand
    /// further off, deeper in the haze.
    /// </summary>
    public const float RoundFrom = 2000f, RoundRadius = 300000f;

    /// <summary>How far ground this far from the eye has sunk over the round world's horizon, m.</summary>
    public static float RoundSink(float distance)
    {
        float d = MathF.Max(distance - RoundFrom, 0f);
        return d * d / (2f * RoundRadius);
    }

    /// <summary>How far inside the voxel square the far rings still draw: a grid square of the coarsest level, the last that can be there.</summary>
    const float FarHoleCut = 8f;

    public PlanetWorld World { get; private set; }
    public Vector3 Focus { get; private set; }

    public ShaderMaterial VoxelMaterial { get; private set; }
    public ShaderMaterial FarMaterial { get; private set; }
    ShaderMaterial _horizonMaterial, _floraMaterial;
    Material _waterMaterial;
    Dictionary<Flora, Mesh> _floraMeshes;

    /// <summary>The world's species as meshes, by herbarium index and variant.</summary>
    Mesh[][] _plantMeshes = System.Array.Empty<Mesh[]>();

    // ---- Voxel state ----
    sealed class Column
    {
        public int Cx, Cz;
        public bool HeightsRequested, HeightsReady, Planned;
        public readonly List<VChunk> Chunks = new();

        /// <summary>Every chunk planned for the column has a mesh (an empty one counts).</summary>
        public bool Built
        {
            get
            {
                if (!Planned) return false;
                foreach (VChunk c in Chunks) if (c.BuiltVersion < 0) return false;
                return true;
            }
        }
    }

    sealed class VChunk
    {
        public Int3 Key, BuiltKey;
        public int Version, BuiltVersion = -1;
        public bool InFlight, Queued, Urgent;
        public MeshInstance3D Mesh;
        public StaticBody3D Body;
        public CollisionShape3D Shape;
        public Vector3[] Faces;   // collision triangles: built only for chunks near the player
        public bool HasTriangles;
        public ulong Hash, ShapeHash;
        public int Triangles;
    }

    readonly Dictionary<(int, int), Column> _columns = new();

    /// <summary>
    /// Mesh nodes of unloaded chunks, kept for the next chunk: flying fast loads and unloads
    /// dozens a second, and freeing and creating nodes cost more main-thread time than meshing.
    /// </summary>
    readonly Stack<MeshInstance3D> _spareMeshes = new();
    readonly Dictionary<Int3, VChunk> _chunks = new();
    readonly ConcurrentQueue<(int, int)> _heightsDone = new();
    readonly ConcurrentQueue<(VoxelJob job, int shiftX, int shiftZ)> _voxelDone = new();
    readonly PriorityQueue<Int3, int> _queue = new();
    readonly List<(int dx, int dz)> _ring = new();
    int _heightsInFlight, _voxelInFlight, _shiftX, _shiftZ;
    readonly int _maxInFlight = Math.Clamp(System.Environment.ProcessorCount / 2 - 1, 2, 6);
    int _completeRadius = -1;

    // ---- Far state ----
    sealed class FarChunk
    {
        public int Fx, Fz, Lod = -1, WantedLod;
        public bool InFlight;
        public int Version;
        public Node3D Node;
        public List<FloraBatch> Flora;

        /// <summary>Its ground mesh as drawn (chunk-local), and its triangles by 16 m cell, made the first time something asks (<see cref="DrawnGround"/>).</summary>
        public Vector3[] Vertices;
        public int[] Indices;
        public List<int>[] Cells;

        /// <summary>Level 0: its solid flora (trunks, boulders), in planet coordinates. Null otherwise.</summary>
        public List<FloraSolid> Solids;
    }

    readonly Dictionary<(int, int), FarChunk> _far = new();
    readonly ConcurrentQueue<FarJob> _farDone = new();
    readonly List<(int fx, int fz, int lod, int d, float rank)> _farWanted = new();
    readonly HashSet<(int, int)> _farKeep = new();
    int _farInFlight, _farWaited;
    /// <summary>The most frames a finished far chunk waits for an unhurried one.</summary>
    const int FarPatience = 6;
    MeshInstance3D _horizon;
    Task<(Godot.Collections.Array arrays, double x, double z)> _horizonJob;
    double _horizonX = double.MaxValue, _horizonZ = double.MaxValue;
    readonly HashSet<long> _dug = new();
    HashSet<long> _dugSnapshot = new();
    readonly HashSet<(int, int)> _floraDirty = new();
    int _evictTimer;

    // ---- Stats ----
    // Streaming's own main-thread time. A garbage collection that happens to pause the process
    // while streaming runs is left out: it would pause whatever was running, and it is measured
    // and reported on its own (LastGcPauseMs, and the tests' GC summary).
    public double LastFrameMs { get; private set; }
    public double WorstFrameMs { get; private set; }
    public double LastGcPauseMs { get; private set; }

    /// <summary>Where the worst frame's time went, section by section.</summary>
    public string WorstBreakdown { get; private set; } = "";
    readonly double[] _sections = new double[5];
    long _frameStart;
    static readonly string[] SectionNames = { "voxels", "far", "horizon", "evict", "collision" };
    public int MeshedChunks => _chunks.Count;
    public int Triangles { get; private set; }
    public int CollisionBodies { get; private set; }
    public int FarChunks => _far.Count;
    public int CompleteRadius => _completeRadius;

    /// <summary>How many eviction passes (unloading far-off generated chunks) have run: tests wait on it.</summary>
    public int EvictPasses { get; private set; }
    public int Pending => _queue.Count + _voxelInFlight + _heightsInFlight + _farInFlight;

    /// <summary>A voxel chunk's new mesh was swapped in (frame key).</summary>
    public event Action<Int3> ChunkApplied;

    /// <summary>The longest GC pause that landed inside streaming's own time since the last reset.</summary>
    public double WorstGcPauseMs { get; private set; }

    public void ResetStats() => WorstFrameMs = WorstGcPauseMs = 0;

    public override void _Ready()
    {
        _floraMeshes = FloraMeshes.BuildAll();
        var sizes = new Dictionary<Flora, (float, float)>();
        foreach (var (kind, mesh) in _floraMeshes)
        {
            Aabb box = mesh.GetAabb();
            sizes[kind] = (box.End.Y, MathF.Max(MathF.Max(-box.Position.X, box.End.X), MathF.Max(-box.Position.Z, box.End.Z)));
        }
        FarBuilder.KindSize = sizes;
        _horizon = new MeshInstance3D { Name = "Horizon", CastShadow = GeometryInstance3D.ShadowCastingSetting.Off };
        AddChild(_horizon);
    }

    /// <summary>Starts streaming a planet (dropping everything of the previous one).</summary>
    public void SetWorld(PlanetWorld world, Vector3 focus, Material water)
    {
        Clear();
        World = world;
        Focus = focus;
        _felledSnapshot = new HashSet<long>(world.Felled); // trees felled here before stay felled
        _waterMaterial = water;
        _roads = RoadField.Build(world.Source, world.Width, world.Height);
        _grounds = GroundField.Build(world.Source.Grounds, world.Width, world.Height);
        if (_grounds.Crowded > 0) GD.Print($"made ground: {_grounds.PatchCount} patches, {_grounds.Crowded} cells over {GroundField.MostPerCell}");
        VoxelMaterial = TerrainMaterial(far: false);
        FarMaterial = TerrainMaterial(far: true);
        _horizonMaterial = TerrainMaterial(far: true);
        // Inside the square the far rings cover, the horizon's vertices a whole grid step or more in
        // drop far below any ground, so its triangles across the square's edge become walls diving
        // under the far rings: where the horizon is higher than the rings' edge, the wall fills the
        // step (no sky through it); where it is lower, the wall is hidden under them. It is cut only
        // where every triangle is sunk. Its triangles never reach outside the square, so it is
        // untouched there.
        _horizonMaterial.SetShaderParameter("hole_sink", HorizonSink);
        _horizonMaterial.SetShaderParameter("hole_sink_from", HorizonSpacing - 0.5f);
        _horizonMaterial.SetShaderParameter("hole_cut", 2f * HorizonSpacing);
        _horizonMaterial.SetShaderParameter("road_tolerance", 30f);
        _horizonMaterial.SetShaderParameter("rock_uv2", 0f); // its UV2 is its own smoothed ground, not a cliff's reach
        _horizon.MaterialOverride = _horizonMaterial;
        // The far rings meet the voxels the same way. The square moves 16 m at a time, so the meshes
        // can't be stitched, and on a cliff the two can differ by metres along it. Far grid lines fall on
        // the square's edge, and the next vertices in go under, so the last grid square is a wall
        // down from that edge: it fills any step up from the voxels, and is hidden under them otherwise.
        FarMaterial.SetShaderParameter("hole_sink", HorizonSink);
        FarMaterial.SetShaderParameter("hole_sink_from", 0.25f);
        FarMaterial.SetShaderParameter("hole_cut", FarHoleCut);
        _floraMaterial = new ShaderMaterial { Shader = GD.Load<Shader>("res://shaders/flora.gdshader") };
        BuildPlantMeshes();
    }

    /// <summary>The roads the terrain shader paints, for the planet being streamed.</summary>
    RoadField _roads;

    /// <summary>The made ground round the places the terrain shader paints (yards, aprons, car parks, paths).</summary>
    GroundField _grounds;

    /// <summary>The world's plants, once, as meshes: a bounded set every chunk then instances, so nothing is built at chunk load.</summary>
    void BuildPlantMeshes()
    {
        IReadOnlyList<Plant> plants = World.Flora.Book.Plants;
        _plantMeshes = new Mesh[plants.Count][];
        // A plant is drawn with its own mesh at every distance (the herbarium's far meshes were other, simpler shapes: a swap showed).
        foreach (Plant p in plants) _plantMeshes[p.Index] = Array.ConvertAll(p.Near, m => WithLods(PlantMeshes.ToGodot(m)));
        StartCover();
    }

    /// <summary>
    /// A mesh with coarser versions of itself, which the renderer draws instead once their difference from the whole is
    /// under a pixel on screen (its own choice, by the batch's distance): a far tree is the same tree, in fewer triangles.
    /// </summary>
    static ArrayMesh WithLods(ArrayMesh mesh)
    {
        var tool = new SurfaceTool();
        tool.CreateFrom(mesh, 0);
        tool.Index();
        var lods = new ImporterMesh();
        lods.AddSurface(Mesh.PrimitiveType.Triangles, tool.CommitToArrays());
        // Faces of a plant are flat-shaded: every corner is merged with those at its place, whatever their normals, so the coarser versions can cross them.
        lods.GenerateLods(180f, 180f, new Godot.Collections.Array());
        return lods.GetMesh();
    }

    /// <summary>Flora's material (tests draw plants with it), and a kind's own mesh (rocks and what the herbarium has no species for).</summary>
    public ShaderMaterial FloraMaterial => _floraMaterial;
    public Mesh KindMesh(Flora kind) => _floraMeshes != null && _floraMeshes.TryGetValue(kind, out Mesh m) ? m : null;

    /// <summary>The mesh of one species' variant, or null for what the world's herbarium doesn't hold.</summary>
    public Mesh PlantMesh(int plant, int variant) => plant < 0 || plant >= _plantMeshes.Length ? null : _plantMeshes[plant][variant];

    ShaderMaterial TerrainMaterial(bool far)
    {
        var m = new ShaderMaterial { Shader = GD.Load<Shader>("res://shaders/terrain.gdshader") };
        m.SetShaderParameter("far_ring", far);
        m.SetShaderParameter("strata", World.Palette.Strata);
        WorldLook look = World.Look;
        m.SetShaderParameter("snow_altitude", look.SnowAltitude);
        m.SetShaderParameter("base_snow", look.BaseSnow);
        m.SetShaderParameter("dust_color", look.Dust);
        m.SetShaderParameter("glow_color", look.Glow);
        m.SetShaderParameter("road_segments", _roads.Segments);
        m.SetShaderParameter("road_cells", _roads.Cells);
        m.SetShaderParameter("road_index", _roads.Index);
        m.SetShaderParameter("road_grid", _roads.Grid);
        m.SetShaderParameter("road_tolerance", far ? 12f : 1.2f);
        // The world's own ground bared, for its tracks and dirt roads: its rock weathered into clay, grit or sand.
        Planets.Look.Rgb bare = World.Palette.BareSoil;
        m.SetShaderParameter("bare_soil", new Color(bare.R, bare.G, bare.B));
        m.SetShaderParameter("ground_patches", _grounds.Patches);
        m.SetShaderParameter("ground_cells", _grounds.Cells);
        m.SetShaderParameter("ground_index", _grounds.Index);
        m.SetShaderParameter("ground_grid", _grounds.Grid);
        // The prevailing wind that drifts sand over the roads: one of the map's four ways, by the world.
        int wind = (int)(World.Spec.Seed % 4);
        // The world's road paint: yellow or white centre lines, dashed or solid, by its culture; none where
        // the old world never painted its roads.
        ulong paint = World.Spec.Seed / 4;
        bool painted = World.Profile?.Before is not PoiGenerator.Gen.TechLevel.Low;
        m.SetShaderParameter("road_paint", new Vector4(paint % 2, (paint / 2) % 3 == 0 ? 0 : 1, painted ? 1 : 0, 0));
        m.SetShaderParameter("road_wind", wind switch { 0 => new Vector2(1, 0), 1 => new Vector2(-1, 0), 2 => new Vector2(0, 1), _ => new Vector2(0, -1) });
        RockLook.Apply(m, World, cliffInfo: true);
        return m;
    }

    public void SetFocus(Vector3 focus) => Focus = focus;

    /// <summary>
    /// The frame moved by whole planets (the player crossed a seam): re-key every loaded chunk
    /// and move it, so nothing is rebuilt. dx and dz are in chunks (multiples of the planet's).
    /// </summary>
    public void ShiftFrame(int dcx, int dcz)
    {
        _shiftX += dcx;
        _shiftZ += dcz;
        var columns = new List<Column>(_columns.Values);
        _columns.Clear();
        foreach (Column c in columns)
        {
            c.Cx += dcx;
            c.Cz += dcz;
            _columns[(c.Cx, c.Cz)] = c;
        }
        var chunks = new List<VChunk>(_chunks.Values);
        _chunks.Clear();
        foreach (VChunk c in chunks)
        {
            c.Key = new Int3(c.Key.X + dcx, c.Key.Y, c.Key.Z + dcz);
            _chunks[c.Key] = c;
            PlaceChunkNodes(c);
        }
        var queued = new List<(Int3, int)>();
        while (_queue.TryDequeue(out Int3 key, out int priority)) queued.Add((new Int3(key.X + dcx, key.Y, key.Z + dcz), priority));
        foreach (var (key, priority) in queued) _queue.Enqueue(key, priority);
        ShiftSolids(dcx, dcz);
    }

    void PlaceChunkNodes(VChunk c)
    {
        var offset = new Vector3((c.Key.X - c.BuiltKey.X) * S, 0, (c.Key.Z - c.BuiltKey.Z) * S);
        if (c.Mesh != null) c.Mesh.Position = offset;
        if (c.Body != null) c.Body.Position = offset;
    }

    /// <summary>Edits changed these chunks (frame keys, copies included): rebuild the loaded ones first.</summary>
    public void MarkDirty(IEnumerable<Int3> chunks)
    {
        foreach (Int3 key in chunks)
        {
            if (!_chunks.TryGetValue(key, out VChunk c))
            {
                // An edit below the meshed range of a loaded column (a deep shaft): mesh it now.
                if (!_columns.TryGetValue((key.X, key.Z), out Column column) || !column.Planned) continue;
                c = new VChunk { Key = key, BuiltKey = key };
                _chunks[key] = c;
                column.Chunks.Add(c);
            }
            c.Version++;
            Enqueue(c, urgent: true);
        }
    }

    /// <summary>The surface was dug in these cells: flora standing there must go.</summary>
    public void MarkDug(IEnumerable<RemovedCell> cells)
    {
        foreach (RemovedCell r in cells)
        {
            int x = Mod(r.Cell.X, World.Terrain.Width), z = Mod(r.Cell.Z, World.Terrain.Height);
            float surface = World.Source.SurfaceHeight(x + 0.5, z + 0.5);
            if (r.Cell.Y < surface - 2.5f) continue; // only the topsoil holds roots
            if (_dug.Add(FarBuilder.Key(x, z))) _floraDirty.Add((x / FarBuilder.Size, z / FarBuilder.Size));
        }
    }

    void Enqueue(VChunk c, bool urgent)
    {
        if (c.Queued) return;
        c.Queued = true;
        c.Urgent = urgent;
        _queue.Enqueue(c.Key, Rank(c));
    }

    /// <summary>
    /// A chunk's place in the queue: nearest first, the layers round the focus before those over and
    /// under them, what lies ahead of a moving focus before what it is leaving. Edits go before all of it.
    /// </summary>
    int Rank(VChunk c)
    {
        if (c.Urgent) return -1;
        var (fx, fz) = FocusColumn;
        int dx = c.Key.X - fx, dz = c.Key.Z - fz;
        int rank = Math.Max(Math.Abs(dx), Math.Abs(dz)) * 4 + Math.Abs(c.Key.Y - (int)MathF.Floor(Focus.Y / S));
        return Math.Max(0, rank - (int)(Ahead(dx, dz) * 3f));
    }

    /// <summary>How far a point lies ahead of the focus, in the units given (behind counts for nothing); a standing focus has no ahead.</summary>
    float Ahead(float dx, float dz)
    {
        float speed = MathF.Sqrt(Heading.X * Heading.X + Heading.Z * Heading.Z);
        if (speed < 1f) return 0f;
        return MathF.Max(0f, (dx * Heading.X + dz * Heading.Z) / speed) * MathF.Min(speed / 12f, 1f);
    }

    /// <summary>The focus moved to another column: a queue ranked from where it was is stale, and at speed the chunks it just left would go first.</summary>
    void Rerank()
    {
        _reranking.Clear();
        while (_queue.TryDequeue(out Int3 key, out _))
            if (_chunks.TryGetValue(key, out VChunk c)) _reranking.Add(c);
        foreach (VChunk c in _reranking) _queue.Enqueue(c.Key, Rank(c));
    }

    readonly List<VChunk> _reranking = new();
    (int x, int z) _rankedAt;

    (int x, int z) FocusColumn => ((int)MathF.Floor(Focus.X / S), (int)MathF.Floor(Focus.Z / S));

    // ---- Every frame ----------------------------------------------------------------------

    public override void _Process(double delta)
    {
        if (World == null) return;
        long start = Stopwatch.GetTimestamp();
        TimeSpan pausedBefore = GC.GetTotalPauseDuration();
        _frameStart = start;
        Array.Clear(_sections);
        PopMs = 0;

        StreamVoxels();
        _sections[0] = Stopwatch.GetElapsedTime(start).TotalMilliseconds - _sections[4];
        long t = Stopwatch.GetTimestamp();
        StreamFar();
        StepSwaps((float)Math.Min(delta, 0.1));
        StreamCover();
        _sections[1] = Stopwatch.GetElapsedTime(t).TotalMilliseconds - _sections[2] - PopMs;
        t = Stopwatch.GetTimestamp();
        if (++_evictTimer >= 30)
        {
            _evictTimer = 0;
            Evict();
            EvictPasses++;
        }
        _sections[3] = Stopwatch.GetElapsedTime(t).TotalMilliseconds;

        LastGcPauseMs = (GC.GetTotalPauseDuration() - pausedBefore).TotalMilliseconds;
        LastFrameMs = Stopwatch.GetElapsedTime(start).TotalMilliseconds - LastGcPauseMs - PopMs;
        if (LastFrameMs > WorstFrameMs)
        {
            WorstFrameMs = LastFrameMs;
            var parts = new System.Text.StringBuilder();
            for (int i = 0; i < _sections.Length; i++) parts.Append($"{SectionNames[i]} {_sections[i]:F1} ");
            if (LastGcPauseMs > 0) parts.Append($"(less a {LastGcPauseMs:F1} ms GC pause inside them)");
            WorstBreakdown = parts.ToString().TrimEnd();
        }
        if (LastGcPauseMs > WorstGcPauseMs) WorstGcPauseMs = LastGcPauseMs;
    }

    /// <summary>A column's heights worked out on a worker. (Its own method: a lambda in the ring's loop made a closure for every column every frame.)</summary>
    void RequestHeights(PlanetTerrainSource source, int cx, int cz) => Task.Run(() =>
    {
        try { source.ColumnAt(cx, cz); }
        catch (Exception e) { GD.PushError($"column {cx},{cz} failed: {e}"); }
        _heightsDone.Enqueue((cx, cz));
    });

    void StreamVoxels()
    {
        var (fcx, fcz) = FocusColumn;
        PlanetTerrainSource source = World.Source;
        if (_rankedAt != (fcx, fcz))
        {
            _rankedAt = (fcx, fcz);
            Rerank();
        }

        // Columns wanted, nearest first; ask workers for their heights.
        if (_ring.Count == 0) BuildRing();
        foreach (var (dx, dz) in _ring)
        {
            var key = (fcx + dx, fcz + dz);
            if (!_columns.TryGetValue(key, out Column column))
            {
                column = new Column { Cx = key.Item1, Cz = key.Item2 };
                _columns[key] = column;
            }
            if (column.HeightsRequested || _heightsInFlight >= _maxInFlight) continue;
            column.HeightsRequested = true;
            _heightsInFlight++;
            RequestHeights(source, column.Cx, column.Cz);
        }
        int sx = _shiftX, sz = _shiftZ;
        while (_heightsDone.TryDequeue(out var done))
        {
            _heightsInFlight--;
            // A shift while it was computing moved the column; the cache is keyed canonically.
            foreach (Column c in _columns.Values)
                if (Mod(c.Cx, World.Terrain.ChunksX) == Mod(done.Item1, World.Terrain.ChunksX) && Mod(c.Cz, World.Terrain.ChunksZ) == Mod(done.Item2, World.Terrain.ChunksZ))
                    c.HeightsReady = true;
        }

        // Plan columns whose heights are known: which chunk ys to mesh.
        foreach (Column c in _columns.Values)
        {
            if (!c.HeightsReady || c.Planned) continue;
            c.Planned = true;
            var (lo, hi) = source.MeshRange(c.Cx, c.Cz);
            SortedSet<int> touched = World.Terrain.TouchedYs(c.Cx, c.Cz);
            if (touched != null)
                foreach (int y in touched)
                {
                    lo = Math.Min(lo, y);
                    hi = Math.Max(hi, y);
                }
            for (int y = lo; y <= hi; y++)
            {
                var key = new Int3(c.Cx, y, c.Cz);
                if (_chunks.ContainsKey(key)) continue;
                var chunk = new VChunk { Key = key, BuiltKey = key };
                _chunks[key] = chunk;
                c.Chunks.Add(chunk);
                Enqueue(chunk, urgent: false);
            }
        }

        // Start mesh jobs from snapshots.
        while (_voxelInFlight < _maxInFlight && _queue.TryDequeue(out Int3 next, out _))
        {
            if (!_chunks.TryGetValue(next, out VChunk c)) continue;
            c.Queued = c.Urgent = false;
            if (c.InFlight) continue; // picked up again when the running job lands
            c.InFlight = true;
            _voxelInFlight++;
            bool near = CollisionEnabled && Math.Max(Math.Abs(next.X - fcx), Math.Abs(next.Z - fcz)) <= CollisionRadius + 2;
            var job = new VoxelJob { Key = next, Version = c.Version, Hood = World.Terrain.Store.CaptureNeighborhood(next), NeedFaces = near };
            GroundPalette palette = World.Palette;
            double w = World.Terrain.Width, h = World.Terrain.Height;
            Task.Run(() =>
            {
                VoxelMeshing.Build(job, source, palette, w, h);
                _voxelDone.Enqueue((job, sx, sz));
            });
        }

        // Swap finished meshes in, within the frame budget (always at least one, so it drains).
        long start = Stopwatch.GetTimestamp();
        int applied = 0;
        while (_voxelDone.TryPeek(out _))
        {
            if (applied > 0 && Stopwatch.GetElapsedTime(start).TotalMilliseconds > UploadBudgetMs) break;
            _voxelDone.TryDequeue(out var done);
            _voxelInFlight--;
            applied++;
            Int3 key = new(done.job.Key.X + (_shiftX - done.shiftX), done.job.Key.Y, done.job.Key.Z + (_shiftZ - done.shiftZ));
            if (!_chunks.TryGetValue(key, out VChunk c)) continue; // unloaded while building
            c.InFlight = false;
            if (done.job.Error != null)
            {
                GD.PushError($"meshing chunk {key} failed: {done.job.Error}");
                continue;
            }
            Apply(c, done.job);
            if (c.Version != done.job.Version) Enqueue(c, urgent: true); // edited again meanwhile
        }

        long collisionStart = Stopwatch.GetTimestamp();
        UpdateCollision(fcx, fcz, start);
        long solidsStart = Stopwatch.GetTimestamp();
        UpdateSolids(fcx, fcz, start);
        SolidsTotalMs += Stopwatch.GetElapsedTime(solidsStart).TotalMilliseconds;
        _sections[4] = Stopwatch.GetElapsedTime(collisionStart).TotalMilliseconds;
        Unload(fcx, fcz);
        UpdateVisibility(fcx, fcz);
    }

    void BuildRing()
    {
        for (int dz = -VoxelRadius; dz <= VoxelRadius; dz++)
        for (int dx = -VoxelRadius; dx <= VoxelRadius; dx++)
            _ring.Add((dx, dz));
        _ring.Sort((a, b) => (a.dx * a.dx + a.dz * a.dz).CompareTo(b.dx * b.dx + b.dz * b.dz));
    }

    void Apply(VChunk c, VoxelJob job)
    {
        c.BuiltVersion = job.Version;
        c.BuiltKey = job.Key;
        c.Hash = job.Hash;
        Triangles -= c.Triangles;
        c.Triangles = job.IsEmpty ? 0 : job.Indices.Length / 3;
        Triangles += c.Triangles;
        c.Faces = job.Faces;
        c.HasTriangles = !job.IsEmpty;
        if (job.IsEmpty)
        {
            if (c.Mesh != null) c.Mesh.Mesh = null;
        }
        else
        {
            var arrays = new Godot.Collections.Array();
            arrays.Resize((int)Mesh.ArrayType.Max);
            arrays[(int)Mesh.ArrayType.Vertex] = job.Positions;
            arrays[(int)Mesh.ArrayType.Normal] = job.Normals;
            arrays[(int)Mesh.ArrayType.Color] = job.Colors;
            arrays[(int)Mesh.ArrayType.TexUV] = job.Flags;
            arrays[(int)Mesh.ArrayType.TexUV2] = job.Relief;
            arrays[(int)Mesh.ArrayType.Index] = job.Indices;
            var mesh = new ArrayMesh();
            mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays);
            if (c.Mesh == null)
            {
                if (_spareMeshes.TryPop(out MeshInstance3D spare)) c.Mesh = spare;
                else
                {
                    c.Mesh = new MeshInstance3D { PhysicsInterpolationMode = PhysicsInterpolationModeEnum.Off };
                    AddChild(c.Mesh);
                }
                c.Mesh.MaterialOverride = VoxelMaterial;
            }
            c.Mesh.Mesh = mesh;
        }
        // An existing body gets its new shape right away: the ground the player stands on changed.
        // (Without triangles yet, collision picks the chunk up again below and builds them.)
        if (c.Body != null)
        {
            if (c.Faces != null) SetShape(c);
            else
            {
                c.ShapeHash = 0;
                // Nothing left in the chunk (its last ground dug away): its old shape must go with the mesh, or it stands there unseen.
                if (job.IsEmpty) c.Shape.Disabled = true;
            }
        }
        PlaceChunkNodes(c);
        ChunkApplied?.Invoke(c.Key);
    }

    void Recycle(VChunk c)
    {
        if (c.Mesh == null) return;
        c.Mesh.Mesh = null;
        c.Mesh.Visible = false;
        _spareMeshes.Push(c.Mesh);
        c.Mesh = null;
    }

    void UpdateCollision(int fcx, int fcz, long frameStart)
    {
        int bodies = 0;
        foreach (VChunk c in _chunks.Values)
        {
            int d = Math.Max(Math.Abs(c.Key.X - fcx), Math.Abs(c.Key.Z - fcz));
            bool want = CollisionEnabled && d <= CollisionRadius && c.BuiltVersion >= 0;
            if (want && c.Faces == null && c.HasTriangles && !c.InFlight)
            {
                // Meshed while it was too far away to need collision: mesh it again, with triangles.
                // (Keeping every chunk's arrays just in case made the garbage collector stall frames.)
                Enqueue(c, urgent: true);
            }
            if (want && c.Body == null && c.Faces != null)
            {
                if (Stopwatch.GetElapsedTime(frameStart).TotalMilliseconds > UploadBudgetMs * 1.5) { bodies++; continue; }
                c.Shape = new CollisionShape3D { Shape = new ConcavePolygonShape3D() };
                c.Body = new StaticBody3D { Name = $"Ground {c.Key.X} {c.Key.Y} {c.Key.Z}", CollisionLayer = Layers.World, CollisionMask = 0 };
                c.Body.AddChild(c.Shape);
                AddChild(c.Body);
                SetShape(c);
                PlaceChunkNodes(c);
            }
            else if (c.Body != null && (!CollisionEnabled || d > CollisionRadius + 1))
            {
                c.Body.QueueFree();
                c.Body = null;
                c.Shape = null;
                c.ShapeHash = 0;
            }
            if (c.Body != null) bodies++;
        }
        CollisionBodies = bodies;
    }

    void SetShape(VChunk c)
    {
        if (c.ShapeHash == c.Hash && c.ShapeHash != 0) return;
        c.ShapeHash = c.Hash;
        ((ConcavePolygonShape3D)c.Shape.Shape).Data = c.Faces ?? Array.Empty<Vector3>();
        c.Shape.Disabled = c.Faces == null || c.Faces.Length == 0;
    }

    void Unload(int fcx, int fcz)
    {
        List<(int, int)> gone = null;
        foreach (var (key, column) in _columns)
        {
            int d = Math.Max(Math.Abs(column.Cx - fcx), Math.Abs(column.Cz - fcz));
            if (d <= VoxelRadius + 1) continue; // a ring of slack, so hovering on a border never thrashes
            (gone ??= new List<(int, int)>()).Add(key);
        }
        if (gone == null) return;
        foreach (var key in gone)
        {
            foreach (VChunk c in _columns[key].Chunks)
            {
                Recycle(c);
                c.Body?.QueueFree();
                Triangles -= c.Triangles;
                _chunks.Remove(c.Key);
            }
            _columns.Remove(key);
        }
    }

    /// <summary>
    /// Voxel chunks show only inside the largest square of complete columns around the focus,
    /// and the far rings cut exactly that square out: the two never overlap or leave a gap.
    /// </summary>
    void UpdateVisibility(int fcx, int fcz)
    {
        int full = -1;
        for (int r = 0; r <= VoxelRadius; r++)
        {
            bool complete = true;
            for (int dz = -r; dz <= r && complete; dz++)
            for (int dx = -r; dx <= r && complete; dx++)
            {
                if (Math.Max(Math.Abs(dx), Math.Abs(dz)) != r) continue;
                complete = _columns.TryGetValue((fcx + dx, fcz + dz), out Column c) && c.Built;
            }
            if (!complete) break;
            full = r;
        }
        _completeRadius = full;
        foreach (VChunk c in _chunks.Values)
        {
            if (c.Mesh == null) continue;
            int d = Math.Max(Math.Abs(c.Key.X - fcx), Math.Abs(c.Key.Z - fcz));
            c.Mesh.Visible = d <= full;
        }
        Vector4 hole = Vector4.Zero;
        if (full >= 0)
        {
            float x0 = (fcx - full) * S, z0 = (fcz - full) * S, span = (2 * full + 1) * S;
            hole = new Vector4(x0 + 0.5f, z0 + 0.5f, x0 + span - 0.5f, z0 + span - 0.5f);
        }
        FarMaterial.SetShaderParameter("hole", hole);
    }

    // ---- Far rings ------------------------------------------------------------------------

    /// <summary>A far chunk built on a worker. (Its own method: a lambda in the wanted list's loop made a closure for every chunk every frame.)</summary>
    void BuildFar(FarJob job)
    {
        PlanetTerrainSource source = World.Source;
        GroundPalette palette = World.Palette;
        HashSet<long> dug = _dugSnapshot, felled = _felledSnapshot;
        Task.Run(() =>
        {
            FarBuilder.Build(job, source, palette, dug, felled);
            _farDone.Enqueue(job);
        });
    }

    void StreamFar()
    {
        double w = World.Terrain.Width, h = World.Terrain.Height;
        int nx = FarBuilder.Count(World.Terrain.Width), nz = FarBuilder.Count(World.Terrain.Height);
        double cx = Mod(Focus.X, w), cz = Mod(Focus.Z, h);
        // A moving focus: the rings stand a little ahead of it, so what it is coming to is sharp when it gets there.
        // (Not on foot: rings that shift with every step back and forth over a border rebuild for nothing.)
        Vector3 lead = new Vector3(Heading.X, 0f, Heading.Z) * FarLeadSeconds;
        lead = lead.Length() < FarLeadFrom * FarLeadSeconds ? Vector3.Zero : lead.LimitLength(FarLeadMost);
        double leadX = Mod(cx + lead.X, w), leadZ = Mod(cz + lead.Z, h);
        int ffx = (int)Math.Floor(leadX / FarBuilder.Size), ffz = (int)Math.Floor(leadZ / FarBuilder.Size);
        if (_reachVersion != DetailReach.Version) ApplyReach();
        UpdateShade();

        if (_floraDirty.Count > 0)
        {
            // Dug ground changes flora: rebuild those chunks with a fresh snapshot of the dug set.
            _dugSnapshot = new HashSet<long>(_dug);
            _felledSnapshot = new HashSet<long>(World.Felled);
            foreach (var (fx, fz) in _floraDirty)
            {
                if (_far.TryGetValue((fx, fz), out FarChunk f)) { f.Lod = -1; f.Version++; }
                CoverDug(fx, fz);
            }
            _floraDirty.Clear();
        }

        List<(int fx, int fz, int lod, int d, float rank)> wanted = _farWanted;
        wanted.Clear();
        // Each level reaches as far round the focus itself as round the point ahead: what is left behind
        // a moving focus keeps its detail until it is as far off as anything is (flora dissolves by its
        // own distance, so a level dropping under it must be past its reach).
        int bx = (int)Math.Floor(cx / FarBuilder.Size) - ffx, bz = (int)Math.Floor(cz / FarBuilder.Size) - ffz;
        if (bx > nx / 2) bx -= nx; else if (bx < -nx / 2) bx += nx;
        if (bz > nz / 2) bz -= nz; else if (bz < -nz / 2) bz += nz;
        static int LodAt(int d) => d <= FarLod0Radius ? 0 : d <= FarLod1Radius ? 1 : 2;
        for (int dz = -FarRadius; dz <= FarRadius; dz++)
        for (int dx = -FarRadius; dx <= FarRadius; dx++)
        {
            int d = Math.Max(Math.Abs(dx), Math.Abs(dz)), back = Math.Max(Math.Abs(dx - bx), Math.Abs(dz - bz));
            wanted.Add((Mod(ffx + dx, nx), Mod(ffz + dz, nz), Math.Min(LodAt(d), LodAt(back)), Math.Min(d, back), Math.Min(d, back) - Ahead(dx, dz) * 0.5f));
        }
        // The whole square round the focus stays too, so nothing within the detail distance of it (the
        // distant drawings of trees reach to there) goes from behind or beside a moving focus.
        if (bx != 0 || bz != 0)
            for (int dz = bz - FarRadius; dz <= bz + FarRadius; dz++)
            for (int dx = bx - FarRadius; dx <= bx + FarRadius; dx++)
            {
                if (Math.Max(Math.Abs(dx), Math.Abs(dz)) <= FarRadius) continue; // in the square ahead already
                int back = Math.Max(Math.Abs(dx - bx), Math.Abs(dz - bz));
                wanted.Add((Mod(ffx + dx, nx), Mod(ffz + dz, nz), LodAt(back), back, back));
            }
        wanted.Sort(static (a, b) => a.rank.CompareTo(b.rank));
        HashSet<(int, int)> keep = _farKeep;
        keep.Clear();
        FarBehind = 0;
        FarBehindNearest = int.MaxValue;
        foreach (var (fx, fz, lod, d, _) in wanted)
        {
            keep.Add((fx, fz));
            if (!_far.TryGetValue((fx, fz), out FarChunk f)) _far[(fx, fz)] = f = new FarChunk { Fx = fx, Fz = fz };
            f.WantedLod = lod;
            if (f.Lod != lod)
            {
                FarBehind++;
                FarBehindNearest = Math.Min(FarBehindNearest, d);
            }
            if (f.Lod == lod || f.InFlight || _farInFlight >= 3) continue;
            f.InFlight = true;
            _farInFlight++;
            BuildFar(new FarJob
            {
                Fx = fx, Fz = fz, Lod = lod, Version = f.Version,
                SpanX = FarBuilder.Span(fx, World.Terrain.Width), SpanZ = FarBuilder.Span(fz, World.Terrain.Height),
            });
        }

        // Far chunks share the frame's upload budget with the voxels: one at most, and only if the
        // voxel uploads left time for it, so the two never stack into one long frame.
        // But a run of busy frames (walking with the voxels streaming) never starves them: every few frames one goes in anyway.
        int applied = 0;
        if (!_farDone.IsEmpty) _farWaited++;
        while (_farDone.TryPeek(out _))
        {
            if (applied > 0 || Stopwatch.GetElapsedTime(_frameStart).TotalMilliseconds > UploadBudgetMs && _farWaited < FarPatience) break;
            _farWaited = 0;
            _farDone.TryDequeue(out FarJob job);
            _farInFlight--;
            applied++;
            if (!_far.TryGetValue((job.Fx, job.Fz), out FarChunk f)) continue;
            f.InFlight = false;
            if (job.Error != null)
            {
                GD.PushError($"far chunk {job.Fx},{job.Fz} failed: {job.Error}");
                continue;
            }
            FarBuildMs[job.Lod] += job.Milliseconds;
            FarBuilds[job.Lod]++;
            if (job.Version != f.Version) continue; // flora went stale: it is requested again
            NotePops(f, job);
            Node3D old = f.Node;
            long nodeStart = Stopwatch.GetTimestamp();
            f.Node = BuildFarNode(job);
            f.Flora = job.Flora;
            f.Vertices = job.Vertices;
            f.Indices = job.Indices;
            f.Cells = null;
            double nodeMs = Stopwatch.GetElapsedTime(nodeStart).TotalMilliseconds;
            if (nodeMs > WorstFarNodeMs)
            {
                WorstFarNodeMs = nodeMs;
                int flora = 0;
                foreach (var (_, _, count) in job.Flora) flora += count;
                WorstFarNode = $"lod {job.Lod}, {job.Vertices.Length} vertices, {job.Flora.Count} flora kinds with {flora} instances, water {job.WaterVertices?.Length ?? 0}";
            }
            f.Lod = job.Lod;
            f.Solids = job.Solids;
            if (job.Lod == 0) SolidsChanged(job.Fx, job.Fz);
            AddChild(f.Node);
            BeginSwap(job.Fx, job.Fz, old, f.Node);
        }

        // Drop far chunks out of range; place the rest at their copy nearest the focus.
        List<(int, int)> gone = null;
        foreach (var (key, f) in _far)
        {
            if (!keep.Contains(key))
            {
                NoteGone(f);
                EndSwap(key);
                f.Node?.QueueFree();
                (gone ??= new List<(int, int)>()).Add(key);
                continue;
            }
            if (f.Node == null) continue;
            // Written only when it moves (a wrap round the planet): a same-spot write still tells every batch under it.
            var at = new Vector3((float)NearestCopy(f.Fx * FarBuilder.Size, FarBuilder.Size, Focus.X, w), 0, (float)NearestCopy(f.Fz * FarBuilder.Size, FarBuilder.Size, Focus.Z, h));
            if (f.Node.Position != at) f.Node.Position = at;
        }
        if (gone != null) foreach (var key in gone) _far.Remove(key);

        // The horizon cuts out the square the far rings cover completely.
        int full = -1;
        for (int r = 0; r <= FarRadius; r++)
        {
            bool complete = true;
            for (int dz = -r; dz <= r && complete; dz++)
            for (int dx = -r; dx <= r && complete; dx++)
            {
                if (Math.Max(Math.Abs(dx), Math.Abs(dz)) != r) continue;
                complete = _far.TryGetValue((Mod(ffx + dx, nx), Mod(ffz + dz, nz)), out FarChunk f) && f.Node != null;
            }
            if (!complete) break;
            full = r;
        }
        Vector4 hole = Vector4.Zero;
        if (full >= 0)
        {
            // From the near edge of the first chunk of the square to the far edge of its last, in
            // the frame: a chunk cut short at a seam makes the square that much smaller. The horizon
            // sinks inside it and is cut well inside it (see SetWorld): it comes from the
            // coarse world map and often sits tens of metres above the far rings' outer edge, and a
            // cut right at that edge left a gap there that the sky showed through.
            var (x0, x1) = FarEdges(ffx - full, ffx + full, nx, World.Terrain.Width, Focus.X);
            var (z0, z1) = FarEdges(ffz - full, ffz + full, nz, World.Terrain.Height, Focus.Z);
            hole = new Vector4(x0, z0, x1, z1);
        }
        _horizonMaterial.SetShaderParameter("hole", hole);
        FarComplete = full >= FarRadius;
        UpdateHorizon(cx, cz, w, h);
    }

    /// <summary>
    /// The frame coordinates of the near edge of far chunk <paramref name="first"/> and the far edge
    /// of far chunk <paramref name="last"/> (unwrapped indices, a few chunks either side of the focus),
    /// along one axis of a planet <paramref name="planet"/> metres and <paramref name="count"/> chunks across.
    /// </summary>
    static (float from, float to) FarEdges(int first, int last, int count, int planet, double focus)
    {
        int a = Mod(first, count), b = Mod(last, count);
        double from = NearestCopy(a * FarBuilder.Size, FarBuilder.Span(a, planet), focus, planet);
        double to = NearestCopy(b * FarBuilder.Size, FarBuilder.Span(b, planet), focus, planet) + FarBuilder.Span(b, planet);
        return ((float)from, (float)to);
    }

    const int GroundCells = FarBuilder.Size / 16;

    /// <summary>
    /// The height of the far ground as drawn under a point (frame coordinates), m: the far chunk's own
    /// mesh, not the generator's ground, which the coarser levels only meet at their grid points. Null
    /// where no far chunk is drawn. What stands out on the far land stands on this (far creatures).
    /// </summary>
    public float? DrawnGround(Vector3 p)
    {
        if (World == null) return null;
        double w = World.Terrain.Width, h = World.Terrain.Height;
        int fx = (int)(Mod(p.X, w) / FarBuilder.Size), fz = (int)(Mod(p.Z, h) / FarBuilder.Size);
        if (!_far.TryGetValue((fx, fz), out FarChunk f) || f.Node == null || f.Vertices == null) return null;
        float lx = (float)Mod(p.X - fx * (double)FarBuilder.Size, w), lz = (float)Mod(p.Z - fz * (double)FarBuilder.Size, h);
        if (f.Cells == null)
        {
            f.Cells = new List<int>[GroundCells * GroundCells];
            for (int t = 0; t + 2 < f.Indices.Length; t += 3)
            {
                Vector3 a = f.Vertices[f.Indices[t]], b = f.Vertices[f.Indices[t + 1]], c = f.Vertices[f.Indices[t + 2]];
                // Walls (curtains, cliff faces seen edge-on from above) are not ground.
                float area = (b.X - a.X) * (c.Z - a.Z) - (c.X - a.X) * (b.Z - a.Z);
                if (MathF.Abs(area) < 1e-3f) continue;
                int i0 = Math.Clamp((int)(MathF.Min(a.X, MathF.Min(b.X, c.X)) / 16f), 0, GroundCells - 1), i1 = Math.Clamp((int)(MathF.Max(a.X, MathF.Max(b.X, c.X)) / 16f), 0, GroundCells - 1);
                int j0 = Math.Clamp((int)(MathF.Min(a.Z, MathF.Min(b.Z, c.Z)) / 16f), 0, GroundCells - 1), j1 = Math.Clamp((int)(MathF.Max(a.Z, MathF.Max(b.Z, c.Z)) / 16f), 0, GroundCells - 1);
                for (int j = j0; j <= j1; j++)
                for (int i = i0; i <= i1; i++)
                    (f.Cells[j * GroundCells + i] ??= new List<int>()).Add(t);
            }
        }
        List<int> tris = f.Cells[Math.Clamp((int)(lz / 16f), 0, GroundCells - 1) * GroundCells + Math.Clamp((int)(lx / 16f), 0, GroundCells - 1)];
        if (tris == null) return null;
        float? best = null;
        foreach (int t in tris)
        {
            Vector3 a = f.Vertices[f.Indices[t]], b = f.Vertices[f.Indices[t + 1]], c = f.Vertices[f.Indices[t + 2]];
            float d = (b.X - a.X) * (c.Z - a.Z) - (c.X - a.X) * (b.Z - a.Z);
            float u = ((b.X - lx) * (c.Z - lz) - (c.X - lx) * (b.Z - lz)) / d, v = ((c.X - lx) * (a.Z - lz) - (a.X - lx) * (c.Z - lz)) / d, wgt = 1f - u - v;
            if (u < -1e-4f || v < -1e-4f || wgt < -1e-4f) continue;
            float y = u * a.Y + v * b.Y + wgt * c.Y;
            if (best == null || y > best) best = y;
        }
        return best;
    }

    /// <summary>Whether a far chunk's ground is in under a point (frame coordinates). Tests ask it at the seams.</summary>
    public bool FarGroundAt(Vector3 p)
    {
        int fx = (int)(Mod(p.X, World.Terrain.Width) / FarBuilder.Size), fz = (int)(Mod(p.Z, World.Terrain.Height) / FarBuilder.Size);
        return _far.TryGetValue((fx, fz), out FarChunk f) && f.Node != null;
    }

    public bool FarComplete { get; private set; }

    /// <summary>Far chunks not yet at the detail wanted of them, and the nearest ring holding one (int.MaxValue: none). Tests and measurements read these.</summary>
    public int FarBehind { get; private set; }
    public int FarBehindNearest { get; private set; } = int.MaxValue;

    /// <summary>
    /// The flora drawn within a radius of a point (frame coordinates): each plant's kind, where its
    /// trunk meets the ground, its scale and its scatter id (the one <see cref="PlanetWorld.Felled"/>
    /// and <see cref="Fell"/> take). Read from the far chunks' own scatter, so it works with or
    /// without a renderer; felled plants are left out at once.
    /// </summary>
    public IEnumerable<PlantSighting> FloraNear(Vector3 at, float radius)
    {
        double w = World.Terrain.Width, h = World.Terrain.Height;
        foreach (FarChunk f in _far.Values)
        {
            if (f.Flora == null) continue;
            float x0 = (float)NearestCopy(f.Fx * FarBuilder.Size, FarBuilder.Size, at.X, w);
            float z0 = (float)NearestCopy(f.Fz * FarBuilder.Size, FarBuilder.Size, at.Z, h);
            if (at.X < x0 - radius || at.X > x0 + FarBuilder.Size + radius || at.Z < z0 - radius || at.Z > z0 + FarBuilder.Size + radius) continue;
            foreach (FloraBatch b in f.Flora)
            {
                for (int i = 0; i < b.Count; i++)
                {
                    int o = i * 16;
                    float[] buffer = b.Buffer;
                    var root = new Vector3(x0 + buffer[o + 3], buffer[o + 7], z0 + buffer[o + 11]);
                    if (new Vector2(root.X - at.X, root.Z - at.Z).Length() > radius) continue;
                    // The scatter cell it was rolled in: its offset in the chunk, a cell to a ScatterCell.
                    long id = FarBuilder.ScatterId(f.Fx, f.Fz, (int)(buffer[o + 3] / FarBuilder.ScatterCell), (int)(buffer[o + 11] / FarBuilder.ScatterCell));
                    var turn = new Basis(new Vector3(buffer[o], buffer[o + 4], buffer[o + 8]), new Vector3(buffer[o + 1], buffer[o + 5], buffer[o + 9]), new Vector3(buffer[o + 2], buffer[o + 6], buffer[o + 10]));
                    if (!World.Felled.Contains(id)) yield return new PlantSighting(b.Kind, root, buffer[o + 5], id, b.Plant, b.Variant, turn, new Color(buffer[o + 12], buffer[o + 13], buffer[o + 14], buffer[o + 15]));
                }
            }
        }
    }

    /// <summary>The mesh a plant is drawn with: its species' variant, or its kind's where the herbarium has none. What it is felled or thrown as.</summary>
    public Mesh MeshOf(in PlantSighting s) => PlantMesh(s.Plant, s.Variant) ?? KindMesh(s.Kind);

    /// <summary>One plant seen in the scatter: its kind, where it roots, its scale, id, species and mesh, its turn, and its instance's own tint and glow.</summary>
    public readonly record struct PlantSighting(Flora Kind, Vector3 Root, float Scale, long Id, int Plant, int Variant, Basis Turn, Color Custom = default)
    {
        /// <summary>Its own tint and glow (small growth's glow carries its thinning marker, +2).</summary>
        public Color OwnTint => Custom.A >= 1.5f ? new Color(Custom.R, Custom.G, Custom.B, Custom.A - 2f) : Custom;

        public void Deconstruct(out Flora kind, out Vector3 root, out float scale, out long id)
        {
            kind = Kind;
            root = Root;
            scale = Scale;
            id = Id;
        }
    }

    public double WorstFarNodeMs { get; private set; }

    /// <summary>Workers' time building far chunks so far, by level of detail, and how many: tests report it.</summary>
    public readonly double[] FarBuildMs = new double[3];
    public readonly int[] FarBuilds = new int[3];
    public string WorstFarNode { get; private set; } = "";

    /// <summary>A canonical position moved by whole planets to the copy nearest the focus (frame coordinates).</summary>
    public static double NearestCopy(double canonical, double size, double focus, double span) =>
        canonical + span * Math.Round((focus - canonical - size * 0.5) / span);

    Node3D BuildFarNode(FarJob job)
    {
        var node = new Node3D { Name = $"Far {job.Fx} {job.Fz} lod{job.Lod}", PhysicsInterpolationMode = PhysicsInterpolationModeEnum.Off };
        var arrays = new Godot.Collections.Array();
        arrays.Resize((int)Mesh.ArrayType.Max);
        arrays[(int)Mesh.ArrayType.Vertex] = job.Vertices;
        arrays[(int)Mesh.ArrayType.Normal] = job.Normals;
        arrays[(int)Mesh.ArrayType.Color] = job.Colors;
        arrays[(int)Mesh.ArrayType.TexUV] = job.Flags;
        arrays[(int)Mesh.ArrayType.TexUV2] = job.Relief;
        arrays[(int)Mesh.ArrayType.Index] = job.Indices;
        var mesh = new ArrayMesh();
        mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays);
        node.AddChild(new MeshInstance3D
        {
            Mesh = mesh, MaterialOverride = FarMaterial,
            CastShadow = job.Lod <= 1 ? GeometryInstance3D.ShadowCastingSetting.On : GeometryInstance3D.ShadowCastingSetting.Off,
        });
        if (job.WaterVertices != null)
        {
            var water = new Godot.Collections.Array();
            water.Resize((int)Mesh.ArrayType.Max);
            water[(int)Mesh.ArrayType.Vertex] = job.WaterVertices;
            water[(int)Mesh.ArrayType.Index] = job.WaterIndices;
            var waterMesh = new ArrayMesh();
            waterMesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, water);
            node.AddChild(new MeshInstance3D { Mesh = waterMesh, MaterialOverride = _waterMaterial, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off });
        }
        foreach (FloraBatch b in job.Flora)
        {
            var multimesh = new MultiMesh { TransformFormat = MultiMesh.TransformFormatEnum.Transform3D, UseCustomData = true, Mesh = PlantMesh(b.Plant, b.Variant) ?? _floraMeshes[b.Kind] };
            multimesh.InstanceCount = b.Count;
            multimesh.Buffer = b.Buffer;
            int layer = b.Layer;
            var flora = new MultiMeshInstance3D
            {
                Name = $"L{layer}",
                Multimesh = multimesh, MaterialOverride = _floraMaterial,
                // A tree casts its shadow as far as the sun's shadows reach; small growth only in the finest chunks.
                CastShadow = layer != SmallLayer || job.Lod == 0 ? GeometryInstance3D.ShadowCastingSetting.On : GeometryInstance3D.ShadowCastingSetting.Off,
                // The wind bends a crown up to a metre or so past the box its instances stand in: it is not culled at the screen's edge.
                ExtraCullMargin = 1.5f,
            };
            SetRange(flora, layer);
            flora.SetInstanceShaderParameter("flora_layer", (float)layer);
            node.AddChild(flora);
        }
        return node;
    }

    int _reachVersion = -1;

    /// <summary>
    /// How far a flora batch is drawn: its layer's reach, and half its box's diagonal past it. The renderer measures from
    /// the eye to the box's centre, and each plant dissolves by its own distance from the eye (FloraShows), so a batch
    /// is never left out while any plant in it shows, from the ground or high over it.
    /// </summary>
    static void SetRange(MultiMeshInstance3D g, int layer) => g.VisibilityRangeEnd = FloraLayerReach(layer) + g.Multimesh.GetAabb().Size.Length() * 0.5f;

    /// <summary>The detail distance changed (<see cref="DetailReach"/>): flora's reaches into its shader, and every batch drawn re-ranged. The rings lay themselves out again as they go.</summary>
    void ApplyReach()
    {
        _reachVersion = DetailReach.Version;
        _floraMaterial.SetShaderParameter("flora_reach", new Vector2(DetailReach.Large, DetailReach.Small));
        _floraMaterial.SetShaderParameter("flora_bands", new Vector2(DetailReach.LargeBand, DetailReach.SmallBand));
        foreach (FarChunk f in _far.Values)
        {
            if (f.Node == null) continue;
            foreach (Node child in f.Node.GetChildren())
                if (child is MultiMeshInstance3D m && m.Name.ToString() is ['L', var c]) SetRange(m, c - '0');
        }
    }

    void UpdateHorizon(double cx, double cz, double w, double h)
    {
        long horizonStart = Stopwatch.GetTimestamp();
        if (_horizonJob != null && _horizonJob.IsCompleted)
        {
            var (arrays, hx, hz) = _horizonJob.Result;
            _horizonJob = null;
            var mesh = new ArrayMesh();
            mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays);
            _horizon.Mesh = mesh;
            _horizonX = hx;
            _horizonZ = hz;
        }
        if (_horizonJob == null && Math.Abs(Mathx.WrapDelta(cx - _horizonX, w)) + Math.Abs(Mathx.WrapDelta(cz - _horizonZ, h)) > HorizonRebuild)
        {
            double hx = Math.Round(cx / HorizonSpacing) * HorizonSpacing, hz = Math.Round(cz / HorizonSpacing) * HorizonSpacing;
            PlanetWorld world = World;
            _horizonJob = Task.Run(() => (BuildHorizon(world, hx, hz), hx, hz));
        }
        if (_horizon.Mesh != null)
        {
            _horizon.Position = new Vector3((float)NearestCopy(_horizonX, 0, Focus.X, w), 0, (float)NearestCopy(_horizonZ, 0, Focus.Z, h));
            HorizonRim = new Vector3(_horizon.Position.X, _horizon.Position.Z, HorizonReach);
            _horizonMaterial.SetShaderParameter("rim", FarFieldBehind ? HorizonRim : NoRim);
        }
        _sections[2] = Stopwatch.GetElapsedTime(horizonStart).TotalMilliseconds;
    }

    /// <summary>The horizon: a coarse grid straight from the world map, sitting a little low so finer ground always wins.</summary>
    static Godot.Collections.Array BuildHorizon(PlanetWorld world, double cx, double cz)
    {
        WorldMap map = world.Map;
        GroundPalette pal = world.Palette;
        int n = (int)(HorizonReach * 2 / HorizonSpacing) + 1;
        var verts = new Vector3[n * n];
        var normals = new Vector3[n * n];
        var colors = new Color[n * n];
        var flags = new Vector2[n * n];
        var smooth = new Vector2[n * n];
        var height = new float[n * n];
        for (int j = 0; j < n; j++)
        for (int i = 0; i < n; i++)
        {
            double x = Mathx.WrapX(cx - HorizonReach + i * HorizonSpacing, map.WidthMeters), z = Mathx.Wrap(cz - HorizonReach + j * HorizonSpacing, map.HeightMeters);
            height[j * n + i] = map.Sample(map.Elevation, x, z);
        }
        for (int j = 0; j < n; j++)
        for (int i = 0; i < n; i++)
        {
            int k = j * n + i;
            double x = cx - HorizonReach + i * HorizonSpacing, z = cz - HorizonReach + j * HorizonSpacing;
            float hl = height[j * n + Math.Max(i - 1, 0)], hr = height[j * n + Math.Min(i + 1, n - 1)];
            float hd = height[Math.Max(j - 1, 0) * n + i], hu = height[Math.Min(j + 1, n - 1) * n + i];
            var normal = new Vector3(hl - hr, 2f * HorizonSpacing, hd - hu).Normalized();
            float hgt = height[k];
            verts[k] = new Vector3((float)(x - cx), hgt - 2f, (float)(z - cz));
            normals[k] = normal;
            smooth[k] = new Vector2(Smoothed(height, n, i, j) - 2f, 0f);
            Rgb c = pal.FarGround(Mathx.WrapX(x, map.WidthMeters), Mathx.Wrap(z, map.HeightMeters), hgt, normal.Y, out float bare);
            colors[k] = new Color(c.R, c.G, c.B, bare);
        }
        var indices = new int[(n - 1) * (n - 1) * 6];
        int t = 0;
        for (int j = 0; j < n - 1; j++)
        for (int i = 0; i < n - 1; i++)
        {
            int a = j * n + i, b = a + 1, c = a + n, d = c + 1;
            indices[t++] = a; indices[t++] = b; indices[t++] = c;
            indices[t++] = b; indices[t++] = d; indices[t++] = c;
        }
        var arrays = new Godot.Collections.Array();
        arrays.Resize((int)Mesh.ArrayType.Max);
        arrays[(int)Mesh.ArrayType.Vertex] = verts;
        arrays[(int)Mesh.ArrayType.Normal] = normals;
        arrays[(int)Mesh.ArrayType.Color] = colors;
        arrays[(int)Mesh.ArrayType.TexUV] = flags;
        arrays[(int)Mesh.ArrayType.TexUV2] = smooth;
        arrays[(int)Mesh.ArrayType.Index] = indices;
        return arrays;
    }

    /// <summary>How far round a horizon vertex its ground is smoothed, in grid steps: about what the far field's relief holds where the land meets it.</summary>
    const int SmoothSteps = 2;

    /// <summary>
    /// The ground at a horizon vertex as the far field past it has it: the mean of the heights round
    /// it, the sea floor at the sea's level. Toward its rim the land eases onto this (terrain.gdshader),
    /// so where it thins away the sheet behind is the same land at the same height: no peak goes to
    /// a ghost of itself with the sky through it.
    /// </summary>
    static float Smoothed(float[] height, int n, int i, int j)
    {
        float sum = 0f;
        int count = 0;
        for (int dj = -SmoothSteps; dj <= SmoothSteps; dj++)
        for (int di = -SmoothSteps; di <= SmoothSteps; di++)
        {
            int a = Math.Clamp(i + di, 0, n - 1), b = Math.Clamp(j + dj, 0, n - 1);
            sum += MathF.Max(height[b * n + a], 0f);
            count++;
        }
        return sum / count;
    }

    // ---- Housekeeping ---------------------------------------------------------------------

    /// <summary>Forgets generated cells and cached columns far from the focus. Edits are never forgotten.</summary>
    void Evict()
    {
        var (fcx, fcz) = FocusColumn;
        int keep = VoxelRadius + 3;
        World.Terrain.Store.Base.Forget(c => Math.Max(Math.Abs(c.X - fcx), Math.Abs(c.Z - fcz)) > keep);
        int nx = World.Terrain.ChunksX, nz = World.Terrain.ChunksZ;
        int ccx = Mod(fcx, nx), ccz = Mod(fcz, nz);
        World.Source.ForgetColumns((x, z) =>
        {
            int dx = Math.Abs(x - ccx), dz = Math.Abs(z - ccz);
            dx = Math.Min(dx, nx - dx);
            dz = Math.Min(dz, nz - dz);
            return Math.Max(dx, dz) > keep;
        });
    }

    /// <summary>Drops every node and pending result (switching planets).</summary>
    public void Clear()
    {
        foreach (VChunk c in _chunks.Values)
        {
            c.Mesh?.QueueFree();
            c.Body?.QueueFree();
        }
        foreach (FarChunk f in _far.Values) f.Node?.QueueFree();
        ClearSwaps();
        if (World != null) ClearSolids();
        ClearCover();
        _chunks.Clear();
        _columns.Clear();
        _far.Clear();
        _queue.Clear();
        _dug.Clear();
        _dugSnapshot = new HashSet<long>();
        _floraDirty.Clear();
        // Results still in flight belong to the old planet: count them out as they land.
        while (_voxelDone.TryDequeue(out _)) _voxelInFlight--;
        while (_farDone.TryDequeue(out _)) _farInFlight--;
        while (_heightsDone.TryDequeue(out _)) _heightsInFlight--;
        _voxelInFlight = _heightsInFlight = _farInFlight = 0;
        if (_horizon != null) _horizon.Mesh = null;
        _horizonJob = null;
        _horizonX = _horizonZ = double.MaxValue;
        Triangles = 0;
        _completeRadius = -1;
        World = null;
    }

    // ---- Queries --------------------------------------------------------------------------

    /// <summary>Whether every chunk within <paramref name="columns"/> of a point is meshed, with collision if wanted.</summary>
    public bool ReadyAround(Vector3 p, int columns, bool needCollision)
    {
        int cx = (int)MathF.Floor(p.X / S), cz = (int)MathF.Floor(p.Z / S);
        for (int dz = -columns; dz <= columns; dz++)
        for (int dx = -columns; dx <= columns; dx++)
        {
            if (!_columns.TryGetValue((cx + dx, cz + dz), out Column c) || !c.Built) return false;
            if (!needCollision) continue;
            foreach (VChunk chunk in c.Chunks)
                if (chunk.HasTriangles && (chunk.Body == null || chunk.Faces == null || chunk.ShapeHash != chunk.Hash))
                    return false;
        }
        return true;
    }

    /// <summary>True once the voxel square, the far rings and the horizon are all in.</summary>
    public bool Settled => _completeRadius >= VoxelRadius && FarComplete && _horizon.Mesh != null && _queue.Count == 0 && _voxelInFlight == 0;

    /// <summary>The mesh hash of a loaded chunk (frame key), for tests.</summary>
    public bool TryGetHash(Int3 key, out ulong hash)
    {
        hash = 0;
        if (!_chunks.TryGetValue(key, out VChunk c) || c.BuiltVersion < 0 || c.InFlight || c.Queued) return false;
        hash = c.Hash;
        return true;
    }

    public bool IsLoaded(Int3 key) => _chunks.ContainsKey(key);

    /// <summary>
    /// Whether the ground at a point has collision to land on: its column is built and every chunk in it that has a surface has a
    /// live shape. Beyond the collision radius it hasn't, and a loose body there would fall through the world.
    /// </summary>
    public bool CollidesAt(Vector3 p)
    {
        if (!_columns.TryGetValue(((int)MathF.Floor(p.X / S), (int)MathF.Floor(p.Z / S)), out Column c) || !c.Built) return false;
        foreach (VChunk chunk in c.Chunks)
            if (chunk.HasTriangles && (chunk.Shape == null || chunk.Shape.Disabled)) return false;
        return true;
    }

    /// <summary>Whether a loaded chunk has live collision (tests).</summary>
    public bool Collides(Int3 key) => _chunks.TryGetValue(key, out VChunk c) && c.Shape != null && !c.Shape.Disabled;

    /// <summary>
    /// Whether a chunk has been meshed in the current frame, so its cells (and its neighbours') are
    /// already generated and reading them costs nothing. Main-thread queries that only want a
    /// glance (is there rock overhead?) check this first, so they never generate terrain here.
    /// </summary>
    public bool IsReady(Int3 key) => _chunks.TryGetValue(key, out VChunk c) && c.BuiltVersion >= 0 && c.BuiltKey == key;

    /// <summary>The loaded chunk's mesh in frame coordinates, for tests (seams, normals).</summary>
    public ArrayMesh MeshOf(Int3 key, out Vector3 offset)
    {
        offset = Vector3.Zero;
        if (!_chunks.TryGetValue(key, out VChunk c) || c.Mesh == null) return null;
        offset = c.Mesh.Position;
        return c.Mesh.Mesh as ArrayMesh;
    }

    /// <summary>The voxel meshes on show right now, with their offsets in the frame: what a test's rays must meet.</summary>
    public IEnumerable<(ArrayMesh Mesh, Vector3 Offset)> DrawnVoxels()
    {
        foreach (VChunk c in _chunks.Values)
            if (c.Mesh != null && c.Mesh.Visible && c.Mesh.Mesh is ArrayMesh mesh) yield return (mesh, c.Mesh.Position);
    }

    /// <summary>The far chunks' ground meshes on show right now, with their positions in the frame and their levels.</summary>
    public IEnumerable<(ArrayMesh Mesh, Vector3 At, int Lod)> DrawnFar()
    {
        foreach (FarChunk f in _far.Values)
            if (f.Node != null && f.Node.GetChild(0) is MeshInstance3D { Mesh: ArrayMesh mesh }) yield return (mesh, f.Node.Position, f.Lod);
    }

    /// <summary>The horizon's mesh and where it stands in the frame. Null until it is built.</summary>
    public (ArrayMesh Mesh, Vector3 At) DrawnHorizon() => (_horizon.Mesh as ArrayMesh, _horizon.Position);

    public ShaderMaterial HorizonMaterial => _horizonMaterial;

    /// <summary>Where the land's own mesh ends, for what has to end with it: its centre (x, z in the frame) and how far it reaches. <see cref="NoRim"/> until it's built.</summary>
    public Vector3 HorizonRim { get; private set; } = NoRim;

    /// <summary>A rim that cuts nothing.</summary>
    public static readonly Vector3 NoRim = new(0f, 0f, -1f);

    /// <summary>A far field stands behind the land out to the horizon, so the mesh's edge thins away into it.</summary>
    public bool FarFieldBehind;

    static int Mod(int v, int m) => (v % m + m) % m;
    static double Mod(double v, double m)
    {
        v %= m;
        return v < 0 ? v + m : v;
    }
}
