using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using GalHack.Physics;
using Godot;
using TerrainDigging.Terrain;
using NVector3 = System.Numerics.Vector3;

namespace PhysicsSandbox.World;

/// <summary>
/// Draws terrain-digging's chunk store with its Surface Nets mesher and keeps its collision in
/// step: eight static bodies per chunk (one per 8³ block), so a crater only makes the engine
/// rebuild the blocks it touched, as in terrain-digging. The first build meshes on all cores;
/// edits rebuild their chunks at once, on the main thread, so the collision a crash falls into
/// is there the same tick the crater is cut.
/// </summary>
public partial class TerrainView : Node3D
{
    public ChunkStore Store { get; private set; }

    /// <summary>False in headless tests: collision only, no meshes to draw.</summary>
    public bool Visuals = true;

    public int Triangles { get; private set; }
    public int ChunksRebuilt { get; private set; }
    public double LastRebuildMs { get; private set; }

    sealed class ChunkNode
    {
        public MeshInstance3D Mesh;
        public Node3D Bodies;
        public readonly CollisionShape3D[] Shapes = new CollisionShape3D[ChunkMesh.Blocks];
        public readonly ulong[] Hashes = new ulong[ChunkMesh.Blocks];
        public int Triangles;
    }

    readonly Dictionary<Int3, ChunkNode> _nodes = new();
    readonly ThreadLocal<SurfaceNetsMesher> _meshers = new(() => new SurfaceNetsMesher());
    ShaderMaterial _material;

    public void Initialize(ChunkStore store)
    {
        Store = store;
        _material = new ShaderMaterial { Shader = new Shader { Code = TerrainShader } };
    }

    /// <summary>Meshes the whole world once, in parallel, then swaps everything in.</summary>
    public void BuildAll()
    {
        CellBox w = ChunkStore.WorldChunks;
        var chunks = new List<Int3>();
        for (int y = w.Min.Y; y < w.Max.Y; y++)
            for (int z = w.Min.Z; z < w.Max.Z; z++)
                for (int x = w.Min.X; x < w.Max.X; x++)
                    chunks.Add(new Int3(x, y, z));
        var hoods = chunks.Select(c => Store.CaptureNeighborhood(c)).ToArray();
        var meshes = new ChunkMesh[hoods.Length];
        Parallel.For(0, hoods.Length, i => meshes[i] = _meshers.Value.Build(hoods[i]));
        for (int i = 0; i < meshes.Length; i++) Apply(chunks[i], meshes[i]);
    }

    /// <summary>Rebuilds the chunks an edit dirtied, right now.</summary>
    public void Rebuild(IEnumerable<Int3> chunks)
    {
        var watch = Stopwatch.StartNew();
        int n = 0;
        foreach (Int3 c in chunks)
        {
            if (!ChunkStore.IsWorldChunk(c)) continue;
            Apply(c, _meshers.Value.Build(Store.CaptureNeighborhood(c)));
            n++;
        }
        ChunksRebuilt += n;
        LastRebuildMs = watch.Elapsed.TotalMilliseconds;
    }

    void Apply(Int3 chunk, ChunkMesh mesh)
    {
        _nodes.TryGetValue(chunk, out ChunkNode node);
        if (mesh.IsEmpty)
        {
            if (node == null) return;
            for (int b = 0; b < ChunkMesh.Blocks; b++)
            {
                node.Shapes[b].Disabled = true;
                node.Hashes[b] = 0;
            }
            if (node.Mesh != null) node.Mesh.Mesh = null;
            Triangles -= node.Triangles;
            node.Triangles = 0;
            return;
        }

        node ??= CreateNode(chunk);
        Vector3[] positions = MemoryMarshal.Cast<NVector3, Vector3>(mesh.Positions.AsSpan()).ToArray();
        if (Visuals)
        {
            var arrays = new Godot.Collections.Array();
            arrays.Resize((int)Mesh.ArrayType.Max);
            var colors = new Color[positions.Length];
            var flags = new Vector2[positions.Length];
            for (int i = 0; i < colors.Length; i++)
            {
                CellMaterial m = mesh.Materials[i];
                colors[i] = Palette.Of(m).SrgbToLinear();
                flags[i] = new Vector2(m == CellMaterial.Ore ? 1f : 0f, m == CellMaterial.Bedrock ? 1f : 0f);
            }
            arrays[(int)Mesh.ArrayType.Vertex] = positions;
            arrays[(int)Mesh.ArrayType.Normal] = MemoryMarshal.Cast<NVector3, Vector3>(mesh.Normals.AsSpan()).ToArray();
            arrays[(int)Mesh.ArrayType.Color] = colors;
            arrays[(int)Mesh.ArrayType.TexUV] = flags;
            arrays[(int)Mesh.ArrayType.Index] = mesh.Indices;
            var am = new ArrayMesh();
            am.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays);
            node.Mesh.Mesh = am;
        }

