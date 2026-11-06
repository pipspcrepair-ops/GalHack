using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading.Tasks;
using Godot;
using Planets.Gen;
using Planets.Look;

namespace Planets.Surface;

/// <summary>
/// Streams terrain around the camera without hitches. Chunks are built on worker threads (plain
/// arrays only, no engine calls) and handed to the scene a few per frame within a time budget.
///
/// Rings of detail, by chunk distance from the camera: 2 m grid with full scatter within 2
/// chunks, 4 m with trees within 4, 8 m out to 7 (about 1.1 km). Past that a single far mesh
/// drawn straight from the 32 m world map reaches the horizon; it cuts a hole wherever detailed
/// chunks are already loaded, so the two never fight.
///
/// Chunk coordinates wrap on both axes (the planet is a torus). Every frame each chunk is placed
/// at whichever copy of its x and z lies nearest the camera, which keeps both seams invisible and
/// the camera within one map width and height.
/// </summary>
public partial class ChunkStreamer : Node3D
{
    public const int Lod0Radius = 2, Lod1Radius = 4, Radius = 7;
    const double UploadBudgetMs = 3.0;
    const float FarSpacing = 64f, FarReach = 6400f, FarRebuildDistance = 1500f;

    readonly SurfacePalette _palette;
    readonly double _width, _height;
    readonly int _nx, _nz;
    readonly ShaderMaterial _terrain, _far;
    readonly Material _water, _flora;
    readonly Dictionary<Flora, Mesh> _floraMeshes;
    readonly Mesh _oreMesh;
    readonly Dictionary<(int, int), Chunk> _chunks = new();
    readonly HashSet<(int, int, int)> _building = new();
    readonly ConcurrentQueue<ChunkData> _built = new();
    readonly int _maxJobs = Math.Clamp(System.Environment.ProcessorCount - 2, 1, 6);
    readonly List<(int cx, int cz, int lod, int dist)> _wanted = new();

    MeshInstance3D _farMesh;
    Task<(ChunkData data, double cx, double cz)> _farJob;
    double _farX = double.MaxValue, _farZ = double.MaxValue;
    double _camX, _camZ;

    sealed class Chunk
    {
        public int Cx, Cz, Lod;
        public Node3D Node;
    }

    internal ChunkStreamer(SurfacePalette palette, Material water)
    {
        _palette = palette;
        _width = palette.Map.WidthMeters;
        _height = palette.Map.HeightMeters;
        _nx = (int)Math.Round(_width / ChunkBuilder.Size);
        _nz = (int)Math.Round(palette.Map.HeightMeters / ChunkBuilder.Size);
        _water = water;
        _terrain = TerrainMaterial(Shaders.Terrain);
        _far = TerrainMaterial(Shaders.FarTerrain);
        _flora = new ShaderMaterial { Shader = Shaders.Flora };
        _floraMeshes = FloraMeshes.BuildAll();
        _oreMesh = FloraMeshes.Ore();
    }

    public ChunkStreamer() { } // Godot needs a parameterless constructor

    public int ChunkCount => _chunks.Count;
    public int Building => _building.Count;
    public double LastBuildMs { get; private set; }

    /// <summary>True once every wanted chunk is loaded at its wanted detail and the far mesh is in.</summary>
    public bool Settled { get; private set; }

    ShaderMaterial TerrainMaterial(Shader shader)
    {
        var material = new ShaderMaterial { Shader = shader };
        Rgb glow = _palette.GlowColor;
        material.SetShaderParameter("glow_color", new Color(glow.R, glow.G, glow.B));
        material.SetShaderParameter("map_size", new Vector2((float)_width, (float)_height));
        material.SetShaderParameter("veins", _palette.Map.Def.Archetype == Archetype.Lava ? 1f : 0f);
        return material;
    }

    public override void _Ready()
    {
        _farMesh = new MeshInstance3D { Name = "Far", MaterialOverride = _far, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off };
        AddChild(_farMesh);
    }

