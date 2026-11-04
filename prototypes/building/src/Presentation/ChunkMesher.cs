using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Godot;
using GalHack.Structures;
using NVec = System.Numerics.Vector3;

namespace GalHack.Structures.Presentation;

/// <summary>Vertex lists for one surface, ready to become an ArrayMesh surface.</summary>
public sealed class MeshBuffers
{
    public readonly List<Vector3> Vertices = new();
    public readonly List<Vector3> Normals = new();
    public readonly List<Color> Colors = new();
    public readonly List<Vector2> UVs = new();
    public readonly List<Vector2> UV2s = new();
    public readonly List<int> Indices = new();

    public int VertexCount => Vertices.Count;

    static readonly Vector3[] Rights = new Vector3[6], Ups = new Vector3[6];

    static MeshBuffers()
    {
        foreach (Dir d in Dirs.All)
        {
            // Seen from outside: up is +Y on walls and forward (-Z) on floors and roofs.
            Vector3 n = ToGodot(d.Normal());
            Vector3 up = d.Axis() == 1 ? Vector3.Forward : Vector3.Up;
            Rights[(int)d] = up.Cross(n);
            Ups[(int)d] = up;
        }
    }

    public void Clear()
    {
        Vertices.Clear();
        Normals.Clear();
        Colors.Clear();
        UVs.Clear();
        UV2s.Clear();
        Indices.Clear();
    }

    /// <summary>
    /// Adds a convex polygon given in metres, counter-clockwise around <paramref name="normal"/>
    /// (it is flipped if not). Godot's front faces are clockwise, so the fan is emitted reversed.
    /// UV is the position on the face in block units, so patterns line up across blocks.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    public void AddPolygon(ReadOnlySpan<Vector3> points, Vector3 normal, Dir slot, float cellSize, in FaceLook look,
        ReadOnlySpan<Vector3> vertexNormals = default)
    {
        int start = Vertices.Count;
        Vector3 right = Rights[(int)slot], up = Ups[(int)slot];
        Vector2 uv2 = new(look.Accent, look.Flags);
        float inv = 1f / cellSize;
        var half = new Vector3(0.5f, 0.5f, 0.5f);
        bool smooth = vertexNormals.Length > 0;
        for (int i = 0; i < points.Length; i++)
        {
            Vector3 p = points[i];
            Vertices.Add(p);
            Normals.Add(smooth ? vertexNormals[i] : normal);
            Colors.Add(look.Base);
            Vector3 c = p * inv + half;
            UVs.Add(new Vector2(c.Dot(right), c.Dot(up)));
            UV2s.Add(uv2);
        }
        bool ccw = (points[1] - points[0]).Cross(points[2] - points[0]).Dot(normal) > 0;
        for (int i = 1; i + 1 < points.Length; i++)
        {
            Indices.Add(start);
            Indices.Add(start + (ccw ? i + 1 : i));
            Indices.Add(start + (ccw ? i : i + 1));
        }
    }

    public Godot.Collections.Array ToArrays()
    {
        var arrays = new Godot.Collections.Array();
        arrays.Resize((int)Mesh.ArrayType.Max);
        arrays[(int)Mesh.ArrayType.Vertex] = Vertices.ToArray();
        arrays[(int)Mesh.ArrayType.Normal] = Normals.ToArray();
        arrays[(int)Mesh.ArrayType.Color] = Colors.ToArray();
        arrays[(int)Mesh.ArrayType.TexUV] = UVs.ToArray();
        arrays[(int)Mesh.ArrayType.TexUV2] = UV2s.ToArray();
        arrays[(int)Mesh.ArrayType.Index] = Indices.ToArray();
        return arrays;
    }

    public static Vector3 ToGodot(NVec v) => new(v.X, v.Y, v.Z);
}

/// <summary>How one face is drawn: base colour (sRGB), accent colour (0xRRGGBB) and the packed flags.</summary>
public readonly record struct FaceLook(Color Base, uint Accent, int Flags);

/// <summary>Everything one chunk produces: the opaque and glass surfaces and its collision shapes.</summary>
public sealed class ChunkMeshData
{
    public readonly MeshBuffers Opaque = new(), Glass = new();
    public readonly List<(Vector3 center, Vector3 size)> Boxes = new();
    public readonly List<Vector3[]> Hulls = new();

    public void Clear()
    {
        Opaque.Clear();
        Glass.Clear();
        Boxes.Clear();
        Hulls.Clear();
    }
}

