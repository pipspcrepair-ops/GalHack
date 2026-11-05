using System;
using System.Numerics;
using System.Runtime.InteropServices;

namespace TerrainDigging.Terrain;

/// <summary>
/// Everything a mesh worker needs to build one chunk: the immutable base chunks and removal
/// masks of the chunk and its 26 neighbours, and the structure volumes near it, captured on the
/// main thread in microseconds. Base chunks that don't exist yet are generated on the worker
/// when it first reads them.
/// </summary>
public sealed class ChunkNeighborhood
{
    public readonly Int3 Chunk;
    readonly BaseCache _base;
    readonly ulong[][] _masks;

    /// <summary>Structure volumes within <see cref="ChunkStore.VolumeReach"/> cells of the chunk, in key order. Usually none.</summary>
    public readonly StructureVolume[] Volumes;

    public ChunkNeighborhood(Int3 chunk, BaseCache baseCache, ulong[][] masks) : this(chunk, baseCache, masks, null) { }

    public ChunkNeighborhood(Int3 chunk, BaseCache baseCache, ulong[][] masks, StructureVolume[] volumes)
    {
        Chunk = chunk;
        _base = baseCache;
        _masks = masks;
        Volumes = volumes ?? Array.Empty<StructureVolume>();
    }

    public static int NeighborIndex(int dx, int dy, int dz) => (dx + 1) + 3 * ((dz + 1) + 3 * (dy + 1));

    /// <summary>Whether a cell within two cells of the chunk has been removed.</summary>
    bool RemovedAt(int x, int y, int z)
    {
        int dx = (x >> ChunkStore.Shift) - Chunk.X, dy = (y >> ChunkStore.Shift) - Chunk.Y, dz = (z >> ChunkStore.Shift) - Chunk.Z;
        ulong[] mask = _masks[NeighborIndex(dx, dy, dz)];
        if (mask == null) return false;
        int i = ChunkStore.LocalIndex(new Int3(x, y, z));
        return (mask[i >> 6] & (1ul << (i & 63))) != 0;
    }

    // Cover flags for the padded grid plus a one-cell ring round it (each padding cell's neighbours).
    const int Ring = SurfaceNetsMesher.Samples + 2;
    [ThreadStatic] static bool[] _covered;

    /// <summary>
    /// Rule 4, the drawing half: a cell a standing structure cleared (removed, centre inside one
    /// of its volumes) is drawn as the ground it was, and the mesher then cuts the surface off at
    /// the structure's faces. So the ground outside meets each face exactly where it always was,
    /// with no sag or moat from the cleared cells. The exception is a cleared cell beside dug
    /// ground outside the structure: there the hole is open to the face and drawn as the air it is.
    /// The cells themselves stay removed; only what is drawn changes.
    /// </summary>
    void ShowCleared(float[] density, CellMaterial[] material)
    {
        const int N = SurfaceNetsMesher.Samples;
        bool[] covered = _covered ??= new bool[Ring * Ring * Ring];
        Array.Clear(covered);
        int ox = Chunk.X * ChunkStore.Size - 2, oy = Chunk.Y * ChunkStore.Size - 2, oz = Chunk.Z * ChunkStore.Size - 2;
        bool any = false;
        foreach (StructureVolume v in Volumes)
        {
            CellBox b = v.CellBounds;
            int x0 = Math.Max(b.Min.X, ox), x1 = Math.Min(b.Max.X, ox + Ring);
            int y0 = Math.Max(b.Min.Y, oy), y1 = Math.Min(b.Max.Y, oy + Ring);
            int z0 = Math.Max(b.Min.Z, oz), z1 = Math.Min(b.Max.Z, oz + Ring);
            for (int y = y0; y < y1; y++)
                for (int z = z0; z < z1; z++)
                    for (int x = x0; x < x1; x++)
                    {
                        int r = (x - ox) + Ring * ((z - oz) + Ring * (y - oy));
                        if (!covered[r] && v.ContainsCell(new Int3(x, y, z)))
                        {
                            covered[r] = true;
                            any = true;
                        }
                    }
        }
        if (!any) return;

        for (int ay = 0; ay < N; ay++)
            for (int az = 0; az < N; az++)
                for (int ax = 0; ax < N; ax++)
                {
                    int r = (ax + 1) + Ring * ((az + 1) + Ring * (ay + 1));
                    if (!covered[r]) continue;
                    int x = ox + ax + 1, y = oy + ay + 1, z = oz + az + 1;
                    if (!RemovedAt(x, y, z)) continue;
                    if ((!covered[r - 1] && RemovedAt(x - 1, y, z)) || (!covered[r + 1] && RemovedAt(x + 1, y, z)) ||
                        (!covered[r - Ring] && RemovedAt(x, y, z - 1)) || (!covered[r + Ring] && RemovedAt(x, y, z + 1)) ||
                        (!covered[r - Ring * Ring] && RemovedAt(x, y - 1, z)) || (!covered[r + Ring * Ring] && RemovedAt(x, y + 1, z)))
                        continue;
                    var cell = new Int3(x, y, z);
                    BaseChunk b = _base.Get(ChunkStore.ChunkOf(cell));
                    int li = ChunkStore.LocalIndex(cell);
                    int ai = ax + N * (az + N * ay);
                    density[ai] = b.DensityAt(li);
                    material[ai] = b.MaterialAt(li);
                }
    }