    /// <summary>Call once per frame with the camera's map position (x in [0, width)).</summary>
    public void Stream(double camX, double camZ)
    {
        _camX = camX;
        _camZ = camZ;
        int ccx = (int)Math.Floor(camX / ChunkBuilder.Size), ccz = (int)Math.Floor(camZ / ChunkBuilder.Size);

        // What should exist, nearest first.
        _wanted.Clear();
        for (int dz = -Radius; dz <= Radius; dz++)
        for (int dx = -Radius; dx <= Radius; dx++)
        {
            int dist = Math.Max(Math.Abs(dx), Math.Abs(dz));
            int lod = dist <= Lod0Radius ? 0 : dist <= Lod1Radius ? 1 : 2;
            _wanted.Add((Wrap(ccx + dx), WrapZ(ccz + dz), lod, dist));
        }
        _wanted.Sort((a, b) => a.dist.CompareTo(b.dist));

        bool settled = true;
        foreach (var (cx, cz, lod, _) in _wanted)
        {
            bool have = _chunks.TryGetValue((cx, cz), out Chunk chunk) && chunk.Lod == lod;
            if (have) continue;
            settled = false;
            if (_building.Contains((cx, cz, lod)) || _building.Count >= _maxJobs) continue;
            _building.Add((cx, cz, lod));
            SurfacePalette palette = _palette;
            Task.Run(() =>
            {
                try { _built.Enqueue(ChunkBuilder.Build(palette, cx, cz, lod)); }
                catch (Exception e) { GD.PushError($"chunk {cx},{cz} failed: {e}"); _built.Enqueue(new ChunkData { Cx = cx, Cz = cz, Lod = -1 }); }
            });
        }

        Upload(ccx, ccz);
        Unload(ccx, ccz);
        UpdateFar(camX, camZ);
        foreach (Chunk chunk in _chunks.Values)
            chunk.Node.Position = new Vector3((float)DisplayX(chunk.Cx * ChunkBuilder.Size), 0, (float)DisplayZ(chunk.Cz * ChunkBuilder.Size));
        UpdateHole(ccx, ccz);
        Settled = settled && _building.Count == 0 && _built.IsEmpty && _farJob == null && _farMesh.Mesh != null;
    }

    /// <summary>The copy of a map x that lies nearest the camera.</summary>
    public double DisplayX(double mapX) => mapX + _width * Math.Round((_camX - mapX - ChunkBuilder.Size / 2) / _width);

    /// <summary>The copy of a map z that lies nearest the camera.</summary>
    public double DisplayZ(double mapZ) => mapZ + _height * Math.Round((_camZ - mapZ - ChunkBuilder.Size / 2) / _height);

    int Wrap(int cx) => (cx % _nx + _nx) % _nx;
    int WrapZ(int cz) => (cz % _nz + _nz) % _nz;

    void Upload(int ccx, int ccz)
    {
        var watch = Stopwatch.StartNew();
        while (watch.Elapsed.TotalMilliseconds < UploadBudgetMs && _built.TryDequeue(out ChunkData data))
        {
            _building.Remove((data.Cx, data.Cz, data.Lod));
            if (data.Lod < 0) continue;
            int dist = Math.Max(Math.Abs(WrapDelta(data.Cx - ccx)), Math.Abs(WrapDeltaZ(data.Cz - ccz)));
            if (dist > Radius + 1) continue; // flew away while it was building
            LastBuildMs = data.BuildMs;
            if (_chunks.TryGetValue((data.Cx, data.Cz), out Chunk old)) old.Node.QueueFree();
            _chunks[(data.Cx, data.Cz)] = new Chunk { Cx = data.Cx, Cz = data.Cz, Lod = data.Lod, Node = CreateNode(data) };
        }
    }

    int WrapDelta(int d)
    {
        d = Wrap(d);
        return d > _nx / 2 ? d - _nx : d;
    }

    int WrapDeltaZ(int d)
    {
        d = WrapZ(d);
        return d > _nz / 2 ? d - _nz : d;
    }