/// <summary>
/// Turns one 16³ chunk into a mesh and colliders. Cube blocks are greedy-meshed (coplanar faces
/// that look the same merge into one quad); every other shape and every component appends its
/// own polygons. Faces hidden by a neighbour's full opaque face are culled, across chunk edges
/// too. Colliders are boxes greedily merged in 3D, plus a convex hull per sloped block.
///
/// The chunk is first copied with a one-cell border into flat arrays, along with each block's
/// face masks from the catalogue, so the hot loops are plain array reads.
/// </summary>
public static class ChunkMesher
{
    const int S = Chunk.Size, P = S + 2;
    const int StrideY = P, StrideZ = P * P;
    const uint DarkColor = 0x23252e;

    static readonly int[] Stride = { 1, StrideY, StrideZ };
    static readonly Basis[] Bases = new Basis[Orientations.Count];

    [ThreadStatic] static Block[] _pad;
    [ThreadStatic] static byte[] _occlude, _full, _damage;
    [ThreadStatic] static ushort[] _glass;
    [ThreadStatic] static bool[] _cube, _solid;
    [ThreadStatic] static long[] _mask;
    [ThreadStatic] static Vector3[] _poly, _normals;

    static ChunkMesher()
    {
        for (int o = 0; o < Orientations.Count; o++)
        {
            Mat3 m = Orientations.Matrix(o);
            Bases[o] = new Basis(MeshBuffers.ToGodot(m.X.ToVector3()), MeshBuffers.ToGodot(m.Y.ToVector3()), MeshBuffers.ToGodot(m.Z.ToVector3()));
        }
    }

    static int Pad(int x, int y, int z) => (x + 1) + P * ((y + 1) + P * (z + 1));

    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    public static ChunkMeshData Build(Structure s, Int3 chunk, ChunkMeshData data = null)
    {
        data ??= new ChunkMeshData();
        data.Clear();
        if (_pad == null)
        {
            _pad = new Block[P * P * P];
            _occlude = new byte[P * P * P];
            _full = new byte[P * P * P];
            _damage = new byte[P * P * P];
            _glass = new ushort[P * P * P];
            _cube = new bool[P * P * P];
            _solid = new bool[S * S * S];
            _mask = new long[S * S];
            _poly = new Vector3[64];
            _normals = new Vector3[64];
        }

        Catalogue cat = s.Catalogue;
        Int3 origin = Structure.ChunkOrigin(chunk);
        s.Chunks.TryGetValue(chunk, out Chunk own);
        if (own == null) return data;

        for (int z = -1; z <= S; z++)
            for (int y = -1; y <= S; y++)
                for (int x = -1; x <= S; x++)
                {
                    int i = Pad(x, y, z);
                    bool inside = x >= 0 && y >= 0 && z >= 0 && x < S && y < S && z < S;
                    Block b;
                    if (inside) b = own.Cells[Chunk.Index(x, y, z)];
                    else s.TryGet(origin + new Int3(x, y, z), out b);
                    _pad[i] = b;
                    if (b.IsEmpty)
                    {
                        _occlude[i] = _full[i] = 0;
                        _glass[i] = 0;
                        _cube[i] = false;
                        continue;
                    }
                    BlockType t = cat.Types[b.Type];
                    _occlude[i] = cat.OccludeMask(b);
                    _full[i] = cat.FullMask(b);
                    _glass[i] = t.Transparent ? b.Type : (ushort)0;
                    _cube[i] = t.IsArmor && cat.Shapes[b.Shape].IsCube;
                    _damage[i] = inside ? (byte)DamageBucket(b.Hp / cat.MaxHp(b, s.Grid)) : (byte)0;
                }

        float size = s.Grid.CellSize;
        GreedyCubes(s, origin, size, data);
        Parts(s, origin, size, data);
        Colliders(s, origin, size, data);
        return data;
    }

    /// <summary>One block on its own, with nothing culled: the ghost preview.</summary>
    public static ChunkMeshData BuildSingle(Structure s, Block block)
    {
        var scratch = new Structure(s.Catalogue, s.Grid);
        foreach (Dir d in Dirs.All)
            if (block.Paint[d] != 0) block.Paint[d] = scratch.Palette.Intern(s.Palette[block.Paint[d]]);
        scratch.Set(Int3.Zero, block);
        return Build(scratch, Int3.Zero);
    }