    /// <summary>
    /// Fills the padded sample grid: the chunk's 16³ cells plus a one-cell border taken from
    /// its neighbours, so both sides of a seam mesh from identical numbers.
    /// </summary>
    public void Fill(float[] density, CellMaterial[] material)
    {
        const int S = ChunkStore.Size, N = SurfaceNetsMesher.Samples;
        for (int ry = 0; ry < 3; ry++)
        {
            int y0 = ry == 0 ? 0 : ry == 1 ? 1 : N - 1, y1 = ry == 0 ? 1 : ry == 1 ? N - 1 : N;
            for (int rz = 0; rz < 3; rz++)
            {
                int z0 = rz == 0 ? 0 : rz == 1 ? 1 : N - 1, z1 = rz == 0 ? 1 : rz == 1 ? N - 1 : N;
                for (int rx = 0; rx < 3; rx++)
                {
                    int x0 = rx == 0 ? 0 : rx == 1 ? 1 : N - 1, x1 = rx == 0 ? 1 : rx == 1 ? N - 1 : N;
                    BaseChunk b = _base.Get(new Int3(Chunk.X + rx - 1, Chunk.Y + ry - 1, Chunk.Z + rz - 1));
                    ulong[] mask = _masks[rx + 3 * (rz + 3 * ry)];

                    for (int ay = y0; ay < y1; ay++)
                    {
                        // Sample -1 is the neighbour's cell 15 and sample 16 its cell 0: & 15 covers both.
                        int ly = (ay - 1) & ChunkStore.LocalMask;
                        for (int az = z0; az < z1; az++)
                        {
                            int lz = (az - 1) & ChunkStore.LocalMask;
                            int row = N * (az + N * ay);
                            int baseRow = S * (lz + S * ly);
                            for (int ax = x0; ax < x1; ax++)
                            {
                                int li = ((ax - 1) & ChunkStore.LocalMask) + baseRow;
                                int ai = ax + row;
                                if (mask != null && (mask[li >> 6] & (1ul << (li & 63))) != 0)
                                {
                                    density[ai] = -ChunkStore.DensityLimit;
                                    material[ai] = CellMaterial.Air;
                                }
                                else
                                {
                                    density[ai] = b.DensityAt(li);
                                    material[ai] = b.MaterialAt(li);
                                }
                            }
                        }
                    }
                }
            }
        }
        if (Volumes.Length > 0) ShowCleared(density, material);
    }
}

/// <summary>One chunk's triangles in world space. Positions are absolute so seams match bit for bit.</summary>
public sealed class ChunkMesh
{
    public readonly Int3 Chunk;
    public readonly Vector3[] Positions;
    public readonly Vector3[] Normals;
    public readonly CellMaterial[] Materials;
    public readonly int[] Indices;

    /// <summary>
    /// Which of the chunk's eight 8³ blocks owns each triangle. Collision is split the same way,
    /// so a dig only rebuilds the collision blocks it actually changed.
    /// </summary>
    public readonly byte[] TriangleBlocks;

