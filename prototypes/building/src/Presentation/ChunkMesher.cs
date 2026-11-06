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
        int start = Vertices.Count, count = points.Length;
        Vector3 right = Rights[(int)slot], up = Ups[(int)slot];
        Vector2 uv2 = new(look.Accent, look.Flags);
        float inv = 1f / cellSize;
        var half = new Vector3(0.5f, 0.5f, 0.5f);
        bool smooth = vertexNormals.Length > 0;
        // Room for the whole polygon at once, then plain writes: a holed face is a lot of small polygons.
        Span<Vector3> vertices = Grow(Vertices, count), normals = Grow(Normals, count);
        Span<Color> colors = Grow(Colors, count);
        Span<Vector2> uvs = Grow(UVs, count), uv2s = Grow(UV2s, count);
        for (int i = 0; i < count; i++)
        {
            Vector3 p = points[i];
            vertices[i] = p;
            normals[i] = smooth ? vertexNormals[i] : normal;
            colors[i] = look.Base;
            Vector3 c = p * inv + half;
            uvs[i] = new Vector2(c.Dot(right), c.Dot(up));
            uv2s[i] = uv2;
        }
        bool ccw = (points[1] - points[0]).Cross(points[2] - points[0]).Dot(normal) > 0;
        Span<int> indices = Grow(Indices, (count - 2) * 3);
        for (int i = 1, n = 0; i + 1 < count; i++)
        {
            indices[n++] = start;
            indices[n++] = start + (ccw ? i + 1 : i);
            indices[n++] = start + (ccw ? i : i + 1);
        }
    }

    /// <summary>Lengthens a list by <paramref name="count"/> and hands back the new items to fill in.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    static Span<T> Grow<T>(List<T> list, int count)
    {
        int start = list.Count;
        CollectionsMarshal.SetCount(list, start + count);
        return CollectionsMarshal.AsSpan(list).Slice(start, count);
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

    /// <summary>Devices in this chunk so badly damaged they smoke and spark.</summary>
    public readonly List<Int3> Smoking = new();

    public void Clear()
    {
        Opaque.Clear();
        Glass.Clear();
        Boxes.Clear();
        Hulls.Clear();
        Smoking.Clear();
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
///
/// Damage is drawn from each block's health (<see cref="Damage.Bucket"/>). Scuffed cubes still
/// merge, and the shader marks them block by block. Dented and holed cube faces leave the greedy
/// pass and get real geometry of their own: a pushed-in pyramid, or a buckled ring around a hole
/// with the frame behind it. Each block's look comes from a hash of its cell and face, never from
/// how the faces happened to merge, so a remesh can't change it. The shader does the rest, for
/// every material and shape.
/// </summary>
public static class ChunkMesher
{
    const int S = Chunk.Size, P = S + 2;
    const int StrideY = P, StrideZ = P * P;
    const uint DarkColor = 0x23252e, FrameColor = 0x30333c, StrutColor = 0x565b66;

    /// <summary>How deep the frame sits behind a holed face, in cells.</summary>
    public const float FrameDepth = 0.34f;

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
    public static ChunkMeshData Build(Structure s, Int3 chunk, ChunkMeshData data = null, bool colliders = true)
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
                    _damage[i] = inside ? (byte)Damage.Bucket(b.Hp, cat.MaxHp(b, s.Grid)) : (byte)0;
                }

        float size = s.Grid.CellSize;
        GreedyCubes(s, origin, size, data);
        Parts(s, origin, size, data);
        if (colliders) Colliders(s, origin, size, data);
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
                            if (_damage[c] >= 2 && _glass[c] == 0)
                            {
                                DamagedFace(s, data, origin, c, d, a, u, v, i, j, k, size, normal);
                                _mask[i + j * S] = 0;
                                continue;
                            }
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

    // ---- Damaged faces --------------------------------------------------------------------

    /// <summary>A hash of one block's face: the same for that face however its neighbours mesh.</summary>
    public static uint FaceSeed(Int3 cell, Dir d)
    {
        uint h = unchecked((uint)cell.X * 73856093u ^ (uint)cell.Y * 19349663u ^ (uint)cell.Z * 83492791u ^ ((uint)d + 1u) * 2654435761u);
        h ^= h >> 15;
        h = unchecked(h * 2246822519u);
        return h ^ (h >> 13);
    }

    static float Next(ref uint h)
    {
        h = unchecked(h * 1664525u + 1013904223u);
        return ((h >> 8) & 0xffff) / 65536f;
    }

    /// <summary>The shader's per-block seed, in the colour's alpha. Under 1, which means "work it out from the position" (merged faces).</summary>
    static Color WithSeed(Color c, uint h) => new(c.R, c.G, c.B, (h & 0x7f) / 128f);

    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    static void DamagedFace(Structure s, ChunkMeshData data, Int3 origin, int c, Dir d, int a, int u, int v, int i, int j, int k, float size, Vector3 normal)
    {
        Int3 step = Int3.Zero.With(a, k).With(u, i).With(v, j);
        uint h = FaceSeed(origin + step, d);
        var f = new FaceFrame(a, u, v, origin[a] + k + d.Sign() * 0.5f, origin[u] + i - 0.5f, origin[v] + j - 0.5f, d.Sign(), size);
        if (_damage[c] == 2) DentedFace(s, data.Opaque, f, c, d, h, normal);
        else HoledFace(s, data.Opaque, f, c, d, h, normal);
    }

    /// <summary>A cell face's own coordinates: across it 0 to 1 each way, and depth into the block.</summary>
    readonly record struct FaceFrame(int A, int U, int V, float Plane, float U0, float V0, int Sign, float Size)
    {
        public Vector3 At(float s, float t, float depth) => Corner(A, U, V, Plane - Sign * depth, U0 + s, V0 + t) * Size;
    }

    /// <summary>Cracked and dented: the rim stays flush with its neighbours, and the middle is pushed in off-centre.</summary>
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    static void DentedFace(Structure s, MeshBuffers m, in FaceFrame f, int c, Dir d, uint h, Vector3 normal)
    {
        FaceLook look = Look(s, _pad[c], d, PartStyle.Body, 2);
        look = look with { Base = WithSeed(look.Base, h) };
        Vector3 p0 = f.At(0, 0, 0), p1 = f.At(1, 0, 0), p2 = f.At(1, 1, 0), p3 = f.At(0, 1, 0);
        float across = Next(ref h), along = Next(ref h), depth = 0.07f + 0.09f * Next(ref h);
        Vector3 dent = f.At(0.3f + 0.4f * across, 0.3f + 0.4f * along, depth);
        Tri(m, p0, p1, dent, normal, d, f.Size, look);
        Tri(m, p1, p2, dent, normal, d, f.Size, look);
        Tri(m, p2, p3, dent, normal, d, f.Size, look);
        Tri(m, p3, p0, dent, normal, d, f.Size, look);
    }

    /// <summary>
    /// Holed: the plate is torn open and bent inwards round the tear, and the block's frame shows
    /// behind it, recessed, with a strut across. The outer edge stays where it was, so nothing
    /// opens up against the neighbouring faces; only the look changes, never the collider.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    static void HoledFace(Structure s, MeshBuffers m, in FaceFrame f, int c, Dir d, uint h, Vector3 normal)
    {
        FaceLook plate = Look(s, _pad[c], d, PartStyle.Body, 3, holed: true);
        plate = plate with { Base = WithSeed(plate.Base, h) };
        FaceLook torn = new(WithSeed(Rgb(DarkColor), h), Paint.DefaultAccent, Flags(Pattern.None, 0, FaceTexture.Smooth, 3, false, 1, 0, false, DamageLook.Metal, true));
        // Not polished: it would mirror the sky down the hole.
        var frame = new FaceLook(Rgb(FrameColor), Paint.DefaultAccent, Flags(Pattern.None, 0, FaceTexture.Plates, 0, false, 0, 0, false, DamageLook.Metal, false));
        var strut = new FaceLook(Rgb(StrutColor), Paint.DefaultAccent, Flags(Pattern.None, 0, FaceTexture.Smooth, 0, false, 0, 0, false, DamageLook.Metal, false));

        // The tear: a lopsided four-sided hole, each corner its own way off a rough rectangle.
        float s0 = 0.14f + 0.14f * Next(ref h), s1 = 0.72f + 0.14f * Next(ref h);
        float t0 = 0.14f + 0.14f * Next(ref h), t1 = 0.72f + 0.14f * Next(ref h);
        Span<Vector2> hole = stackalloc Vector2[4];
        hole[0] = new Vector2(s0 + 0.08f * Next(ref h), t0 + 0.08f * Next(ref h));
        hole[1] = new Vector2(s1 - 0.08f * Next(ref h), t0 + 0.08f * Next(ref h));
        hole[2] = new Vector2(s1 - 0.08f * Next(ref h), t1 - 0.08f * Next(ref h));
        hole[3] = new Vector2(s0 + 0.08f * Next(ref h), t1 - 0.08f * Next(ref h));

        Span<Vector3> outer = stackalloc Vector3[4], rim = stackalloc Vector3[4], back = stackalloc Vector3[4];
        outer[0] = f.At(0, 0, 0);
        outer[1] = f.At(1, 0, 0);
        outer[2] = f.At(1, 1, 0);
        outer[3] = f.At(0, 1, 0);
        for (int n = 0; n < 4; n++)
        {
            rim[n] = f.At(hole[n].X, hole[n].Y, 0.05f + 0.13f * Next(ref h));
            back[n] = f.At(hole[n].X, hole[n].Y, FrameDepth);
        }

        Vector3 middle = (back[0] + back[1] + back[2] + back[3]) * 0.25f;
        Span<Vector3> quad = stackalloc Vector3[4];
        for (int n = 0; n < 4; n++)
        {
            int m1 = (n + 1) & 3;
            // The bent plate, two facets to a side: it creases as it folds in.
            Tri(m, outer[n], outer[m1], rim[m1], normal, d, f.Size, plate);
            Tri(m, outer[n], rim[m1], rim[n], normal, d, f.Size, plate);
            // The torn edge of the plate, down to the frame, facing into the hole.
            Vector3 inwards = (middle - (back[n] + back[m1]) * 0.5f).Normalized();
            quad[0] = rim[n];
            quad[1] = rim[m1];
            quad[2] = back[m1];
            quad[3] = back[n];
            m.AddPolygon(quad, inwards, d, f.Size, torn);
        }

        m.AddPolygon(back, normal, d, f.Size, frame);

        // The struts: one each way across the hole, standing proud of the frame.
        float sa = MathF.Max(hole[0].X, hole[3].X), sb = MathF.Min(hole[1].X, hole[2].X);
        float ta = MathF.Max(hole[0].Y, hole[1].Y), tb = MathF.Min(hole[2].Y, hole[3].Y);
        float sm = (sa + sb) * 0.5f, tm = (ta + tb) * 0.5f, depth = FrameDepth - 0.07f;
        const float W = 0.035f;
        quad[0] = f.At(sa, tm - W, depth);
        quad[1] = f.At(sb, tm - W, depth);
        quad[2] = f.At(sb, tm + W, depth);
        quad[3] = f.At(sa, tm + W, depth);
        m.AddPolygon(quad, normal, d, f.Size, strut);
        quad[0] = f.At(sm - W, ta, depth);
        quad[1] = f.At(sm + W, ta, depth);
        quad[2] = f.At(sm + W, tb, depth);
        quad[3] = f.At(sm - W, tb, depth);
        m.AddPolygon(quad, normal, d, f.Size, strut);
    }

    /// <summary>One flat-shaded triangle, facing the way of <paramref name="towards"/>: bent plates catch the light facet by facet.</summary>
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    static void Tri(MeshBuffers m, Vector3 a, Vector3 b, Vector3 c, Vector3 towards, Dir slot, float size, in FaceLook look)
    {
        Span<Vector3> points = stackalloc Vector3[3];
        points[0] = a;
        points[1] = b;
        points[2] = c;
        Vector3 n = (b - a).Cross(c - a).Normalized();
        if (n.Dot(towards) < 0f) n = -n;
        m.AddPolygon(points, n, slot, size, look);
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
                        if (_damage[c] == 3) data.Smoking.Add(origin + new Int3(x, y, z));
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
        FaceLook look = Look(s, b, slot, style, _damage[c]);
        // Every polygon of a shaped or component block carries its own seed: these faces never merge.
        if (_damage[c] > 0) look = look with { Base = WithSeed(look.Base, FaceSeed(Int3.Round(ToN(centre)), slot)) };
        target.AddPolygon(_poly.AsSpan(0, points.Length), basis * ToG(poly.Normal), slot, size, look,
            poly.SmoothAxis >= 0 ? _normals.AsSpan(0, points.Length) : default);
    }

    static Vector3 ToG(NVec v) => new(v.X, v.Y, v.Z);
    static NVec ToN(Vector3 v) => new(v.X, v.Y, v.Z);

    // ---- Appearance ----------------------------------------------------------------------

    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    public static FaceLook Look(Structure s, in Block b, Dir slot, PartStyle style, int damage, bool holed = false)
    {
        BlockType t = s.Catalogue.Types[b.Type];
        DamageLook wear = t.DamageLook;
        switch (style)
        {
            case PartStyle.Dark:
                return new FaceLook(Rgb(DarkColor), Paint.DefaultAccent, Flags(Pattern.None, 0, FaceTexture.Smooth, damage, false, 2, 0, false, wear, holed));
            case PartStyle.Accent:
                return new FaceLook(Rgb(t.Accent), Paint.DefaultAccent, Flags(Pattern.None, 0, FaceTexture.Smooth, damage, false, 1, 0, false, wear, holed));
            case PartStyle.Glow:
                // Smashed badly enough, a light goes out.
                bool lit = !(t.IsLight && b.Off) && damage < 3;
                return new FaceLook(Rgb(lit ? t.GlowColor : 0x3a3a3a), Paint.DefaultAccent, Flags(Pattern.None, 0, FaceTexture.Smooth, damage, lit, 0, 0, false, wear, holed));
        }

        ushort slotPaint = b.Paint[slot];
        if (slotPaint == 0)
            return new FaceLook(Rgb(t.Color), t.Accent, Flags(Pattern.None, 0, t.Texture, damage, false, t.Metal, 0, false, wear, holed));
        Paint p = s.Palette[slotPaint];
        int stencil = p.Stencil >= '0' && p.Stencil <= '9' ? p.Stencil - '0' + 1 : 0;
        return new FaceLook(Rgb(p.Base), p.Accent, Flags(p.Pattern, stencil, p.Texture, damage, false, t.Metal, p.Grime, true, wear, holed));
    }

    static int Flags(Pattern pattern, int stencil, FaceTexture texture, int damage, bool glow, int metal, int grime, bool painted, DamageLook wear, bool holed) =>
        ((int)pattern << BlockMaterials.PatternShift)
        | (stencil << BlockMaterials.StencilShift)
        | ((int)texture << BlockMaterials.TextureShift)
        | (damage << BlockMaterials.DamageShift)
        | ((glow ? 1 : 0) << BlockMaterials.GlowShift)
        | (metal << BlockMaterials.MetalShift)
        | (grime << BlockMaterials.GrimeShift)
        | ((painted ? 1 : 0) << BlockMaterials.PaintedShift)
        | ((wear == DamageLook.Glass ? 0 : (int)wear) << BlockMaterials.WearShift)
        | ((holed ? 1 : 0) << BlockMaterials.HoledShift);

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
