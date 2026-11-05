using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using GalHack.Terrain;
using Godot;
using PlanetSurface.Terrain;
using TerrainDigging.Terrain;
using PhysicsLayers = GalHack.Physics.Layers;
using PlanetWorld = PlanetSurface.World.PlanetWorld;
using TInt3 = TerrainDigging.Terrain.Int3;
using WorldLook = PlanetSurface.World.WorldLook;

namespace CapitalBase.World;

/// <summary>
/// The ground this prototype stands on: a fixed square of planet-surface's diggable voxel chunk
/// columns around the landing site, meshed with planet-surface's own voxel mesher and palette,
/// drawn with its terrain shader (read where it lives) and given triangle colliders everywhere a
/// ship can land. Beyond the square, planet-surface's far-ring heightfields out to the horizon,
/// with a hole where the voxels are.
///
/// It doesn't stream: a capital ship parks, and a base never moves. Edits mark chunks dirty, and
/// dirty chunks are rebuilt within a per-frame budget (or all at once, for tests and lift-off).
/// </summary>
public partial class TerrainPatch : Node3D
{
    const int S = ChunkStore.Size;

    /// <summary>False in headless tests: colliders only, no meshes and no far rings.</summary>
    public bool Visuals = true;

    public PlanetWorld World { get; private set; }
    public int MinCx { get; private set; }
    public int MinCz { get; private set; }
    public int Columns { get; private set; }

    /// <summary>Chunks waiting to be rebuilt after edits.</summary>
    public int Pending => _dirty.Count;

    /// <summary>Main-thread cost of the last <see cref="RebuildPending"/> pass, ms.</summary>
    public double LastPassMs { get; private set; }

    public int ChunkCount => _pieces.Count;
    public int Rebuilt { get; private set; }

    /// <summary>The far rings are built (screenshots wait for them).</summary>
    public bool FarReady => _farTask == null || _farTask.IsCompleted && _farApplied;

    sealed class Piece
    {
        public TInt3 Key;
        public MeshInstance3D Mesh;
        public StaticBody3D Body;
        public CollisionShape3D Shape;
    }

    readonly Dictionary<TInt3, Piece> _pieces = new();
    readonly SortedSet<TInt3> _dirty = new();
    ShaderMaterial _near, _far;
    StandardMaterial3D _water;
    Task<List<FarJob>> _farTask;
    bool _farApplied;

    /// <summary>Planet x and z of the patch's corners, m.</summary>
    public Rect2 Area => new(MinCx * S, MinCz * S, Columns * S, Columns * S);

    public bool ContainsColumn(int cx, int cz) => cx >= MinCx && cz >= MinCz && cx < MinCx + Columns && cz < MinCz + Columns;

    /// <summary>Meshes every chunk of the square (on worker threads), then puts their nodes in.</summary>
    public void Build(PlanetWorld world, int centerCx, int centerCz, int columns)
    {
        World = world;
        Columns = columns;
        MinCx = centerCx - columns / 2;
        MinCz = centerCz - columns / 2;
        if (Visuals)
        {
            _near = TerrainMaterial(far: false);
            _far = TerrainMaterial(far: true);
            WorldLook look = world.Look;
            _water = new StandardMaterial3D
            {
                AlbedoColor = new Color(look.Shallow.R, look.Shallow.G, look.Shallow.B, 0.82f),
                Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
                Roughness = 0.08f,
                Metallic = 0.1f,
                CullMode = BaseMaterial3D.CullModeEnum.Disabled,
            };
        }

        var keys = new List<TInt3>();
        for (int cz = MinCz; cz < MinCz + columns; cz++)
        for (int cx = MinCx; cx < MinCx + columns; cx++)
        {
            PlanetTerrainSource.Column column = world.Source.ColumnAt(cx, cz);
            int lo = (int)MathF.Floor((column.Min - 4f) / S), hi = (int)MathF.Floor((column.Max + 1f) / S);
            for (int cy = lo; cy <= hi; cy++) keys.Add(new TInt3(cx, cy, cz));
        }
        var jobs = keys.Select(k => Job(k)).ToList();
        Parallel.ForEach(jobs, job => VoxelMeshing.Build(job, world.Source, world.Palette, world.Width, world.Height));
        foreach (VoxelJob job in jobs) Apply(job);

        if (Visuals) _farTask = Task.Run(() => BuildFar(centerCx, centerCz));
    }