    /// <summary>True if the face of the block at pad index <paramref name="i"/> towards <paramref name="d"/> is hidden by its neighbour.</summary>
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    static bool Hidden(int i, Dir d, int n)
    {
        int back = 1 << ((int)d ^ 1);
        if ((_occlude[n] & back) != 0) return true;
        // Glass against the same glass: no inner panes.
        return _glass[i] != 0 && _glass[n] == _glass[i] && (_full[n] & back) != 0;
    }

    // ---- Cubes ---------------------------------------------------------------------------

    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    static void GreedyCubes(Structure s, Int3 origin, float size, ChunkMeshData data)
    {
        Span<Vector3> quad = stackalloc Vector3[4];
        int base0 = Pad(0, 0, 0);
        foreach (Dir d in Dirs.All)
        {
            int a = d.Axis(), u = (a + 1) % 3, v = (a + 2) % 3;
            int sa = Stride[a], su = Stride[u], sv = Stride[v];
            int toNeighbour = d.Sign() * sa;
            Vector3 normal = MeshBuffers.ToGodot(d.Normal());
            for (int k = 0; k < S; k++)
            {
                bool anyFace = false;
                for (int j = 0; j < S; j++)
                {
                    int row = base0 + k * sa + j * sv;
                    for (int i = 0; i < S; i++)
                    {
                        int c = row + i * su;
                        long key = 0;
                        if (_cube[c] && !Hidden(c, d, c + toNeighbour))
                        {
                            ref Block b = ref _pad[c];
                            key = 1 + b.Type + ((long)b.Paint[d] << 16) + ((long)_damage[c] << 32);
                            anyFace = true;
                        }
                        _mask[i + j * S] = key;
                    }
                }
                if (!anyFace) continue;

                for (int j = 0; j < S; j++)
                    for (int i = 0; i < S;)
                    {
                        long key = _mask[i + j * S];
                        if (key == 0) { i++; continue; }
                        int w = 1;
                        while (i + w < S && _mask[i + w + j * S] == key) w++;
                        int h = 1;
                        while (j + h < S)
                        {
                            bool same = true;
                            for (int x = 0; x < w && same; x++) same = _mask[i + x + (j + h) * S] == key;
                            if (!same) break;
                            h++;
                        }
                        for (int y = 0; y < h; y++)
                            for (int x = 0; x < w; x++) _mask[i + x + (j + y) * S] = 0;

                        int c = base0 + k * sa + i * su + j * sv;
                        float plane = origin[a] + k + d.Sign() * 0.5f;
                        float u0 = origin[u] + i - 0.5f, u1 = u0 + w, v0 = origin[v] + j - 0.5f, v1 = v0 + h;
                        quad[0] = Corner(a, u, v, plane, u0, v0) * size;
                        quad[1] = Corner(a, u, v, plane, u1, v0) * size;
                        quad[2] = Corner(a, u, v, plane, u1, v1) * size;
                        quad[3] = Corner(a, u, v, plane, u0, v1) * size;
                        (_glass[c] != 0 ? data.Glass : data.Opaque)
                            .AddPolygon(quad, normal, d, size, Look(s, _pad[c], d, PartStyle.Body, _damage[c]));
                        i += w;
                    }
            }
        }
    }

    static Vector3 Corner(int a, int u, int v, float pa, float pu, float pv)
    {
        var p = new Vector3();
        p[a] = pa;
        p[u] = pu;
        p[v] = pv;
        return p;
    }

