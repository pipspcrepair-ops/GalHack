using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Godot;
using TerrainDigging.Terrain;
using NVector3 = System.Numerics.Vector3;

namespace TerrainDigging;

/// <summary>
/// Draws the chunk store and keeps its collision in step. Edits mark chunks dirty; the main
/// thread snapshots each dirty chunk's neighbourhood (a few references), a worker builds the
/// mesh, and finished meshes are swapped in under a per-frame time budget. Only chunks an
/// edit touched are ever rebuilt, and nothing heavy runs on the main thread.
/// </summary>
public partial class TerrainView : Node3D
{
    public ChunkStore Store { get; private set; }

    /// <summary>Main-thread time allowed per frame for swapping in finished meshes.</summary>
    public double ApplyBudgetMs = 2.0;

    public readonly Stats RemeshMs = new(), SurfaceNetsMs = new(), ApplyMs = new(), ApplyMeshMs = new(), ApplyShapeMs = new();
    public double LastFrameMs { get; private set; }
    public double WorstFrameMs { get; private set; }
    public int MeshedChunks => _nodes.Count;
    public int Triangles { get; private set; }
    public long BlocksRebuilt, BlockTriangles;

    /// <summary>A chunk's new mesh (or emptiness) has just been swapped in.</summary>
    public event Action<Int3> ChunkApplied;

    /// <summary>A wipe-and-rebuild finished: chunks compared, how many differed, seconds taken.</summary>
    public event Action<int, int, double> RebuildVerified;

    public bool IsIdle => _queued.Count == 0 && _inFlight.Count == 0 && _done.IsEmpty;
    public int Pending => _queued.Count + _inFlight.Count + _done.Count;
    public IReadOnlyDictionary<Int3, ulong> MeshHashes => _hashes;
    public ShaderMaterial Material => _material;

    sealed class ChunkNode
    {
        public MeshInstance3D Mesh;
        // One body per block: changing a shape makes the physics engine rebuild its whole body.
        public Node3D Bodies;
        public readonly CollisionShape3D[] Shapes = new CollisionShape3D[ChunkMesh.Blocks];
        public readonly ulong[] ShapeHashes = new ulong[ChunkMesh.Blocks];
        public int Triangles;
    }

    sealed class MeshJob
    {
        public Int3 Chunk;
        public int Epoch;
        public ChunkNeighborhood Hood;
        public Vector3[] Positions, Normals;
        public Vector3[][] BlockFaces;
        public ulong[] BlockHashes;
        public Color[] Colors;
        public Vector2[] Flags;
        public int[] Indices;
        public ulong Hash;
        public double Milliseconds, MeshMilliseconds;
        public Exception Error;
    }

    readonly Dictionary<Int3, ChunkNode> _nodes = new();
    readonly Dictionary<Int3, ulong> _hashes = new();
    readonly Queue<Int3> _urgent = new(), _background = new();
    readonly HashSet<Int3> _queued = new(), _inFlight = new(), _dirtyAgain = new();
    readonly ConcurrentQueue<MeshJob> _done = new();
    readonly ThreadLocal<SurfaceNetsMesher> _meshers = new(() => new SurfaceNetsMesher());
    // Half the cores: enough to keep up with any digging, and the engine's own threads keep theirs.
    readonly int _maxInFlight = Math.Max(2, System.Environment.ProcessorCount / 2);

    ShaderMaterial _material;
    int _epoch;
    Dictionary<Int3, ulong> _verifyAgainst;
    Stopwatch _verifyTimer;

    public void Initialize(ChunkStore store)
    {
        Store = store;
        _material = Shaders.Make(Shaders.Terrain);
    }

    /// <summary>Queues every chunk in the world, for the first build.</summary>
    public void QueueWorld()
    {
        CellBox w = ChunkStore.WorldChunks;
        // Top layers first: those are the ones a player sees from spawn.
        for (int y = w.Max.Y - 1; y >= w.Min.Y; y--)
            for (int z = w.Min.Z; z < w.Max.Z; z++)
                for (int x = w.Min.X; x < w.Max.X; x++)
                    Enqueue(new Int3(x, y, z), urgent: false);
    }

    public void MarkDirty(IEnumerable<Int3> chunks)
    {
        foreach (Int3 c in chunks)
            if (ChunkStore.IsWorldChunk(c))
                Enqueue(c, urgent: true);
    }

    void Enqueue(Int3 chunk, bool urgent)
    {
        if (_inFlight.Contains(chunk))
        {
            _dirtyAgain.Add(chunk);
            return;
        }
        if (_queued.Add(chunk) || urgent) (urgent ? _urgent : _background).Enqueue(chunk);
    }

    bool TryDequeue(out Int3 chunk)
    {
        while (_urgent.TryDequeue(out chunk))
            if (_queued.Remove(chunk)) return true;
        while (_background.TryDequeue(out chunk))
            if (_queued.Remove(chunk)) return true;
        return false;
    }