    /// <summary>The world cube (min sample cell) each vertex belongs to, when recorded. For seam tests.</summary>
    public readonly Int3[] Cubes;

    public const int Blocks = 8, BlockSize = ChunkStore.Size / 2;

    public ChunkMesh(Int3 chunk, Vector3[] positions, Vector3[] normals, CellMaterial[] materials, int[] indices, byte[] triangleBlocks, Int3[] cubes)
    {
        Chunk = chunk;
        Positions = positions;
        Normals = normals;
        Materials = materials;
        Indices = indices;
        TriangleBlocks = triangleBlocks;
        Cubes = cubes;
    }

    public bool IsEmpty => Indices.Length == 0;
    public int TriangleCount => Indices.Length / 3;

    public ulong ComputeHash()
    {
        var h = Fnv64.Create();
        h.Add(Chunk);
        h.Add(MemoryMarshal.AsBytes(Positions.AsSpan()));
        h.Add(MemoryMarshal.AsBytes(Normals.AsSpan()));
        h.Add(MemoryMarshal.AsBytes(Materials.AsSpan()));
        h.Add(MemoryMarshal.AsBytes(Indices.AsSpan()));
        return h.Value;
    }
}

/// <summary>
/// Naive Surface Nets over cell-centre density samples. Chosen over Marching Cubes because it
/// has no ambiguous cases or lookup tables, never produces slivers or cracks, and turns a
/// 1 m dug cell into a softly chamfered hole that blends with the natural slopes around it.
///
/// One vertex sits in each "dual cube" (eight neighbouring cell centres) that the surface
/// passes through, at the average of its edge crossings. Each sign change between two cell
/// centres becomes a quad joining the four cubes around that edge. A chunk owns the edges
/// that start at its own cells, so every quad is built exactly once, and the shared border
/// vertices are computed from the same samples in the same order on both sides of a seam.
///
/// Where structures stand in the ground, the chunk is then cut at their faces
/// (<see cref="StructureClipper"/>), so the ground meets them flush.
///
/// One instance per thread: it reuses its scratch buffers.
/// </summary>
public sealed class SurfaceNetsMesher
{
    const int S = ChunkStore.Size;

    /// <summary>Samples per axis: the chunk's cells plus one padding cell on each side.</summary>
    public const int Samples = S + 2;

    const int N = Samples, NN = N * N;
    const int Cubes = S + 1;

    // Corner c of a cube is at (c & 1, (c >> 1) & 1, (c >> 2) & 1).
    static readonly int[] CornerOffset = { 0, 1, NN, 1 + NN, N, 1 + N, N + NN, 1 + N + NN };
    static readonly int[] EdgeA = { 0, 2, 4, 6, 0, 1, 4, 5, 0, 1, 2, 3 };
    static readonly int[] EdgeB = { 1, 3, 5, 7, 2, 3, 6, 7, 4, 5, 6, 7 };

    readonly float[] _density = new float[N * N * N];
    readonly CellMaterial[] _material = new CellMaterial[N * N * N];
    readonly int[] _cubeVertex = new int[Cubes * Cubes * Cubes];
    readonly float[] _corner = new float[8];

    readonly Vector3[] _positions = new Vector3[Cubes * Cubes * Cubes];
    readonly Vector3[] _normals = new Vector3[Cubes * Cubes * Cubes];
    readonly CellMaterial[] _materials = new CellMaterial[Cubes * Cubes * Cubes];
    readonly Int3[] _cubes = new Int3[Cubes * Cubes * Cubes];
    readonly int[] _indices = new int[S * S * S * 3 * 6];
    readonly byte[] _blocks = new byte[S * S * S * 3 * 2];
    int _vertexCount, _indexCount, _block;
    Int3 _origin;

    readonly StructureClipper _clipper = new();

    /// <summary>
    /// Meshes one chunk. Where structures stand (rule 4), the ground they cleared is drawn as it
    /// was and the surface is cut off exactly at their faces, so it meets them flush.
    /// </summary>
    public ChunkMesh Build(ChunkNeighborhood hood, bool recordCubes = false)
    {
        hood.Fill(_density, _material);
        ChunkMesh mesh = Mesh(hood.Chunk, recordCubes);
        if (hood.Volumes.Length > 0 && !mesh.IsEmpty) mesh = _clipper.Clip(mesh, hood.Volumes);
        return mesh;
    }