    VoxelJob Job(TInt3 key) => new()
    {
        Key = key,
        Hood = World.Terrain.Store.CaptureNeighborhood(key),
        NeedFaces = true,
    };

    /// <summary>Edits changed these chunks (copies across seams included): rebuild the ones in the square.</summary>
    public void MarkDirty(IEnumerable<TInt3> chunks)
    {
        foreach (TInt3 c in chunks)
            if (ContainsColumn(c.X, c.Z)) _dirty.Add(c);
    }

    /// <summary>Rebuilds dirty chunks in a fixed order until the budget is spent. Returns how many.</summary>
    public int RebuildPending(double budgetMs)
    {
        var watch = Stopwatch.StartNew();
        int n = 0;
        while (_dirty.Count > 0 && (n == 0 || watch.Elapsed.TotalMilliseconds < budgetMs))
        {
            TInt3 key = _dirty.Min;
            _dirty.Remove(key);
            VoxelJob job = Job(key);
            VoxelMeshing.Build(job, World.Source, World.Palette, World.Width, World.Height);
            Apply(job);
            n++;
        }
        LastPassMs = watch.Elapsed.TotalMilliseconds;
        Rebuilt += n;
        return n;
    }

    public void RebuildAllNow() => RebuildPending(double.PositiveInfinity);