    public override void _Process(double delta)
    {
        long start = Stopwatch.GetTimestamp();

        // Swap in finished meshes, within the frame budget (always at least one, so it drains).
        int applied = 0;
        while (_done.TryPeek(out MeshJob _))
        {
            if (applied > 0 && Stopwatch.GetElapsedTime(start).TotalMilliseconds > ApplyBudgetMs) break;
            _done.TryDequeue(out MeshJob job);
            if (job.Epoch != _epoch) continue;
            _inFlight.Remove(job.Chunk);
            long applyStart = Stopwatch.GetTimestamp();
            Apply(job);
            ApplyMs.Add(Stopwatch.GetElapsedTime(applyStart).TotalMilliseconds);
            applied++;
            if (_dirtyAgain.Remove(job.Chunk)) Enqueue(job.Chunk, urgent: true);
        }

        // Start new jobs.
        while (_inFlight.Count < _maxInFlight && TryDequeue(out Int3 chunk))
        {
            var job = new MeshJob { Chunk = chunk, Epoch = _epoch, Hood = Store.CaptureNeighborhood(chunk) };
            _inFlight.Add(chunk);
            Task.Run(() => Build(job));
        }

        if (_verifyAgainst != null && IsIdle) FinishVerify();

        LastFrameMs = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
        WorstFrameMs = Math.Max(WorstFrameMs, LastFrameMs);
    }

    public void ResetFrameStats()
    {
        WorstFrameMs = 0;
        RemeshMs.Reset();
        SurfaceNetsMs.Reset();
        ApplyMs.Reset();
        ApplyMeshMs.Reset();
        ApplyShapeMs.Reset();
    }

    // ---- Worker -----------------------------------------------------------------------

    void Build(MeshJob job)
    {
        try
        {
            long start = Stopwatch.GetTimestamp();
            ChunkMesh mesh = _meshers.Value.Build(job.Hood);
            job.MeshMilliseconds = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
            job.Hash = mesh.ComputeHash();
            if (!mesh.IsEmpty)
            {
                job.Positions = MemoryMarshal.Cast<NVector3, Vector3>(mesh.Positions.AsSpan()).ToArray();
                job.Normals = MemoryMarshal.Cast<NVector3, Vector3>(mesh.Normals.AsSpan()).ToArray();
                job.Indices = mesh.Indices;
                job.Colors = new Color[mesh.Positions.Length];
                job.Flags = new Vector2[mesh.Positions.Length];
                for (int i = 0; i < job.Colors.Length; i++)
                    (job.Colors[i], job.Flags[i]) = Palette.Vertex(mesh.Materials[i]);
                BuildCollision(job, mesh);
            }
            job.Milliseconds = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
        }
        catch (Exception e)
        {
            job.Error = e;
        }
        _done.Enqueue(job);
    }

    /// <summary>
    /// Collision triangles, split into the chunk's eight blocks with a hash each. Rebuilding a
    /// physics mesh is the one expensive main-thread step, so only changed blocks are rebuilt.
    /// </summary>
    static void BuildCollision(MeshJob job, ChunkMesh mesh)
    {
        var counts = new int[ChunkMesh.Blocks];
        foreach (byte b in mesh.TriangleBlocks) counts[b]++;
        job.BlockFaces = new Vector3[ChunkMesh.Blocks][];
        job.BlockHashes = new ulong[ChunkMesh.Blocks];
        for (int b = 0; b < ChunkMesh.Blocks; b++) job.BlockFaces[b] = new Vector3[counts[b] * 3];
        Array.Clear(counts);
        for (int t = 0; t < mesh.TriangleBlocks.Length; t++)
        {
            byte b = mesh.TriangleBlocks[t];
            Vector3[] faces = job.BlockFaces[b];
            int o = counts[b]++ * 3;
            faces[o] = job.Positions[mesh.Indices[t * 3]];
            faces[o + 1] = job.Positions[mesh.Indices[t * 3 + 1]];
            faces[o + 2] = job.Positions[mesh.Indices[t * 3 + 2]];
        }
        for (int b = 0; b < ChunkMesh.Blocks; b++)
        {
            if (job.BlockFaces[b].Length == 0) continue; // empty blocks hash to 0, like a fresh node
            var h = Fnv64.Create();
            h.Add(MemoryMarshal.AsBytes(job.BlockFaces[b].AsSpan()));
            job.BlockHashes[b] = h.Value | 1ul;
        }
    }

    // ---- Main thread ------------------------------------------------------------------