    ChunkMesh Mesh(Int3 chunk, bool recordCubes)
    {
        _vertexCount = 0;
        _indexCount = 0;
        _origin = chunk * S;

        bool anySolid = false, anyAir = false;
        foreach (float d in _density)
        {
            if (d > 0f) anySolid = true;
            else anyAir = true;
            if (anySolid && anyAir) break;
        }
        if (!(anySolid && anyAir))
            return new ChunkMesh(chunk, Array.Empty<Vector3>(), Array.Empty<Vector3>(), Array.Empty<CellMaterial>(), Array.Empty<int>(),
                Array.Empty<byte>(), recordCubes ? Array.Empty<Int3>() : null);

        Array.Fill(_cubeVertex, -1);

        // Owned sample edges start at the chunk's own cells (array coordinates 1..S).
        for (int ay = 1; ay <= S; ay++)
            for (int az = 1; az <= S; az++)
                for (int ax = 1; ax <= S; ax++)
                {
                    int i = ax + N * (az + N * ay);
                    bool s0 = _density[i] > 0f;
                    _block = ((ax - 1) >> 3) | (((ay - 1) >> 3) << 1) | (((az - 1) >> 3) << 2);

                    if (s0 != _density[i + 1] > 0f)
                        Quad(s0, Cube(ax, ay - 1, az - 1), Cube(ax, ay, az - 1), Cube(ax, ay, az), Cube(ax, ay - 1, az));
                    if (s0 != _density[i + NN] > 0f)
                        Quad(s0, Cube(ax - 1, ay, az - 1), Cube(ax - 1, ay, az), Cube(ax, ay, az), Cube(ax, ay, az - 1));
                    if (s0 != _density[i + N] > 0f)
                        Quad(s0, Cube(ax - 1, ay - 1, az), Cube(ax, ay - 1, az), Cube(ax, ay, az), Cube(ax - 1, ay, az));
                }

        return new ChunkMesh(chunk,
            _positions.AsSpan(0, _vertexCount).ToArray(),
            _normals.AsSpan(0, _vertexCount).ToArray(),
            _materials.AsSpan(0, _vertexCount).ToArray(),
            _indices.AsSpan(0, _indexCount).ToArray(),
            _blocks.AsSpan(0, _indexCount / 3).ToArray(),
            recordCubes ? _cubes.AsSpan(0, _vertexCount).ToArray() : null);
    }

    static int Cube(int cx, int cy, int cz) => cx + Cubes * (cz + Cubes * cy);

    /// <summary>
    /// Emits the quad around one sign-changing edge. The corners go round the edge
    /// anticlockwise seen from its positive end; Godot treats clockwise as front-facing, so the
    /// order is flipped when the surface faces that way (solid below, air above).
    /// </summary>
    void Quad(bool solidBelow, int c00, int c10, int c11, int c01)
    {
        int v00 = Vertex(c00), v10 = Vertex(c10), v11 = Vertex(c11), v01 = Vertex(c01);
        if (solidBelow) (v10, v01) = (v01, v10);

        // Split along the shorter diagonal: fewer slivers, smoother shading.
        if (Vector3.DistanceSquared(_positions[v00], _positions[v11]) <= Vector3.DistanceSquared(_positions[v10], _positions[v01]))
            Emit(v00, v10, v11, v00, v11, v01);
        else
            Emit(v00, v10, v01, v10, v11, v01);
    }

    void Emit(int a, int b, int c, int d, int e, int f)
    {
        _blocks[_indexCount / 3] = (byte)_block;
        _blocks[_indexCount / 3 + 1] = (byte)_block;
        _indices[_indexCount++] = a;
        _indices[_indexCount++] = b;
        _indices[_indexCount++] = c;
        _indices[_indexCount++] = d;
        _indices[_indexCount++] = e;
        _indices[_indexCount++] = f;
    }

