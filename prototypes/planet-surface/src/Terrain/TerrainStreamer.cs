using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading.Tasks;
using GalHack.Terrain;
using Godot;
using Planets.Gen;
using Planets.Look;
using Planets.Surface;
using PlanetSurface.World;
using TerrainDigging.Terrain;

namespace PlanetSurface.Terrain;

/// <summary>
/// Streams the planet around a focus point (the player, or the free-fly camera):
/// - <b>Near:</b> diggable 1 m voxel chunks (terrain-digging's Surface Nets) in a square of
///   columns around the focus. Column heights are computed on a worker, then each chunk is
///   meshed on a worker from an immutable snapshot of the store. Collision exists only close to
///   the player, where something can touch it.
/// - <b>Far:</b> heightfield chunks from the same source at 2, 4 and 8 m out to about a kilometre,
///   then a horizon mesh straight from the planet generator's world map.
/// Each level cuts a hole where the finer one is complete, so they never overlap.
///
/// The main thread only takes snapshots and swaps finished meshes in, under a per-frame time
/// budget. Voxel chunks live in the terrain's frame (see <see cref="PlanetTerrain"/>); when the
/// player crosses a seam the frame shifts by a whole planet and the loaded chunks are re-keyed and
/// moved, never rebuilt. Far chunks are keyed canonically and drawn at their copy nearest the focus.
/// </summary>
public partial class TerrainStreamer : Node3D
{
    const int S = ChunkStore.Size;

    public int VoxelRadius = 5;
    public int CollisionRadius = 2;
    public bool CollisionEnabled = true;
    public double UploadBudgetMs = 3.0;
    public const int FarRadius = 7, FarLod0Radius = 1, FarLod1Radius = 3;
    const float HorizonSpacing = 64f, HorizonReach = 7000f, HorizonRebuild = 1200f;

    public PlanetWorld World { get; private set; }
    public Vector3 Focus { get; private set; }