    void Apply(VoxelJob job)
    {
        if (job.Error != null) throw new InvalidOperationException($"meshing chunk {job.Key} failed", job.Error);
        if (!_pieces.TryGetValue(job.Key, out Piece piece))
        {
            if (job.IsEmpty) return;
            piece = new Piece { Key = job.Key };
            _pieces[job.Key] = piece;
            piece.Body = new StaticBody3D { Name = $"Ground {job.Key.X} {job.Key.Y} {job.Key.Z}", CollisionLayer = PhysicsLayers.World, CollisionMask = 0 };
            piece.Shape = new CollisionShape3D { Shape = new ConcavePolygonShape3D() };
            piece.Body.AddChild(piece.Shape);
            AddChild(piece.Body);
            if (Visuals)
            {
                piece.Mesh = new MeshInstance3D { Name = $"Chunk {job.Key.X} {job.Key.Y} {job.Key.Z}", MaterialOverride = _near };
                AddChild(piece.Mesh);
            }
        }
        ((ConcavePolygonShape3D)piece.Shape.Shape).Data = job.Faces ?? Array.Empty<Vector3>();
        piece.Shape.Disabled = job.IsEmpty;
        if (piece.Mesh == null) return;
        if (job.IsEmpty)
        {
            piece.Mesh.Mesh = null;
            return;
        }
        var arrays = new Godot.Collections.Array();
        arrays.Resize((int)Mesh.ArrayType.Max);
        arrays[(int)Mesh.ArrayType.Vertex] = job.Positions;
        arrays[(int)Mesh.ArrayType.Normal] = job.Normals;
        arrays[(int)Mesh.ArrayType.Color] = job.Colors;
        arrays[(int)Mesh.ArrayType.TexUV] = job.Flags;
        arrays[(int)Mesh.ArrayType.Index] = job.Indices;
        var mesh = new ArrayMesh();
        mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays);
        piece.Mesh.Mesh = mesh;
    }

    // ---- Materials and far rings ----------------------------------------------------------

    ShaderMaterial TerrainMaterial(bool far)
    {
        var m = new ShaderMaterial { Shader = ForeignShader.Load("planet-surface", "terrain.gdshader") };
        m.SetShaderParameter("far_ring", far);
        m.SetShaderParameter("strata", World.Palette.Strata);
        WorldLook look = World.Look;
        m.SetShaderParameter("snow_altitude", look.SnowAltitude);
        m.SetShaderParameter("base_snow", look.BaseSnow);
        m.SetShaderParameter("dust_color", look.Dust);
        m.SetShaderParameter("glow_color", look.Glow);
        return m;
    }

    /// <summary>Heightfield rings from the same source: 4 m spacing near, 8 m out to about 2 km.</summary>
    List<FarJob> BuildFar(int centerCx, int centerCz)
    {
        const int Rings = 8;
        int fx0 = (int)MathF.Floor(centerCx * S / (float)FarBuilder.Size), fz0 = (int)MathF.Floor(centerCz * S / (float)FarBuilder.Size);
        var jobs = new List<FarJob>();
        for (int dz = -Rings; dz <= Rings; dz++)
        for (int dx = -Rings; dx <= Rings; dx++)
        {
            int d = Math.Max(Math.Abs(dx), Math.Abs(dz));
            jobs.Add(new FarJob { Fx = fx0 + dx, Fz = fz0 + dz, Lod = d <= 3 ? 1 : 2 });
        }
        var dug = new HashSet<long>();
        Parallel.ForEach(jobs, job => FarBuilder.Build(job, World.Source, World.Palette, dug));
        return jobs;
    }

    public override void _Process(double delta)
    {
        if (_farTask == null || _farApplied || !_farTask.IsCompleted) return;
        _farApplied = true;
        if (_farTask.IsFaulted)
        {
            GD.PushError($"far rings failed: {_farTask.Exception}");
            return;
        }
        Rect2 area = Area;
        _far.SetShaderParameter("hole", new Vector4(area.Position.X + 0.5f, area.Position.Y + 0.5f, area.End.X - 0.5f, area.End.Y - 0.5f));
        foreach (FarJob job in _farTask.Result)
        {
            if (job.Error != null || job.Vertices == null) continue;
            var node = new Node3D { Name = $"Far {job.Fx} {job.Fz}", Position = new Vector3(job.Fx * FarBuilder.Size, 0, job.Fz * FarBuilder.Size) };
            AddChild(node);
            var arrays = new Godot.Collections.Array();
            arrays.Resize((int)Mesh.ArrayType.Max);
            arrays[(int)Mesh.ArrayType.Vertex] = job.Vertices;
            arrays[(int)Mesh.ArrayType.Normal] = job.Normals;
            arrays[(int)Mesh.ArrayType.Color] = job.Colors;
            arrays[(int)Mesh.ArrayType.TexUV] = job.Flags;
            arrays[(int)Mesh.ArrayType.Index] = job.Indices;
            var mesh = new ArrayMesh();
            mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays);
            node.AddChild(new MeshInstance3D { Mesh = mesh, MaterialOverride = _far });
            if (job.WaterVertices == null) continue;
            int[] wet = InWater(job, area);
            if (wet.Length == 0) continue;
            var water = new Godot.Collections.Array();
            water.Resize((int)Mesh.ArrayType.Max);
            water[(int)Mesh.ArrayType.Vertex] = job.WaterVertices;
            water[(int)Mesh.ArrayType.Color] = job.WaterColors;
            water[(int)Mesh.ArrayType.Index] = wet;
            var waterMesh = new ArrayMesh();
            waterMesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, water);
            node.AddChild(new MeshInstance3D { Mesh = waterMesh, MaterialOverride = _water, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off });
        }
    }

    /// <summary>
    /// A far chunk's river triangles outside the voxel square: inside it the voxels' own ground
    /// stands, and a coarse river surface would show through it as puddles.
    /// </summary>
    static int[] InWater(FarJob job, Rect2 area)
    {
        var kept = new List<int>(job.WaterIndices.Length);
        float x0 = job.Fx * FarBuilder.Size, z0 = job.Fz * FarBuilder.Size;
        for (int t = 0; t + 2 < job.WaterIndices.Length; t += 3)
        {
            bool inside = true;
            for (int k = 0; k < 3; k++)
            {
                Vector3 v = job.WaterVertices[job.WaterIndices[t + k]];
                inside &= area.HasPoint(new Vector2(x0 + v.X, z0 + v.Z));
            }
            if (inside) continue;
            kept.Add(job.WaterIndices[t]);
            kept.Add(job.WaterIndices[t + 1]);
            kept.Add(job.WaterIndices[t + 2]);
        }
        return kept.ToArray();
    }
}