        // Collision, split into the chunk's eight blocks; only changed blocks are rebuilt.
        var counts = new int[ChunkMesh.Blocks];
        foreach (byte b in mesh.TriangleBlocks) counts[b]++;
        var faces = new Vector3[ChunkMesh.Blocks][];
        for (int b = 0; b < ChunkMesh.Blocks; b++) faces[b] = new Vector3[counts[b] * 3];
        System.Array.Clear(counts);
        for (int t = 0; t < mesh.TriangleBlocks.Length; t++)
        {
            byte b = mesh.TriangleBlocks[t];
            int o = counts[b]++ * 3;
            faces[b][o] = positions[mesh.Indices[t * 3]];
            faces[b][o + 1] = positions[mesh.Indices[t * 3 + 1]];
            faces[b][o + 2] = positions[mesh.Indices[t * 3 + 2]];
        }
        for (int b = 0; b < ChunkMesh.Blocks; b++)
        {
            ulong hash = faces[b].Length == 0 ? 0 : Hash(faces[b]);
            if (node.Hashes[b] == hash) continue;
            node.Hashes[b] = hash;
            node.Shapes[b].Disabled = faces[b].Length == 0;
            ((ConcavePolygonShape3D)node.Shapes[b].Shape).Data = faces[b];
        }

        Triangles += mesh.TriangleCount - node.Triangles;
        node.Triangles = mesh.TriangleCount;
    }

    static ulong Hash(Vector3[] faces)
    {
        ulong h = 14695981039346656037ul;
        foreach (byte b in MemoryMarshal.AsBytes(faces.AsSpan()))
        {
            h ^= b;
            h *= 1099511628211ul;
        }
        return h | 1ul;
    }

    ChunkNode CreateNode(Int3 chunk)
    {
        var node = new ChunkNode { Bodies = new Node3D { Name = $"Collision {chunk.X} {chunk.Y} {chunk.Z}" } };
        for (int b = 0; b < ChunkMesh.Blocks; b++)
        {
            node.Shapes[b] = new CollisionShape3D { Shape = new ConcavePolygonShape3D(), Disabled = true };
            var body = new StaticBody3D { CollisionLayer = Layers.World, CollisionMask = 0 };
            body.AddChild(node.Shapes[b]);
            node.Bodies.AddChild(body);
        }
        AddChild(node.Bodies);
        if (Visuals)
        {
            node.Mesh = new MeshInstance3D
            {
                Name = $"Chunk {chunk.X} {chunk.Y} {chunk.Z}",
                MaterialOverride = _material,
                PhysicsInterpolationMode = PhysicsInterpolationModeEnum.Off,
            };
            AddChild(node.Mesh);
        }
        _nodes[chunk] = node;
        return node;
    }

    /// <summary>
    /// Natural ground: dusty soil on the flats, weathered rock strata showing on steep banks
    /// and in dug walls, darker the deeper the cut. A little noise breaks up the flat colours so
    /// it reads as rock and dirt, not plastic.
    /// </summary>
    const string TerrainShader = @"
shader_type spatial;
render_mode diffuse_burley, specular_disabled;

varying vec3 world_pos;
varying vec3 world_normal;

float hash(vec3 p) {
    p = fract(p * 0.3183099 + 0.1);
    p *= 17.0;
    return fract(p.x * p.y * p.z * (p.x + p.y + p.z));
}

float noise(vec3 x) {
    vec3 i = floor(x);
    vec3 f = fract(x);
    f = f * f * (3.0 - 2.0 * f);
    return mix(mix(mix(hash(i), hash(i + vec3(1, 0, 0)), f.x), mix(hash(i + vec3(0, 1, 0)), hash(i + vec3(1, 1, 0)), f.x), f.y),
               mix(mix(hash(i + vec3(0, 0, 1)), hash(i + vec3(1, 0, 1)), f.x), mix(hash(i + vec3(0, 1, 1)), hash(i + vec3(1, 1, 1)), f.x), f.y), f.z);
}

void vertex() {
    world_pos = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xyz;
    world_normal = normalize((MODEL_MATRIX * vec4(NORMAL, 0.0)).xyz);
}

void fragment() {
    vec3 c = COLOR.rgb;
    float steep = 1.0 - smoothstep(0.5, 0.82, world_normal.y);
    float strata = 0.5 + 0.5 * sin(world_pos.y * 2.1 + noise(world_pos * 0.35) * 3.0);
    c *= mix(1.0, 0.82 + 0.18 * strata, steep);
    float grain = noise(world_pos * 1.7) * 0.6 + noise(world_pos * 6.0) * 0.4;
    c *= 0.88 + 0.2 * grain;
    vec3 g = abs(fract(world_pos) - 0.5);
    c = mix(c, c * 1.25, UV.y * step(0.47, max(max(g.x, g.y), g.z)));
    ALBEDO = c;
    ROUGHNESS = 0.96;
}
";
}