    // ---- Shapes and components -----------------------------------------------------------

    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    static void Parts(Structure s, Int3 origin, float size, ChunkMeshData data)
    {
        Catalogue cat = s.Catalogue;
        for (int z = 0; z < S; z++)
            for (int y = 0; y < S; y++)
                for (int x = 0; x < S; x++)
                {
                    int c = Pad(x, y, z);
                    Block b = _pad[c];
                    if (b.IsEmpty || _cube[c]) continue;
                    BlockType t = cat.Types[b.Type];
                    Vector3 centre = MeshBuffers.ToGodot((origin + new Int3(x, y, z)).ToVector3());
                    if (t.IsArmor)
                    {
                        MeshBuffers target = t.Transparent ? data.Glass : data.Opaque;
                        foreach (Polygon poly in cat.Shapes[b.Shape].Polygons)
                            AddPart(s, b, c, centre, size, poly, NVec.Zero, PartStyle.Body, target);
                    }
                    else
                    {
                        foreach (Part part in t.Parts)
                        {
                            if (part.ClosedOnly && b.Open) continue;
                            MeshBuffers target = part.Style == PartStyle.Glass ? data.Glass : data.Opaque;
                            NVec partCentre = (part.Min + part.Max) * 0.5f;
                            foreach (Polygon poly in part.Polygons)
                                AddPart(s, b, c, centre, size, poly, partCentre, part.Style, target);
                        }
                    }
                }
    }

    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    static void AddPart(Structure s, in Block b, int c, Vector3 centre, float size, Polygon poly, NVec smoothCentre,
        PartStyle style, MeshBuffers target)
    {
        Basis basis = Bases[b.Rot];
        Dir slot;
        if (poly.Boundary is Dir local)
        {
            Dir world = Orientations.ToWorld(b.Rot, local);
            Int3 step = world.Vec();
            if (Hidden(c, world, c + step.X + step.Y * StrideY + step.Z * StrideZ)) return;
            slot = world;
        }
        else slot = Dirs.Closest(ToN(basis * ToG(poly.Normal)));

        // Shape points are System.Numerics vectors; the layout matches Godot's, so reinterpret instead of copying.
        ReadOnlySpan<Vector3> points = MemoryMarshal.Cast<NVec, Vector3>(poly.Points.AsSpan());
        for (int i = 0; i < points.Length; i++)
        {
            _poly[i] = (centre + basis * points[i]) * size;
            if (poly.SmoothAxis >= 0)
            {
                NVec radial = Geometry.Set(poly.Points[i] - smoothCentre, poly.SmoothAxis, 0f);
                _normals[i] = (basis * ToG(NVec.Normalize(radial))).Normalized();
            }
        }
        target.AddPolygon(_poly.AsSpan(0, points.Length), basis * ToG(poly.Normal), slot, size,
            Look(s, b, slot, style, _damage[c]), poly.SmoothAxis >= 0 ? _normals.AsSpan(0, points.Length) : default);
    }

    static Vector3 ToG(NVec v) => new(v.X, v.Y, v.Z);
    static NVec ToN(Vector3 v) => new(v.X, v.Y, v.Z);

    // ---- Appearance ----------------------------------------------------------------------

    /// <summary>0 healthy, 1-3 increasingly scorched. Part of the greedy key so damaged blocks stand out.</summary>
    static int DamageBucket(float healthFraction) =>
        healthFraction > 0.75f ? 0 : healthFraction > 0.5f ? 1 : healthFraction > 0.25f ? 2 : 3;

    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    public static FaceLook Look(Structure s, in Block b, Dir slot, PartStyle style, int damage)
    {
        BlockType t = s.Catalogue.Types[b.Type];
        switch (style)
        {
            case PartStyle.Dark:
                return new FaceLook(Rgb(DarkColor), Paint.DefaultAccent, Flags(Pattern.None, 0, FaceTexture.Smooth, damage, false, 2, 0, false));
            case PartStyle.Accent:
                return new FaceLook(Rgb(t.Accent), Paint.DefaultAccent, Flags(Pattern.None, 0, FaceTexture.Smooth, damage, false, 1, 0, false));
            case PartStyle.Glow:
                bool lit = !(t.IsLight && b.Off);
                return new FaceLook(Rgb(lit ? t.GlowColor : 0x3a3a3a), Paint.DefaultAccent, Flags(Pattern.None, 0, FaceTexture.Smooth, 0, lit, 0, 0, false));
        }

        ushort slotPaint = b.Paint[slot];
        if (slotPaint == 0)
            return new FaceLook(Rgb(t.Color), t.Accent, Flags(Pattern.None, 0, t.Texture, damage, false, t.Metal, 0, false));
        Paint p = s.Palette[slotPaint];
        int stencil = p.Stencil >= '0' && p.Stencil <= '9' ? p.Stencil - '0' + 1 : 0;
        return new FaceLook(Rgb(p.Base), p.Accent, Flags(p.Pattern, stencil, p.Texture, damage, false, t.Metal, p.Grime, true));
    }

    static int Flags(Pattern pattern, int stencil, FaceTexture texture, int damage, bool glow, int metal, int grime, bool painted) =>
        ((int)pattern << BlockMaterials.PatternShift)
        | (stencil << BlockMaterials.StencilShift)
        | ((int)texture << BlockMaterials.TextureShift)
        | (damage << BlockMaterials.DamageShift)
        | ((glow ? 1 : 0) << BlockMaterials.GlowShift)
        | (metal << BlockMaterials.MetalShift)
        | (grime << BlockMaterials.GrimeShift)
        | ((painted ? 1 : 0) << BlockMaterials.PaintedShift);