    public ShaderMaterial VoxelMaterial { get; private set; }
    public ShaderMaterial FarMaterial { get; private set; }
    ShaderMaterial _horizonMaterial, _floraMaterial;
    Material _waterMaterial;
    Dictionary<Flora, Mesh> _floraMeshes;

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
        public bool InFlight, Queued;
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
    }

    readonly Dictionary<(int, int), FarChunk> _far = new();
    readonly ConcurrentQueue<FarJob> _farDone = new();
    readonly List<(int fx, int fz, int lod, int d)> _farWanted = new();
    readonly HashSet<(int, int)> _farKeep = new();
    int _farInFlight;
    MeshInstance3D _horizon;
    Task<(Godot.Collections.Array arrays, double x, double z)> _horizonJob;
    double _horizonX = double.MaxValue, _horizonZ = double.MaxValue;
    readonly HashSet<long> _dug = new();
    HashSet<long> _dugSnapshot = new();
    readonly HashSet<(int, int)> _floraDirty = new();
    int _evictTimer;

    // ---- Stats ----
    public double LastFrameMs { get; private set; }
    public double WorstFrameMs { get; private set; }

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
    public int Pending => _queue.Count + _voxelInFlight + _heightsInFlight + _farInFlight;

    /// <summary>A voxel chunk's new mesh was swapped in (frame key).</summary>
    public event Action<Int3> ChunkApplied;

    public void ResetStats() => WorstFrameMs = 0;

    public override void _Ready()
    {
        _floraMeshes = FloraMeshes.BuildAll();
        _horizon = new MeshInstance3D { Name = "Horizon", CastShadow = GeometryInstance3D.ShadowCastingSetting.Off };
        AddChild(_horizon);
    }

    /// <summary>Starts streaming a planet (dropping everything of the previous one).</summary>
    public void SetWorld(PlanetWorld world, Vector3 focus, Material water)
    {
        Clear();
        World = world;
        Focus = focus;
        _waterMaterial = water;
        VoxelMaterial = TerrainMaterial(far: false);
        FarMaterial = TerrainMaterial(far: true);
        _horizonMaterial = TerrainMaterial(far: true);
        _horizon.MaterialOverride = _horizonMaterial;
        _floraMaterial = new ShaderMaterial { Shader = GD.Load<Shader>("res://shaders/flora.gdshader") };
    }

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
        int d = Math.Max(Math.Abs(c.Key.X - FocusColumn.x), Math.Abs(c.Key.Z - FocusColumn.z));
        _queue.Enqueue(c.Key, urgent ? -1 : d * 4 + Math.Abs(c.Key.Y - (int)MathF.Floor(Focus.Y / S)));
    }

    (int x, int z) FocusColumn => ((int)MathF.Floor(Focus.X / S), (int)MathF.Floor(Focus.Z / S));

    // ---- Every frame ----------------------------------------------------------------------

    public override void _Process(double delta)
    {
        if (World == null) return;
        long start = Stopwatch.GetTimestamp();
        _frameStart = start;
        Array.Clear(_sections);

        StreamVoxels();
        _sections[0] = Stopwatch.GetElapsedTime(start).TotalMilliseconds - _sections[4];
        long t = Stopwatch.GetTimestamp();
        StreamFar();
        _sections[1] = Stopwatch.GetElapsedTime(t).TotalMilliseconds - _sections[2];
        t = Stopwatch.GetTimestamp();
        if (++_evictTimer >= 30)
        {
            _evictTimer = 0;
            Evict();
        }
        _sections[3] = Stopwatch.GetElapsedTime(t).TotalMilliseconds;

        LastFrameMs = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
        if (LastFrameMs > WorstFrameMs)
        {
            WorstFrameMs = LastFrameMs;
            var parts = new System.Text.StringBuilder();
            for (int i = 0; i < _sections.Length; i++) parts.Append($"{SectionNames[i]} {_sections[i]:F1} ");
            WorstBreakdown = parts.ToString().TrimEnd();
        }
    }

    void StreamVoxels()
    {
        var (fcx, fcz) = FocusColumn;
        PlanetTerrainSource source = World.Source;

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
            int cx = column.Cx, cz = column.Cz;
            Task.Run(() =>
            {
                try { source.ColumnAt(cx, cz); }
                catch (Exception e) { GD.PushError($"column {cx},{cz} failed: {e}"); }
                _heightsDone.Enqueue((cx, cz));
            });
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
            c.Queued = false;
            if (c.InFlight) continue; // picked up again when the running job lands
            c.InFlight = true;
            _voxelInFlight++;
            bool near = CollisionEnabled && Math.Max(Math.Abs(next.X - fcx), Math.Abs(next.Z - fcz)) <= CollisionRadius + 2;
            var job = new VoxelJob { Key = next, Version = c.Version, Hood = World.Terrain.Store.CaptureNeighborhood(next), NeedFaces = near };
            GroundPalette palette = World.Palette;
            double w = World.Terrain.Width, h = World.Terrain.Height;
            Task.Run(() =>
            {
                VoxelMeshing.Build(job, palette, w, h);
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
            else c.ShapeHash = 0;
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
                c.Body = new StaticBody3D { Name = $"Ground {c.Key.X} {c.Key.Y} {c.Key.Z}", CollisionLayer = Layers.Terrain, CollisionMask = 0 };
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

    void StreamFar()
    {
        double w = World.Terrain.Width, h = World.Terrain.Height;
        int nx = World.Terrain.Width / FarBuilder.Size, nz = World.Terrain.Height / FarBuilder.Size;
        double cx = Mod(Focus.X, w), cz = Mod(Focus.Z, h);
        int ffx = (int)Math.Floor(cx / FarBuilder.Size), ffz = (int)Math.Floor(cz / FarBuilder.Size);

        if (_floraDirty.Count > 0)
        {
            // Dug ground changes flora: rebuild those chunks with a fresh snapshot of the dug set.
            _dugSnapshot = new HashSet<long>(_dug);
            foreach (var (fx, fz) in _floraDirty)
                if (_far.TryGetValue((fx, fz), out FarChunk f)) { f.Lod = -1; f.Version++; }
            _floraDirty.Clear();
        }

        List<(int fx, int fz, int lod, int d)> wanted = _farWanted;
        wanted.Clear();
        for (int dz = -FarRadius; dz <= FarRadius; dz++)
        for (int dx = -FarRadius; dx <= FarRadius; dx++)
        {
            int d = Math.Max(Math.Abs(dx), Math.Abs(dz));
            int lod = d <= FarLod0Radius ? 0 : d <= FarLod1Radius ? 1 : 2;
            wanted.Add((Mod(ffx + dx, nx), Mod(ffz + dz, nz), lod, d));
        }
        wanted.Sort(static (a, b) => a.d.CompareTo(b.d));
        HashSet<(int, int)> keep = _farKeep;
        keep.Clear();
        foreach (var (fx, fz, lod, _) in wanted)
        {
            keep.Add((fx, fz));
            if (!_far.TryGetValue((fx, fz), out FarChunk f)) _far[(fx, fz)] = f = new FarChunk { Fx = fx, Fz = fz };
            f.WantedLod = lod;
            if (f.Lod == lod || f.InFlight || _farInFlight >= 3) continue;
            f.InFlight = true;
            _farInFlight++;
            var job = new FarJob { Fx = fx, Fz = fz, Lod = lod, Version = f.Version };
            PlanetTerrainSource source = World.Source;
            GroundPalette palette = World.Palette;
            HashSet<long> dug = _dugSnapshot;
            Task.Run(() =>
            {
                FarBuilder.Build(job, source, palette, dug);
                _farDone.Enqueue(job);
            });
        }

        // Far chunks share the frame's upload budget with the voxels: one at most, and only if the
        // voxel uploads left time for it, so the two never stack into one long frame.
        int applied = 0;
        while (_farDone.TryPeek(out _))
        {
            if (applied > 0 || Stopwatch.GetElapsedTime(_frameStart).TotalMilliseconds > UploadBudgetMs) break;
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
            if (job.Version != f.Version) continue; // flora went stale: it is requested again
            f.Node?.QueueFree();
            long nodeStart = Stopwatch.GetTimestamp();
            f.Node = BuildFarNode(job);
            double nodeMs = Stopwatch.GetElapsedTime(nodeStart).TotalMilliseconds;
            if (nodeMs > WorstFarNodeMs)
            {
                WorstFarNodeMs = nodeMs;
                int flora = 0;
                foreach (var (_, _, count) in job.Flora) flora += count;
                WorstFarNode = $"lod {job.Lod}, {job.Vertices.Length} vertices, {job.Flora.Count} flora kinds with {flora} instances, water {job.WaterVertices?.Length ?? 0}";
            }
            f.Lod = job.Lod;
            AddChild(f.Node);
        }

        // Drop far chunks out of range; place the rest at their copy nearest the focus.
        List<(int, int)> gone = null;
        foreach (var (key, f) in _far)
        {
            if (!keep.Contains(key))
            {
                f.Node?.QueueFree();
                (gone ??= new List<(int, int)>()).Add(key);
                continue;
            }
            if (f.Node != null) f.Node.Position = new Vector3((float)NearestCopy(f.Fx * FarBuilder.Size, FarBuilder.Size, Focus.X, w), 0, (float)NearestCopy(f.Fz * FarBuilder.Size, FarBuilder.Size, Focus.Z, h));
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
            float x0 = (float)NearestCopy(ffx * FarBuilder.Size, FarBuilder.Size, Focus.X, w) - full * FarBuilder.Size;
            float z0 = (float)NearestCopy(ffz * FarBuilder.Size, FarBuilder.Size, Focus.Z, h) - full * FarBuilder.Size;
            float span = (2 * full + 1) * FarBuilder.Size;
            hole = new Vector4(x0 + 1f, z0 + 1f, x0 + span - 1f, z0 + span - 1f);
        }
        _horizonMaterial.SetShaderParameter("hole", hole);
        FarComplete = full >= FarRadius;
        UpdateHorizon(cx, cz, w, h);
    }

    public bool FarComplete { get; private set; }
    public double WorstFarNodeMs { get; private set; }
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
        foreach (var (kind, buffer, count) in job.Flora)
        {
            var multimesh = new MultiMesh { TransformFormat = MultiMesh.TransformFormatEnum.Transform3D, UseCustomData = true, Mesh = _floraMeshes[kind] };
            multimesh.InstanceCount = count;
            multimesh.Buffer = buffer;
            node.AddChild(new MultiMeshInstance3D
            {
                Multimesh = multimesh, MaterialOverride = _floraMaterial,
                CastShadow = job.Lod == 0 ? GeometryInstance3D.ShadowCastingSetting.On : GeometryInstance3D.ShadowCastingSetting.Off,
                VisibilityRangeEnd = job.Lod == 0 ? 520f : 760f,
                VisibilityRangeEndMargin = 60f,
                VisibilityRangeFadeMode = GeometryInstance3D.VisibilityRangeFadeModeEnum.Self,
            });
        }
        return node;
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
            _horizon.Position = new Vector3((float)NearestCopy(_horizonX, 0, Focus.X, w), 0, (float)NearestCopy(_horizonZ, 0, Focus.Z, h));
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
            Rgb c = pal.SoilAt(Mathx.WrapX(x, map.WidthMeters), Mathx.Wrap(z, map.HeightMeters), out _);
            float bare = Math.Clamp((0.85f - normal.Y) * 4f, 0f, 1f);
            c = Rgb.Lerp(c, pal.Rock, bare);
            if (hgt < 0f) c = Rgb.Lerp(c, pal.Seabed, Math.Clamp(-hgt / 5f, 0f, 1f));
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
        arrays[(int)Mesh.ArrayType.Index] = indices;
        return arrays;
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

    /// <summary>The loaded chunk's mesh in frame coordinates, for tests (seams, normals).</summary>
    public ArrayMesh MeshOf(Int3 key, out Vector3 offset)
    {
        offset = Vector3.Zero;
        if (!_chunks.TryGetValue(key, out VChunk c) || c.Mesh == null) return null;
        offset = c.Mesh.Position;
        return c.Mesh.Mesh as ArrayMesh;
    }

    static int Mod(int v, int m) => (v % m + m) % m;
    static double Mod(double v, double m)
    {
        v %= m;
        return v < 0 ? v + m : v;
    }
}