    /// <summary>The vertex of one dual cube, built the first time a quad needs it.</summary>
    int Vertex(int cube)
    {
        int existing = _cubeVertex[cube];
        if (existing >= 0) return existing;

        int cx = cube % Cubes, cz = cube / Cubes % Cubes, cy = cube / (Cubes * Cubes);
        int baseIndex = cx + N * (cz + N * cy);
        float[] d = _corner;
        int mask = 0;
        for (int c = 0; c < 8; c++)
        {
            d[c] = _density[baseIndex + CornerOffset[c]];
            if (d[c] > 0f) mask |= 1 << c;
        }

        // Average the edge crossings: where density hits zero along each sign-changing edge.
        float sx = 0f, sy = 0f, sz = 0f;
        int crossings = 0;
        for (int e = 0; e < 12; e++)
        {
            int a = EdgeA[e], b = EdgeB[e];
            if ((((mask >> a) ^ (mask >> b)) & 1) == 0) continue;
            float t = d[a] / (d[a] - d[b]);
            float ax = a & 1, ay = (a >> 1) & 1, az = (a >> 2) & 1;
            float bx = b & 1, by = (b >> 1) & 1, bz = (b >> 2) & 1;
            sx += ax + (bx - ax) * t;
            sy += ay + (by - ay) * t;
            sz += az + (bz - az) * t;
            crossings++;
        }
        float inv = 1f / crossings;
        float u = sx * inv, v = sy * inv, w = sz * inv;

        // Absolute world coordinates: (integer cube corner + 0.5) + offset is computed the same
        // way by both chunks that share this cube, so the floats come out identical.
        int wx = _origin.X + cx - 1, wy = _origin.Y + cy - 1, wz = _origin.Z + cz - 1;
        var position = new Vector3(wx + 0.5f + u, wy + 0.5f + v, wz + 0.5f + w);

        // The normal is the gradient of the trilinear density at the vertex itself, not the
        // cube's average slope: that one jumps from cube to cube and shades as contour stripes.
        // Density rises into the ground, so the outward normal is minus the gradient.
        float u1 = 1f - u, v1 = 1f - v, w1 = 1f - w;
        float gx = v1 * w1 * (d[1] - d[0]) + v * w1 * (d[3] - d[2]) + v1 * w * (d[5] - d[4]) + v * w * (d[7] - d[6]);
        float gy = u1 * w1 * (d[2] - d[0]) + u * w1 * (d[3] - d[1]) + u1 * w * (d[6] - d[4]) + u * w * (d[7] - d[5]);
        float gz = u1 * v1 * (d[4] - d[0]) + u * v1 * (d[5] - d[1]) + u1 * v * (d[6] - d[2]) + u * v * (d[7] - d[3]);
        float length = MathF.Sqrt(gx * gx + gy * gy + gz * gz);
        Vector3 normal = length > 1e-6f ? new Vector3(-gx / length, -gy / length, -gz / length) : Vector3.UnitY;

        int index = _vertexCount++;
        _positions[index] = position;
        _normals[index] = normal;
        _materials[index] = PickMaterial(baseIndex, d);
        _cubes[index] = new Int3(wx, wy, wz);
        _cubeVertex[cube] = index;
        return index;
    }

    /// <summary>
    /// The colour a vertex shows: the material of the solid corner nearest the surface (the
    /// least dense one), since that's what's actually exposed. A deeper corner would paint the
    /// soil with the rock under it, in stripes along every contour. Ore, then bedrock, win when
    /// they are about as close to the surface, so veins in a pit wall read clearly.
    /// </summary>
    CellMaterial PickMaterial(int baseIndex, float[] d)
    {
        const float Exposed = 0.75f;
        CellMaterial nearest = CellMaterial.Air;
        float nearestDensity = float.MaxValue;
        for (int c = 0; c < 8; c++)
        {
            if (d[c] > 0f && d[c] < nearestDensity)
            {
                nearestDensity = d[c];
                nearest = _material[baseIndex + CornerOffset[c]];
            }
        }

        bool bedrock = false;
        for (int c = 0; c < 8; c++)
        {
            if (d[c] <= 0f || d[c] > nearestDensity + Exposed) continue;
            CellMaterial m = _material[baseIndex + CornerOffset[c]];
            if (m == CellMaterial.Ore) return CellMaterial.Ore;
            if (m == CellMaterial.Bedrock) bedrock = true;
        }
        return bedrock ? CellMaterial.Bedrock : nearest;
    }
}