    void Unload(int ccx, int ccz)
    {
        List<(int, int)> gone = null;
        foreach (var (key, chunk) in _chunks)
        {
            int dist = Math.Max(Math.Abs(WrapDelta(chunk.Cx - ccx)), Math.Abs(WrapDeltaZ(chunk.Cz - ccz)));
            if (dist <= Radius + 1) continue; // one ring of slack, so hovering on a border never thrashes
            chunk.Node.QueueFree();
            (gone ??= new List<(int, int)>()).Add(key);
        }
        if (gone != null) foreach (var key in gone) _chunks.Remove(key);
    }

    Node3D CreateNode(ChunkData data)
    {
        var node = new Node3D { Name = $"Chunk_{data.Cx}_{data.Cz}_{data.Lod}" };
        node.PhysicsInterpolationMode = PhysicsInterpolationModeEnum.Off;
        node.AddChild(new MeshInstance3D
        {
            Mesh = TerrainMesh(data),
            MaterialOverride = _terrain,
            CastShadow = data.Lod <= 1 ? GeometryInstance3D.ShadowCastingSetting.On : GeometryInstance3D.ShadowCastingSetting.Off,
        });

        if (data.WaterVertices != null)
        {
            var water = new Godot.Collections.Array();
            water.Resize((int)Mesh.ArrayType.Max);
            water[(int)Mesh.ArrayType.Vertex] = data.WaterVertices;
            water[(int)Mesh.ArrayType.Index] = data.WaterIndices;
            var waterMesh = new ArrayMesh();
            waterMesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, water);
            node.AddChild(new MeshInstance3D { Mesh = waterMesh, MaterialOverride = _water, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off });
        }