    public static Color Rgb(uint c) => new(((c >> 16) & 255) / 255f, ((c >> 8) & 255) / 255f, (c & 255) / 255f);

    // ---- Colliders -----------------------------------------------------------------------

    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    static void Colliders(Structure s, Int3 origin, float size, ChunkMeshData data)
    {
        Catalogue cat = s.Catalogue;
        Array.Clear(_solid);
        for (int z = 0; z < S; z++)
            for (int y = 0; y < S; y++)
                for (int x = 0; x < S; x++)
                {
                    int c = Pad(x, y, z);
                    Block b = _pad[c];
                    if (b.IsEmpty) continue;
                    BlockType t = cat.Types[b.Type];
                    if (_cube[c] || (t.IsDoor ? !b.Open : !t.IsArmor && t.Collide == Collide.Cell))
                    {
                        _solid[x + S * (y + S * z)] = true;
                        continue;
                    }
                    Vector3 centre = MeshBuffers.ToGodot((origin + new Int3(x, y, z)).ToVector3());
                    Basis basis = Bases[b.Rot];
                    if (t.IsArmor)
                    {
                        NVec[] pts = cat.Shapes[b.Shape].HullPoints;
                        var hull = new Vector3[pts.Length];
                        for (int i = 0; i < hull.Length; i++) hull[i] = (centre + basis * ToG(pts[i])) * size;
                        data.Hulls.Add(hull);
                    }
                    else if (t.Collide == Collide.Parts || t.IsDoor)
                    {
                        foreach (Part part in t.Parts)
                        {
                            if (part.ClosedOnly) continue;
                            Vector3 p0 = basis * ToG(part.Min), p1 = basis * ToG(part.Max);
                            Vector3 min = p0.Min(p1), max = p0.Max(p1);
                            data.Boxes.Add(((centre + (min + max) * 0.5f) * size, (max - min) * size));
                        }
                    }
                }

        // Greedy boxes: grow along x, then y, then z while every cell is solid and unused.
        for (int z = 0; z < S; z++)
            for (int y = 0; y < S; y++)
                for (int x = 0; x < S; x++)
                {
                    if (!_solid[x + S * (y + S * z)]) continue;
                    int x1 = x;
                    while (x1 + 1 < S && _solid[x1 + 1 + S * (y + S * z)]) x1++;
                    int y1 = y;
                    while (y1 + 1 < S && RowSolid(x, x1, y1 + 1, z)) y1++;
                    int z1 = z;
                    while (z1 + 1 < S && LayerSolid(x, x1, y, y1, z1 + 1)) z1++;
                    for (int zz = z; zz <= z1; zz++)
                        for (int yy = y; yy <= y1; yy++)
                            for (int xx = x; xx <= x1; xx++) _solid[xx + S * (yy + S * zz)] = false;
                    var lo = new Vector3(origin.X + x - 0.5f, origin.Y + y - 0.5f, origin.Z + z - 0.5f);
                    var hi = new Vector3(origin.X + x1 + 0.5f, origin.Y + y1 + 0.5f, origin.Z + z1 + 0.5f);
                    data.Boxes.Add(((lo + hi) * 0.5f * size, (hi - lo) * size));
                }
    }

    static bool RowSolid(int x0, int x1, int y, int z)
    {
        for (int x = x0; x <= x1; x++)
            if (!_solid[x + S * (y + S * z)]) return false;
        return true;
    }

    static bool LayerSolid(int x0, int x1, int y0, int y1, int z)
    {
        for (int y = y0; y <= y1; y++)
            if (!RowSolid(x0, x1, y, z)) return false;
        return true;
    }

    // ---- Godot resources -----------------------------------------------------------------

    /// <summary>Builds the chunk's ArrayMesh: opaque surface, then glass if any. Null when empty.</summary>
    public static ArrayMesh ToMesh(ChunkMeshData data)
    {
        if (data.Opaque.VertexCount == 0 && data.Glass.VertexCount == 0) return null;
        var mesh = new ArrayMesh();
        if (data.Opaque.VertexCount > 0)
        {
            mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, data.Opaque.ToArrays());
            mesh.SurfaceSetMaterial(mesh.GetSurfaceCount() - 1, BlockMaterials.Opaque);
        }
        if (data.Glass.VertexCount > 0)
        {
            mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, data.Glass.ToArrays());
            mesh.SurfaceSetMaterial(mesh.GetSurfaceCount() - 1, BlockMaterials.Glass);
        }
        return mesh;
    }
}