    void Apply(MeshJob job)
    {
        if (job.Error != null)
        {
            GD.PrintErr($"Meshing chunk {job.Chunk} failed: {job.Error}");
            return;
        }
        RemeshMs.Add(job.Milliseconds);
        SurfaceNetsMs.Add(job.MeshMilliseconds);
        _hashes[job.Chunk] = job.Hash;
        _nodes.TryGetValue(job.Chunk, out ChunkNode node);

        if (job.Positions == null)
        {
            if (node != null)
            {
                Triangles -= node.Triangles;
                node.Bodies.QueueFree();
                node.Mesh.QueueFree();
                _nodes.Remove(job.Chunk);
            }
            ChunkApplied?.Invoke(job.Chunk);
            return;
        }

        node ??= CreateNode(job.Chunk);
        long meshStart = Stopwatch.GetTimestamp();
        var arrays = new Godot.Collections.Array();
        arrays.Resize((int)Mesh.ArrayType.Max);
        arrays[(int)Mesh.ArrayType.Vertex] = job.Positions;
        arrays[(int)Mesh.ArrayType.Normal] = job.Normals;
        arrays[(int)Mesh.ArrayType.Color] = job.Colors;
        arrays[(int)Mesh.ArrayType.TexUV] = job.Flags;
        arrays[(int)Mesh.ArrayType.Index] = job.Indices;
        var mesh = new ArrayMesh();
        mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays);
        node.Mesh.Mesh = mesh;
        ApplyMeshMs.Add(Stopwatch.GetElapsedTime(meshStart).TotalMilliseconds);
        long shapeStart = Stopwatch.GetTimestamp();
        for (int b = 0; b < ChunkMesh.Blocks; b++)
        {
            if (node.ShapeHashes[b] == job.BlockHashes[b]) continue;
            node.ShapeHashes[b] = job.BlockHashes[b];
            BlocksRebuilt++;
            BlockTriangles += job.BlockFaces[b].Length / 3;
            CollisionShape3D shape = node.Shapes[b];
            shape.Disabled = job.BlockFaces[b].Length == 0;
            ((ConcavePolygonShape3D)shape.Shape).Data = job.BlockFaces[b];
        }
        ApplyShapeMs.Add(Stopwatch.GetElapsedTime(shapeStart).TotalMilliseconds);

        Triangles += job.Indices.Length / 3 - node.Triangles;
        node.Triangles = job.Indices.Length / 3;
        ChunkApplied?.Invoke(job.Chunk);
    }

    ChunkNode CreateNode(Int3 chunk)
    {
        var node = new ChunkNode
        {
            Mesh = new MeshInstance3D
            {
                Name = $"Chunk {chunk.X} {chunk.Y} {chunk.Z}",
                MaterialOverride = _material,
                PhysicsInterpolationMode = PhysicsInterpolationModeEnum.Off,
            },
            Bodies = new Node3D { Name = $"Collision {chunk.X} {chunk.Y} {chunk.Z}" },
        };
        for (int b = 0; b < ChunkMesh.Blocks; b++)
        {
            node.Shapes[b] = new CollisionShape3D { Shape = new ConcavePolygonShape3D(), Disabled = true };
            var body = new StaticBody3D { CollisionLayer = Layers.Terrain, CollisionMask = 0 };
            body.AddChild(node.Shapes[b]);
            node.Bodies.AddChild(body);
        }
        AddChild(node.Mesh);
        AddChild(node.Bodies);
        _nodes[chunk] = node;
        return node;
    }

    // ---- Proof: wipe everything and rebuild it from seed plus edits ---------------------

    /// <summary>
    /// Frees every mesh and collider, forgets the generated base terrain, and rebuilds the
    /// world from the seed and the edit lists alone. When it's done, every chunk's mesh hash
    /// is compared with the one it had before.
    /// </summary>
    public void WipeAndRebuild()
    {
        _verifyAgainst = new Dictionary<Int3, ulong>(_hashes);
        _verifyTimer = Stopwatch.StartNew();
        _epoch++; // results still being built for the old meshes are thrown away
        foreach (ChunkNode node in _nodes.Values)
        {
            node.Mesh.QueueFree();
            node.Bodies.QueueFree();
        }
        _nodes.Clear();
        _hashes.Clear();
        _inFlight.Clear();
        _dirtyAgain.Clear();
        _queued.Clear();
        _urgent.Clear();
        _background.Clear();
        Triangles = 0;
        Store.Base.Clear();
        QueueWorld();
    }

    public bool IsVerifying => _verifyAgainst != null;

    void FinishVerify()
    {
        int compared = 0, mismatched = 0;
        foreach ((Int3 chunk, ulong before) in _verifyAgainst)
        {
            compared++;
            if (!_hashes.TryGetValue(chunk, out ulong after) || after != before) mismatched++;
        }
        foreach (Int3 chunk in _hashes.Keys)
            if (!_verifyAgainst.ContainsKey(chunk))
            {
                compared++;
                mismatched++;
            }
        double seconds = _verifyTimer.Elapsed.TotalSeconds;
        _verifyAgainst = null;
        RebuildVerified?.Invoke(compared, mismatched, seconds);
    }

    public Aabb ChunkBounds(Int3 chunk) =>
        new(new Vector3(chunk.X, chunk.Y, chunk.Z) * ChunkStore.Size, Vector3.One * ChunkStore.Size);
}

/// <summary>Running average and maximum of a timing, and how often it went over 5 ms.</summary>
public sealed class Stats
{
    public int Count { get; private set; }
    public int Over5 { get; private set; }
    public double Total { get; private set; }
    public double Max { get; private set; }
    public double Average => Count == 0 ? 0 : Total / Count;

    public void Add(double value)
    {
        Count++;
        Total += value;
        Max = Math.Max(Max, value);
        if (value > 5.0) Over5++;
    }

    public void Reset()
    {
        Count = 0;
        Over5 = 0;
        Total = 0;
        Max = 0;
    }
}