        foreach (var (kind, buffer, count) in data.Flora)
            AddScatter(node, _floraMeshes[kind], buffer, count, data.Lod == 0, data.Lod == 0 ? 560f : 820f);
        if (data.DepositCount > 0) AddScatter(node, _oreMesh, data.Deposits, data.DepositCount, data.Lod == 0, 700f);
        AddChild(node);
        return node;
    }

    /// <summary>One MultiMesh per kind per chunk: a single draw call however many instances.</summary>
    void AddScatter(Node3D node, Mesh mesh, float[] buffer, int count, bool shadows, float range)
    {
        var multimesh = new MultiMesh
        {
            TransformFormat = MultiMesh.TransformFormatEnum.Transform3D,
            UseCustomData = true,
            Mesh = mesh,
        };
        multimesh.InstanceCount = count;
        multimesh.Buffer = buffer;
        node.AddChild(new MultiMeshInstance3D
        {
            Multimesh = multimesh,
            MaterialOverride = _flora,
            CastShadow = shadows ? GeometryInstance3D.ShadowCastingSetting.On : GeometryInstance3D.ShadowCastingSetting.Off,
            VisibilityRangeEnd = range,
            VisibilityRangeEndMargin = 60f,
            VisibilityRangeFadeMode = GeometryInstance3D.VisibilityRangeFadeModeEnum.Self,
        });
    }

    static ArrayMesh TerrainMesh(ChunkData data)
    {
        var arrays = new Godot.Collections.Array();
        arrays.Resize((int)Mesh.ArrayType.Max);
        arrays[(int)Mesh.ArrayType.Vertex] = data.Vertices;
        arrays[(int)Mesh.ArrayType.Normal] = data.Normals;
        arrays[(int)Mesh.ArrayType.Color] = data.Colors;
        arrays[(int)Mesh.ArrayType.TexUV] = data.Uvs;
        arrays[(int)Mesh.ArrayType.Index] = data.Indices;
        var mesh = new ArrayMesh();
        mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays);
        return mesh;
    }

    // ---- Far terrain --------------------------------------------------------------------

    void UpdateFar(double camX, double camZ)
    {
        if (_farJob != null && _farJob.IsCompleted)
        {
            var (data, fx, fz) = _farJob.Result;
            _farJob = null;
            _farMesh.Mesh = TerrainMesh(data);
            _farX = fx;
            _farZ = fz;
        }
        if (_farJob == null && Math.Abs(Mathx.WrapDelta(camX - _farX, _width)) + Math.Abs(Mathx.WrapDelta(camZ - _farZ, _height)) > FarRebuildDistance)
        {
            double fx = Math.Round(camX / FarSpacing) * FarSpacing, fz = Math.Round(camZ / FarSpacing) * FarSpacing;
            SurfacePalette palette = _palette;
            _farJob = Task.Run(() => (BuildFar(palette, fx, fz), fx, fz));
        }
        if (_farMesh.Mesh != null) _farMesh.Position = new Vector3((float)DisplayX(_farX), 0, (float)DisplayZ(_farZ));
    }

    static ChunkData BuildFar(SurfacePalette pal, double cx, double cz)
    {
        WorldMap map = pal.Map;
        int n = (int)(FarReach * 2 / FarSpacing) + 1;
        var verts = new Vector3[n * n];
        var normals = new Vector3[n * n];
        var colors = new Color[n * n];
        var uvs = new Vector2[n * n];
        var height = new float[n * n];
        for (int j = 0; j < n; j++)
        for (int i = 0; i < n; i++)
        {
            double x = cx - FarReach + i * FarSpacing, z = cz - FarReach + j * FarSpacing;
            height[j * n + i] = map.Sample(map.Elevation, Mathx.WrapX(x, map.WidthMeters), Mathx.Wrap(z, map.HeightMeters));
        }
        for (int j = 0; j < n; j++)
        for (int i = 0; i < n; i++)
        {
            int k = j * n + i;
            double x = cx - FarReach + i * FarSpacing, z = cz - FarReach + j * FarSpacing;
            float hl = height[j * n + Math.Max(i - 1, 0)], hr = height[j * n + Math.Min(i + 1, n - 1)];
            float hd = height[Math.Max(j - 1, 0) * n + i], hu = height[Math.Min(j + 1, n - 1) * n + i];
            var normal = new Vector3(hl - hr, 2f * FarSpacing, hd - hu).Normalized();
            float h = height[k];
            // Sit a little low: where both exist, the detailed chunks win. Low ground (deltas,
            // wetlands, coastal plains a metre or two above the sea) sinks less, or the far ring
            // would show it under water.
            float sink = Math.Clamp((h - 0.6f) * 0.5f, 0f, 3f);
            verts[k] = new Vector3((float)(x - cx), h - sink, (float)(z - cz));
            normals[k] = normal;
            uvs[k] = new Vector2((float)Mathx.WrapX(x, map.WidthMeters), (float)Mathx.Wrap(z, map.HeightMeters));
            Rgb c = pal.ColorAt(Mathx.WrapX(x, map.WidthMeters), Mathx.Wrap(z, map.HeightMeters), out float glow);
            c = Rgb.Lerp(c, pal.Cliff, Mathx.Smooth(0.85f, 0.65f, normal.Y) * 0.8f);
            if (h < 0f) c = Rgb.Lerp(c, pal.Seabed, Mathx.Smooth(0f, -5f, h));
            colors[k] = new Color(c.R, c.G, c.B, glow);
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
        return new ChunkData { Vertices = verts, Normals = normals, Colors = colors, Uvs = uvs, Indices = indices };
    }

    /// <summary>Tells the far mesh which square around the camera is fully covered by detailed chunks.</summary>
    void UpdateHole(int ccx, int ccz)
    {
        int full = -1;
        for (int r = 0; r <= Radius; r++)
        {
            bool complete = true;
            for (int dz = -r; dz <= r && complete; dz++)
            for (int dx = -r; dx <= r && complete; dx++)
            {
                if (Math.Max(Math.Abs(dx), Math.Abs(dz)) != r) continue;
                complete = _chunks.ContainsKey((Wrap(ccx + dx), WrapZ(ccz + dz)));
            }
            if (!complete) break;
            full = r;
        }
        Vector4 hole = Vector4.Zero;
        if (full >= 0)
        {
            float size = ChunkBuilder.Size;
            float x0 = (float)DisplayX(ccx * size) - full * size, z0 = (float)DisplayZ(ccz * size) - full * size;
            float span = (2 * full + 1) * size;
            hole = new Vector4(x0 + 1f, z0 + 1f, x0 + span - 1f, z0 + span - 1f);
        }
        _far.SetShaderParameter("hole", hole);
    }
}
