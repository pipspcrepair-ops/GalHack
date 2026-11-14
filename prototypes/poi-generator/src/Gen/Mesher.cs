#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using FaceSide = PoiGenerator.Gen.Side;

namespace PoiGenerator.Gen;

/// <summary>Which material a face is drawn with.</summary>
/// <summary>Which material a face is drawn with. Flesh is the Hollow Bloom: lit, wet, faintly glowing.</summary>
public enum Surface { Matte, Metal, Glow, Glass, Flesh }

/// <summary>
/// Plain vertex arrays for one surface of one chunk: positions, normals, sRGB colours, and each face's
/// finish code and the place's seed (two floats a vertex: see <see cref="Surfaces.Code"/>), which the
/// place shader draws its surface from.
/// </summary>
public sealed class MeshBuffer
{
    public readonly List<float> Positions = new();
    public readonly List<float> Normals = new();
    public readonly List<float> Colors = new();
    public readonly List<float> Codes = new();
    public readonly List<int> Indices = new();
    /// <summary>The first vertex of each grid cell face (a cube's or the ground's, or a merged run of them): four vertices of its own, never over another (<see cref="MeshStandoff"/>).</summary>
    internal readonly List<int> Grid = new();
    public int VertexCount => Positions.Count / 3;

    public void Vertex(V3 p, V3 n, float r, float g, float b, float a, float code = 0f, float seed = 0f)
    {
        Positions.Add(p.X); Positions.Add(p.Y); Positions.Add(p.Z);
        Normals.Add(n.X); Normals.Add(n.Y); Normals.Add(n.Z);
        Colors.Add(r); Colors.Add(g); Colors.Add(b); Colors.Add(a);
        Codes.Add(code); Codes.Add(seed);
    }

    /// <summary>Another buffer's vertices, scaled and moved, and its triangles after them (a far lump put together, a city tile's buildings in one).</summary>
    public void Append(MeshBuffer from, float scale, V3 offset)
    {
        int first = VertexCount;
        for (int v = 0; v < from.VertexCount; v++)
        {
            Positions.Add(from.Positions[v * 3] * scale + offset.X);
            Positions.Add(from.Positions[v * 3 + 1] * scale + offset.Y);
            Positions.Add(from.Positions[v * 3 + 2] * scale + offset.Z);
        }
        Normals.AddRange(from.Normals);
        Colors.AddRange(from.Colors);
        if (from.Codes.Count == from.VertexCount * 2) Codes.AddRange(from.Codes);
        else for (int v = 0; v < from.VertexCount * 2; v++) Codes.Add(0f);
        foreach (int i in from.Indices) Indices.Add(first + i);
    }
}

public readonly record struct V3(float X, float Y, float Z)
{
    public static V3 operator +(V3 a, V3 b) => new(a.X + b.X, a.Y + b.Y, a.Z + b.Z);
    public static V3 operator -(V3 a, V3 b) => new(a.X - b.X, a.Y - b.Y, a.Z - b.Z);
    public static V3 operator *(V3 a, float k) => new(a.X * k, a.Y * k, a.Z * k);
    public static V3 Cross(V3 a, V3 b) => new(a.Y * b.Z - a.Z * b.Y, a.Z * b.X - a.X * b.Z, a.X * b.Y - a.Y * b.X);
    public static float Dot(V3 a, V3 b) => a.X * b.X + a.Y * b.Y + a.Z * b.Z;
    public V3 Normalized() { float l = MathF.Sqrt(X * X + Y * Y + Z * Z); return l > 0 ? this * (1f / l) : this; }
    public static V3 Of(Int3 p) => new(p.X, p.Y, p.Z);
}

public sealed class ChunkMesh
{
    public Int3 Chunk;
    public readonly MeshBuffer[] Surfaces = { new(), new(), new(), new(), new() };
    public readonly List<float> Collision = new(); // triangle soup, xyz per vertex
    public int Quads;
}

public sealed class MeshOptions
{
    public int ChunkSize = 16;
    /// <summary>Draw door panels (off when the viewer animates its own doors).</summary>
    public bool Doors = true;
    /// <summary>Rock fills this XZ rectangle below ground, down to RockBottom.</summary>
    public Int3 RockMin, RockMax;
    public int RockBottom = -8;
    public bool Rock = true;
    public Rgb GroundColor = new(0xc0, 0x80, 0x50);
    /// <summary>The ground colour at a cell, so a plug's top matches the terrain around it exactly.</summary>
    public Func<int, int, Rgb>? GroundColorAt;
    public Rgb RockColor = new(0x7a, 0x55, 0x3a);
    public bool Collision = true;
    /// <summary>Draw only the blocks this picks; the rest still hide faces (two meshes of one structure with different materials).</summary>
    public Func<Int3, bool>? Only;
    /// <summary>Small floor clutter (papers, ash, scorch, weeds). Off for distant views.</summary>
    public bool Detail = true;
    /// <summary>Set apart faces that would z-fight (<see cref="MeshStandoff"/>).</summary>
    public bool Standoff = true;
    /// <summary>
    /// How many cells of buried ground below ground level hide no faces. On a planet nothing keeps the
    /// ground where it was generated: it dips round a room carved under the pad, a cut or a bank leaves it
    /// short, the player digs it away. A face left out there is a wall you see straight through from
    /// outside and still walk into (its inner face is solid from behind), so the planet draws every
    /// face however deep: <see cref="AllGround"/>.
    /// </summary>
    public int GroundShow;
    /// <summary>For <see cref="GroundShow"/>: buried ground hides no face at any depth.</summary>
    public const int AllGround = 1 << 20;
    /// <summary>How much each cell's shade wanders, as a share of the full wander: less on plain walls, where it reads as noise.</summary>
    public float Mottle = 1f;

    /// <summary>
    /// Anything standing on a planet: buried ground is turned on so rooms keep their shading, with an
    /// empty plug, so none of it is drawn, and it hides no face: the voxel terrain is the ground there, and it can be dug away.
    /// </summary>
    public static MeshOptions OnPlanet(Int3 min) => new()
    {
        Doors = true, Collision = true, Detail = true, Rock = true, Mottle = 0.3f,
        RockMin = new Int3(min.X + 1, 0, min.Z + 1), RockMax = new Int3(min.X, 0, min.Z), RockBottom = min.Y, GroundShow = AllGround,
    };

    /// <summary>
    /// Runs of full cubes merge into big flat quads, of a colour rounded a little, with no per-corner
    /// shading: a place seen from further off, and its collision. Small things are left out of the
    /// drawing (never out of the collision), and what's left of the rest is a few boxes each.
    /// </summary>
    public bool Merged;
    /// <summary>Nothing drawn: collision only.</summary>
    public bool Drawn = true;
}

/// <summary>
/// Turns a block grid into chunked meshes. Faces between full cubes (and against buried rock)
/// are culled; each cube corner gets ambient occlusion from its neighbours, which gives the
/// flat-coloured blocks their depth. Props are a few boxes each, so a whole place stays a
/// handful of draw calls per chunk.
/// </summary>
public static class Mesher
{
    /// <summary>Faces carry their finish codes (off only to measure what they cost: every face then draws plain).</summary>
    public static bool Finishes = true;

    static readonly V3[] Normals =
    {
        new(0, 0, -1), new(1, 0, 0), new(0, 0, 1), new(-1, 0, 0), new(0, 1, 0), new(0, -1, 0),
    };

    // Baked face shading: a little like a painted illustration, light from above.
    static readonly float[] FaceShade = { 0.9f, 0.84f, 0.94f, 0.8f, 1f, 0.66f };
    static readonly float[] AoLevels = { 1f, 0.8f, 0.64f, 0.5f };

    public static List<ChunkMesh> Build(IBlockStructure grid, CellMask occupied, MeshOptions opt)
    {
        var chunks = new Dictionary<Int3, ChunkMesh>();
        var ctx = new Ctx(grid, occupied, opt, chunks);
        Int3 min = grid.Min, max = grid.Min + grid.Size - new Int3(1, 1, 1);
        for (int y = min.Y; y <= max.Y; y++)
            for (int z = min.Z; z <= max.Z; z++)
                for (int x = min.X; x <= max.X; x++)
                {
                    var p = new Int3(x, y, z);
                    var b = grid.Get(p);
                    if (!b.IsAir && (opt.Only == null || opt.Only(p))) ctx.Block(p, b);
                }
        if (opt.Rock)
            for (int y = opt.RockBottom; y <= Assembler.GroundY; y++)
                for (int z = opt.RockMin.Z; z <= opt.RockMax.Z; z++)
                    for (int x = opt.RockMin.X; x <= opt.RockMax.X; x++)
                    {
                        var p = new Int3(x, y, z);
                        if (ctx.IsRock(p)) ctx.Rock(p);
                    }
        ctx.Flush();
        ctx.MergeFaces();
        var list = new List<ChunkMesh>(chunks.Values);
        if (opt.Standoff) MeshStandoff.Apply(list);
        list.Sort((a, b) => (a.Chunk.Y, a.Chunk.Z, a.Chunk.X).CompareTo((b.Chunk.Y, b.Chunk.Z, b.Chunk.X)));
        return list;
    }

    /// <summary>One chunk alone, seeing its neighbours' cells exactly as the whole build does; null if nothing is in it. The options are asked for by the chunk's lowest cell.</summary>
    public static ChunkMesh? BuildChunk(IBlockStructure grid, CellMask occupied, Int3 chunk, int size, Func<Int3, MeshOptions> options)
    {
        var window = new ChunkWindow(grid, chunk * size, size);
        foreach (ChunkMesh m in Build(window, occupied, options(window.Min)))
            if (m.Chunk == chunk) return m;
        return null;
    }

    /// <summary>The chunks whose faces or shading depend on these cells: their own and every one round them, so a block gone shows what it hid in the next chunk too.</summary>
    public static HashSet<Int3> ChunksTouching(IEnumerable<Int3> cells, int size)
    {
        var chunks = new HashSet<Int3>();
        foreach (Int3 c in cells)
            for (int dz = -1; dz <= 1; dz++)
            for (int dy = -1; dy <= 1; dy++)
            for (int dx = -1; dx <= 1; dx++)
                chunks.Add(new Int3(FloorDiv(c.X + dx, size), FloorDiv(c.Y + dy, size), FloorDiv(c.Z + dz, size)));
        return chunks;
    }

    static int FloorDiv(int a, int b) => a >= 0 ? a / b : (a - b + 1) / b;

    /// <summary>
    /// A whole place as one small mesh for the far distance: cells of <paramref name="step"/> blocks a side,
    /// solid where enough stands in them, their mean colour, merged flat. It keeps the silhouette and the
    /// colours of the walls and little else. In the grid's own units.
    /// </summary>
    /// <summary>A window's tone from far off: a pale grey-blue, the sky in the glass.</summary>
    static readonly Rgb Window = new(0x8e, 0x9a, 0xa6);

    public static ChunkMesh Coarse(BlockGrid g, CellMask occupied, int step = 4, int minimum = 7)
    {
        Int3 size = new((g.Size.X + step - 1) / step, (g.Size.Y + step - 1) / step, (g.Size.Z + step - 1) / step);
        var count = new int[size.X * size.Y * size.Z];
        var sum = new long[count.Length * 3];
        foreach (var (c, b) in g.Blocks())
        {
            int weight = BlockInfo.IsFullCube(b.Type) ? 2 : BlockInfo.IsPlanCut(b.Type) || b.Type is BlockType.Ramp or BlockType.Corner or BlockType.InvCorner or BlockType.Stair or BlockType.Rib or BlockType.Pillar or BlockType.Pipe or BlockType.Tank or BlockType.Lattice ? 1 : 0;
            if (weight == 0 || BlockInfo.IsTransparent(b.Material)) continue;
            Int3 l = c - g.Min;
            int i = ((l.Y / step) * size.Z + l.Z / step) * size.X + l.X / step;
            count[i] += weight;
            // From far off a window shows the sky it mirrors, not its dark glass: averaged as glass, a tower of
            // windows was a dark navy box on the skyline.
            Rgb col = b.Role == Role.Window ? Window : b.Color;
            sum[i * 3] += col.R * weight;
            sum[i * 3 + 1] += col.G * weight;
            sum[i * 3 + 2] += col.B * weight;
        }
        var coarse = new BlockGrid(new Int3(0, 0, 0), size);
        for (int y = 0, i = 0; y < size.Y; y++)
            for (int z = 0; z < size.Z; z++)
                for (int x = 0; x < size.X; x++, i++)
                {
                    int n = count[i];
                    if (n < minimum * 2) continue;
                    var color = new Rgb((byte)(sum[i * 3] / n), (byte)(sum[i * 3 + 1] / n), (byte)(sum[i * 3 + 2] / n));
                    coarse.Set(new Int3(x, y, z), new Block(BlockType.Cube, Material.Concrete, color, Role.Wall));
                }
        // What each lump is finished in, from far off: its walls' commonest finish, and its roofs'.
        PlaceLook look = g.Look ?? new PlaceLook();
        var roofs = new byte[count.Length];
        var votes = new (byte Wall, int WallN, byte Roof, int RoofN)[count.Length];
        foreach (var (c, b) in g.Blocks())
        {
            if (!BlockInfo.IsFullCube(b.Type) && !BlockInfo.IsSlope(b.Type) && !BlockInfo.IsCornerSlope(b.Type) || BlockInfo.IsTransparent(b.Material)) continue;
            Int3 l = c - g.Min;
            int i = ((l.Y / step) * size.Z + l.Z / step) * size.X + l.X / step;
            bool top = b.Role is Role.Roof || b.Role == Role.Floor && !BlockInfo.Collides(g.Get(c + Int3.Up).Type);
            Finish fin = look.Pick(b, top ? FaceSide.Roof : FaceSide.Out, BlockInfo.IsSlope(b.Type) || BlockInfo.IsCornerSlope(b.Type), 0).Finish;
            ref var v = ref votes[i];
            // The commonest, by a running vote: one byte a lump, however many kinds are in it.
            if (top) { if (v.RoofN == 0) { v.Roof = (byte)fin; v.RoofN = 1; } else v.RoofN += v.Roof == (byte)fin ? 1 : -1; }
            else { if (v.WallN == 0) { v.Wall = (byte)fin; v.WallN = 1; } else v.WallN += v.Wall == (byte)fin ? 1 : -1; }
        }
        for (int i = 0; i < votes.Length; i++) roofs[i] = votes[i].RoofN > 0 ? votes[i].Roof : votes[i].Wall;
        coarse.Look = look.Far(roofs, size);
        for (int y = 0, i = 0; y < size.Y; y++)
            for (int z = 0; z < size.Z; z++)
                for (int x = 0; x < size.X; x++, i++)
                {
                    if (count[i] < minimum * 2) continue;
                    ref Block cb = ref coarse.At(new Int3(x, y, z));
                    cb.Variant = (byte)(1 + (votes[i].WallN > 0 ? votes[i].Wall : votes[i].Roof));
                }
        var built = Build(coarse, occupied, new MeshOptions { ChunkSize = Math.Max(size.X, Math.Max(size.Y, size.Z)) + 1, Rock = false, Collision = false, Detail = false, Doors = false, Merged = true });
        var all = new ChunkMesh();
        foreach (ChunkMesh m in built)
        {
            all.Quads += m.Quads;
            for (int k = 0; k < m.Surfaces.Length; k++) all.Surfaces[k].Append(m.Surfaces[k], step, V3.Of(g.Min));
        }
        return all;
    }

    sealed class Ctx
    {
        readonly IBlockStructure _g;
        readonly CellMask _occ;
        readonly MeshOptions _o;
        readonly Dictionary<Int3, ChunkMesh> _chunks;

        // Occlusion questions are asked a dozen times per face, so the answers for the whole
        // region are worked out once into a flat array.
        const byte FOccludes = 1, FCollides = 2, FRock = 4;
        // For the finishes: something stands here to be stood on; something solid lies overhead within twelve cells; this cell is such a cover.
        const byte FStands = 8, FCovered = 16, FCover = 32;
        readonly byte[] _flags;
        readonly Int3 _fMin, _fSize;

        readonly PlaceLook _look;
        readonly float _seed;
        readonly Dictionary<long, FinishPick> _picks = new();

        public Ctx(IBlockStructure g, CellMask occ, MeshOptions o, Dictionary<Int3, ChunkMesh> chunks)
        {
            _g = g;
            _occ = occ;
            _o = o;
            _chunks = chunks;
            _look = (g is BlockGrid bg ? bg.Look : g is ChunkWindow w ? w.Look : null) ?? new PlaceLook();
            _seed = _look.Seed;

            Int3 min = g.Min, max = g.Min + g.Size - new Int3(1, 1, 1);
            if (o.Rock)
            {
                // An empty rock area (min past max) reaches nowhere: taken literally it would stretch these
                // bounds from a grid far out in world coordinates all the way to the origin.
                bool area = o.RockMin.X <= o.RockMax.X && o.RockMin.Z <= o.RockMax.Z;
                min = Int3.Min(min, new Int3(area ? o.RockMin.X : min.X, o.RockBottom, area ? o.RockMin.Z : min.Z));
                max = Int3.Max(max, new Int3(area ? o.RockMax.X : max.X, Assembler.GroundY, area ? o.RockMax.Z : max.Z));
            }
            _fMin = min - new Int3(1, 1, 1);
            _fSize = max - min + new Int3(3, 3, 3);
            _flags = new byte[_fSize.X * _fSize.Y * _fSize.Z];
            int i = 0;
            for (int y = 0; y < _fSize.Y; y++)
                for (int z = 0; z < _fSize.Z; z++)
                    for (int x = 0; x < _fSize.X; x++, i++)
                    {
                        var p = new Int3(_fMin.X + x, _fMin.Y + y, _fMin.Z + z);
                        var b = g.Get(p);
                        byte f = 0;
                        if (!b.IsAir)
                        {
                            if (BlockInfo.IsFullCube(b.Type))
                            {
                                f |= FCollides;
                                if (!BlockInfo.IsTransparent(b.Material)) f |= FOccludes;
                            }
                            else if (o.Merged && Shapes.FarAsCube(b)) f |= FOccludes;
                            if (BlockInfo.Supports(b.Type) || BlockInfo.IsSlope(b.Type) || b.Type == BlockType.Rubble) f |= FStands;
                            if (Covers(b)) f |= FCover;
                        }
                        else if (o.Rock && p.Y <= Assembler.GroundY && !occ[p]) f = FOccludes | FCollides | FRock | FStands;
                        _flags[i] = f;
                    }
            // Each column from the top down: under a cover within twelve cells, counting what stands over the region.
            int layer = _fSize.X * _fSize.Z;
            for (int z = 0; z < (Finishes ? _fSize.Z : 0); z++)
                for (int x = 0; x < _fSize.X; x++)
                {
                    int top = _fMin.Y + _fSize.Y - 1, last = int.MaxValue;
                    for (int k = 1; k <= 12 && last == int.MaxValue; k++)
                        if (Covers(g.Get(new Int3(_fMin.X + x, top + k, _fMin.Z + z)))) last = top + k;
                    for (int y = _fSize.Y - 1; y >= 0; y--)
                    {
                        int at = y * layer + z * _fSize.X + x;
                        if (last - (_fMin.Y + y) <= 12) _flags[at] |= FCovered;
                        if ((_flags[at] & FCover) != 0) last = _fMin.Y + y;
                    }
                }
        }

        static bool Covers(Block b) => !b.IsAir && (BlockInfo.IsFullCube(b.Type) || BlockInfo.IsSlope(b.Type) || BlockInfo.IsCornerSlope(b.Type)) && !BlockInfo.IsTransparent(b.Material);

        /// <summary>A cell's flags if the region holds it (its bit for <paramref name="bit"/>), else null: past the region the grid itself is asked.</summary>
        bool? Flag(Int3 p, byte bit)
        {
            int x = p.X - _fMin.X, y = p.Y - _fMin.Y, z = p.Z - _fMin.Z;
            if ((uint)x < (uint)_fSize.X && (uint)y < (uint)_fSize.Y && (uint)z < (uint)_fSize.Z)
                return (_flags[(y * _fSize.Z + z) * _fSize.X + x] & bit) != 0;
            return null;
        }

        byte Flags(Int3 p)
        {
            int x = p.X - _fMin.X, y = p.Y - _fMin.Y, z = p.Z - _fMin.Z;
            if ((uint)x < (uint)_fSize.X && (uint)y < (uint)_fSize.Y && (uint)z < (uint)_fSize.Z)
                return _flags[(y * _fSize.Z + z) * _fSize.X + x];
            // Beyond the region: the ground carries on below ground level, air above.
            return _o.Rock && p.Y <= Assembler.GroundY ? (byte)(FOccludes | FCollides | FRock) : (byte)0;
        }

        ChunkMesh Chunk(Int3 p)
        {
            int s = _o.ChunkSize;
            var c = new Int3(FloorDiv(p.X, s), FloorDiv(p.Y, s), FloorDiv(p.Z, s));
            if (!_chunks.TryGetValue(c, out var m)) _chunks[c] = m = new ChunkMesh { Chunk = c };
            return m;
        }

        static int FloorDiv(int a, int b) => a >= 0 ? a / b : (a - b + 1) / b;

        bool InRockRect(Int3 p) =>
            p.X >= _o.RockMin.X && p.X <= _o.RockMax.X && p.Z >= _o.RockMin.Z && p.Z <= _o.RockMax.Z;

        /// <summary>Buried ground: below ground level and not part of any module. Only the
        /// plug rectangle is drawn, but beyond it the ground still hides faces.</summary>
        public bool IsRock(Int3 p) => (Flags(p) & FRock) != 0;

        /// <summary>Hides a neighbour's face: a full opaque cube, or rock (including the ground beyond the plug).</summary>
        bool Occludes(Int3 p) => (Flags(p) & FOccludes) != 0;

        bool CollidesFull(Int3 p) => (Flags(p) & FCollides) != 0;

        /// <summary>Whether a neighbour hides a face: as <see cref="Occludes"/>, except the top of the buried ground where it may not really be (<see cref="MeshOptions.GroundShow"/>).</summary>
        bool Hides(Int3 p) => Occludes(p) && !(_o.GroundShow > 0 && IsRock(p) && p.Y > Assembler.GroundY - _o.GroundShow);

        // ---- Finishes -------------------------------------------------------------------------------
        // Each face carries its finish and the decay's say for the place shader (Surfaces.Code). These read
        // the whole grid, not the flags, so a chunk built alone asks the same questions as the whole build.

        /// <summary>Which of the six faces a normal is nearest (a slope counts as looking up).</summary>
        static int FaceOf(V3 n) => MathF.Abs(n.Y) > 0.5f ? (n.Y > 0 ? 4 : 5) : MathF.Abs(n.X) > MathF.Abs(n.Z) ? (n.X > 0 ? 1 : 3) : (n.Z > 0 ? 2 : 0);

        /// <summary>The step along a face's across axis as the place shader lays it out (its u: up × normal on walls, east on floors and roofs).</summary>
        static Int3 AcrossOf(int f) => f switch { 0 => new Int3(-1, 0, 0), 1 => new Int3(0, 0, -1), 2 => new Int3(1, 0, 0), 3 => new Int3(0, 0, 1), _ => new Int3(1, 0, 0) };

        /// <summary>The step up a face's other axis: up on walls, south on floors and roofs.</summary>
        static Int3 AlongOf(int f) => f >= 4 ? new Int3(0, 0, 1) : Int3.Up;

        bool RockAt(Int3 c) => _o.Rock && c.Y <= Assembler.GroundY && !_occ[c] && _g.Get(c).IsAir;

        /// <summary>Something stands here to be stood on: a floor, a slope, the ground.</summary>
        bool Stands(Int3 c)
        {
            if (Flag(c, FStands) is bool known) return known;
            Block b = _g.Get(c);
            return !b.IsAir && (BlockInfo.Supports(b.Type) || BlockInfo.IsSlope(b.Type) || b.Type == BlockType.Rubble) || RockAt(c);
        }

        readonly Dictionary<Int3, bool> _covered = new();

        /// <summary>Something solid overhead within a few storeys: under a roof. Asked of the same cell by each face round it, so remembered.</summary>
        bool Covered(Int3 c)
        {
            if (Flag(c, FCovered) is bool inRegion) return inRegion;
            if (_covered.TryGetValue(c, out bool known)) return known;
            bool covered = false;
            for (int y = 1; y <= 12 && !covered; y++)
            {
                Block b = _g.Get(c + Int3.Up * y);
                covered = !b.IsAir && (BlockInfo.IsFullCube(b.Type) || BlockInfo.IsSlope(b.Type) || BlockInfo.IsCornerSlope(b.Type)) && !BlockInfo.IsTransparent(b.Material);
            }
            return _covered[c] = covered;
        }

        /// <summary>How many cells a cell stands above whatever is under it, up to <paramref name="most"/>.</summary>
        int Above(Int3 c, int most)
        {
            int k = 0;
            while (k < most && !Stands(c - Int3.Up * (k + 1))) k++;
            return k;
        }

        bool OpaqueFull(Int3 c)
        {
            if (Flag(c, FOccludes) is bool known) return known;
            Block b = _g.Get(c);
            return BlockInfo.IsFullCube(b.Type) && !BlockInfo.IsTransparent(b.Material) || RockAt(c);
        }

        static bool IsGlass(Block b) => b.Material == Material.Glass && (b.Type is BlockType.Cube or BlockType.Pane);

        FinishPick Pick(Block b, FaceSide side, bool slope, int room)
        {
            Finish own = Surfaces.Own(b);
            long key = (long)b.Material | (long)b.Role << 6 | (long)(b.Color.R | b.Color.G << 8 | b.Color.B << 16) << 11
                | (long)(own != Finish.Plain ? b.Variant : 0) << 35 | (long)side << 43 | (slope ? 1L : 0L) << 46 | (long)(room & 255) << 47;
            if (!_picks.TryGetValue(key, out FinishPick pick)) _picks[key] = pick = _look.Pick(b, side, slope, room);
            return pick;
        }

        /// <summary>A far lump's cell index (Mesher.Coarse's look carries its roofs by it).</summary>
        int CoarseIndex(Int3 p) => (p.Y * _look.CoarseSize.Z + p.Z) * _look.CoarseSize.X + p.X;

        /// <summary>The place shader's code for one face of a block, looking along a normal (see <see cref="Surfaces.Code"/>); 0 draws it plain.</summary>
        float FaceCode(Int3 p, Block b, V3 normal, bool slope, Surface surf)
        {
            if (!Finishes || surf is Surface.Glow or Surface.Flesh) return 0f;
            int f = FaceOf(normal);
            Int3 n = p + Dirs.All[f].Vec();
            bool up = f == 4, down = f == 5;
            bool wallish = b.Role is Role.Wall or Role.Secret or Role.Window or Role.Door;
            FaceSide side;
            bool indoors = false;
            int h = 3, room = 0;
            if (_o.Merged)
                side = up && !wallish ? FaceSide.Roof : down && !wallish ? FaceSide.Soffit : FaceSide.Out;
            else if (down)
            {
                indoors = _occ[n] && !RockAt(n);
                side = wallish ? (indoors ? FaceSide.In : FaceSide.Out) : indoors ? FaceSide.Ceiling : FaceSide.Soffit;
                if (indoors) room = _look.RoomAt(n - Int3.Up * Above(n, 6));
            }
            else
            {
                indoors = _occ[n] && Covered(n);
                if (up)
                {
                    h = 0;
                    side = wallish ? (indoors ? FaceSide.In : FaceSide.Out) : indoors ? FaceSide.Floor : FaceSide.Roof;
                    if (indoors) room = _look.RoomAt(n);
                }
                else
                {
                    h = Above(n, 3);
                    side = indoors ? FaceSide.In : FaceSide.Out;
                    if (indoors) room = _look.RoomAt(n - Int3.Up * h);
                }
            }
            FinishPick pick = _look.CoarseRoofs != null && up && _look.CoarseRoofs.Length > 0
                ? new FinishPick((Finish)_look.CoarseRoofs[Math.Clamp(CoarseIndex(p), 0, _look.CoarseRoofs.Length - 1)])
                : Pick(b, side, slope, room);
            Finish fin = surf == Surface.Glass ? Finish.Glass : pick.Finish;
            if (fin == Finish.Plain) return 0f;
            int param = _look.ParamOf(pick with { Finish = fin });

            // The years: wear in patches, damp low and underground, fire where the fall burned.
            float w = _look.Wear * (indoors ? 0.75f : 1f);
            float j = Hash.Cell(new Int3(p.X >> 1, p.Y >> 1, p.Z >> 1), 0x3EA1u) % 1000 / 1000f;
            int wear = _o.Merged ? (int)MathF.Round(w * 3f) : Math.Clamp((int)MathF.Floor(w * 3f + (j - 0.5f) * 1.2f + 0.35f), 0, 3);
            bool damp = n.Y < 0 || (indoors ? _look.Water > 0.7f : _look.Water > 0.5f && h == 0);
            int scorch = 0;
            if (_look.Fire > 0f)
            {
                float j2 = Hash.Cell(new Int3(p.X / 6, p.Y / 4, p.Z / 6), 0x5C0Cu) % 1000 / 1000f;
                scorch = Math.Clamp((int)(_look.Fire * (j2 * 1.6f - 0.2f) * 2.6f), 0, 3);
            }
            bool painted = fin switch
            {
                Finish.Render or Finish.Plaster or Finish.Block or Finish.Weatherboard or Finish.Precast => Surfaces.Painted(b.Color, b.Material),
                Finish.Plate or Finish.Hull or Finish.Corrugated or Finish.Planks => param != 1 && Surfaces.Painted(b.Color, b.Material),
                _ => false,
            };
            Surfaces.Under under = fin switch
            {
                Finish.Render => _look.Tech == TechLevel.Low ? Surfaces.Under.Stone : Surfaces.Under.Brick,
                Finish.Plaster => b.Material switch
                {
                    Material.Ceramic => Surfaces.Under.Brick, Material.Rock => Surfaces.Under.Stone, Material.Timber => Surfaces.Under.Lath,
                    _ => _look.Tech == TechLevel.High ? Surfaces.Under.Poured : Surfaces.Under.Block,
                },
                Finish.Block => Surfaces.Under.Block,
                _ => Surfaces.Under.None,
            };
            // Edges: a pane's glass neighbours (so a window is framed round, with thin bars between its panes);
            // on a wall, an opening above or below it and a corner beside it.
            int edges = 0;
            if (_o.Merged) edges = fin == Finish.Glass ? 15 : 0;
            else if (fin == Finish.Glass)
            {
                Int3 u = AcrossOf(f), v = AlongOf(f);
                if (IsGlass(_g.Get(p - u))) edges |= 1;
                if (IsGlass(_g.Get(p + u))) edges |= 2;
                if (IsGlass(_g.Get(p - v))) edges |= 4;
                if (IsGlass(_g.Get(p + v))) edges |= 8;
            }
            else if (f < 4)
            {
                Int3 u = AcrossOf(f);
                if (!OpaqueFull(p + Int3.Up)) edges |= 1;
                if (!OpaqueFull(p + Int3.Down) && !Stands(p + Int3.Down)) edges |= 2;
                if (!OpaqueFull(p + u) || !OpaqueFull(p - u)) edges |= 4;
            }
            return Surfaces.Code(fin, param, wear, h, indoors, damp, scorch, painted, under, edges);
        }

        /// <summary>A code for a face with no block to ask (the ground plug): a finish at the place's wear.</summary>
        float PlainCode(Finish fin, bool damp = false) =>
            Surfaces.Code(fin, _look.ParamOf(new FinishPick(fin)), Math.Clamp((int)MathF.Round(_look.Wear * 3f), 0, 3), 3, false, damp, 0, false, Surfaces.Under.None, 0);

        public void Block(Int3 p, Block b)
        {
            // A floor's finish is paint on the top of the floor block under it (see Painted): it has no body of its
            // own, so nothing set in its cell cuts it and it can't fight the floor.
            if (BlockInfo.IsFinish(b.Type)) return;
            if (b.Type == BlockType.Door && !_o.Doors) return;
            if (!_o.Detail && b.Type is BlockType.Scatter or BlockType.Scorch or BlockType.Ash or BlockType.Plant or BlockType.Remains or BlockType.Rebar) return;
            if (BlockInfo.IsFullCube(b.Type))
            {
                if (_o.Merged) Mark(p, b);
                else Cube(p, b);
                return;
            }
            // From further off a shaped wall, floor or ceiling is drawn as the cube it was cut from (it still collides as itself).
            bool far = _o.Merged && Shapes.FarAsCube(b), drawn = _o.Drawn && !far;
            if (far) Mark(p, b, collide: false);
            var chunk = Chunk(p);
            int boxes = !drawn ? 0 : _o.Merged ? BoxesWhenMerged(b.Type) : int.MaxValue;
            if (boxes > 0)
            {
                var shape = Shapes.For(b, p, _g);
                for (int i = 0; i < shape.Count && i < boxes; i++) AddBox(chunk, p, shape[i], b);
            }
            // A stair is steps up close; from further off, a slope.
            if (b.Type == BlockType.Ramp || _o.Merged && b.Type == BlockType.Stair || b.Type == BlockType.Chamfer) Ramp(chunk, p, b, draw: drawn);
            else if (b.Type == BlockType.Stair) Ramp(chunk, p, b, draw: false); // drawn as steps, walked as a slope
            else if (b.Type == BlockType.Spike && !_o.Merged) Spike(chunk, p, b);
            else if (BlockInfo.IsCornerSlope(b.Type) || BlockInfo.IsHung(b.Type)) CornerSlope(chunk, p, b, drawn);
            else if (BlockInfo.IsPlanCut(b.Type)) Prism(chunk, p, b, _o.Drawn);

            if (_o.Collision && BlockInfo.Collides(b.Type) && !BlockInfo.IsSlope(b.Type) && !BlockInfo.IsCornerSlope(b.Type) && !BlockInfo.IsHung(b.Type) && !BlockInfo.IsPlanCut(b.Type))
            {
                // Props collide as their real shape: a table as its top and legs, a locker as its body against the wall.
                bool shaped = BlockInfo.IsShaped(b.Type);
                foreach (var (lo, hi) in Shapes.CollisionBoxes(b, p))
                    CollideBox(chunk, V3.Of(p) + lo, V3.Of(p) + hi, shaped ? (f => Shut(p, b, lo, hi, f, CollidesFull)) : null);
            }
        }

        /// <summary>
        /// Whether a shaped block's box face is shut: it lies on its cell's side and what is next to it covers it
        /// (a full block, or the same shape running on, as halves along a panel or a thin wall up its storey).
        /// </summary>
        bool Shut(Int3 p, Block b, V3 lo, V3 hi, int f, Func<Int3, bool> full)
        {
            const float e = 0.001f;
            bool side = f switch { 0 => lo.Z < e, 1 => hi.X > 1 - e, 2 => hi.Z > 1 - e, 3 => lo.X < e, 4 => hi.Y > 1 - e, _ => lo.Y < e };
            if (!side) return false;
            Dir d = Dirs.All[f];
            Int3 n = p + d.Vec();
            return full(n) || Shapes.RunsOn(b, _g.Get(n), d);
        }

        /// <summary>How many of a block's boxes are drawn from further off (the structural ones first in each shape); none for the small things.</summary>
        static int BoxesWhenMerged(BlockType t) => t switch
        {
            BlockType.Pillar or BlockType.Pane or BlockType.Door or BlockType.SealedDoor or BlockType.Tank => 1,
            BlockType.Rib or BlockType.Machine or BlockType.Pipe or BlockType.Barricade or BlockType.Statue or BlockType.Pump => 2,
            BlockType.Rail or BlockType.Lattice => 4,
            BlockType.Half or BlockType.Quarter or BlockType.Partition or BlockType.Beam => 1,
            BlockType.Column => 2,
            _ => 0,
        };

        // ---- Merged faces --------------------------------------------------------------------------

        /// <summary>The faces of one chunk, by plane: a colour key per cell of each plane, then merged into rectangles.</summary>
        sealed class Planes
        {
            public readonly Dictionary<int, int[]> Drawn = new(), Solid = new();
        }

        readonly Dictionary<Int3, Planes> _planes = new();

        static int[] Plane(Dictionary<int, int[]> planes, int key, int side)
        {
            if (!planes.TryGetValue(key, out int[] m)) planes[key] = m = new int[side * side];
            return m;
        }

        /// <summary>A colour rounded to 32 levels a channel, its surface and its finish (with its choice): what may merge. Never 0.</summary>
        static int Key(Rgb c, Surface s, int finish) => 1 + (c.R >> 3 | (c.G >> 3) << 5 | (c.B >> 3) << 10 | (int)s << 15 | (finish & 0xFF) << 18);

        /// <summary>A full cube's open faces, as the plain mesher would pick them, into the planes.</summary>
        void Mark(Int3 p, Block b, bool collide = true)
        {
            int side = _o.ChunkSize;
            Int3 ch = new(FloorDiv(p.X, side), FloorDiv(p.Y, side), FloorDiv(p.Z, side));
            Int3 l = p - ch * side;
            bool glass = BlockInfo.IsTransparent(b.Material);
            bool glyph = b.Material == Material.Glyph;
            Rgb body = glyph ? b.Color.Lerp(new Rgb(0x1a, 0x1b, 0x22), 0.88f) : b.Color;
            Surface bodySurf = glyph ? Surface.Matte : Shapes.SurfaceOf(b.Material);
            Planes planes = null!;
            for (int f = 0; f < 6; f++)
            {
                Int3 n = p + Dirs.All[f].Vec();
                var nb = _g.Get(n);
                bool hidden = glass ? (!nb.IsAir && nb.Type == BlockType.Cube && (nb.Material == b.Material || !BlockInfo.IsTransparent(nb.Material))) || IsRock(n) && Hides(n)
                    : Hides(n) || f < 4 && BlockInfo.IsPlanCut(nb.Type) && Shapes.Covers(nb, Dirs.All[f].Opposite(), (0f, 1f));
                // (A shaped neighbour drawn far off as a cube hides the face, but still doesn't fill the cell: the face still collides.)
                bool farShape = hidden && !CollidesFull(n) && !IsRock(n);
                if (hidden && !farShape) continue;
                bool solid = collide && _o.Collision && (!CollidesFull(n) || IsRock(n));
                if ((!_o.Drawn || hidden) && !solid) continue;
                planes ??= _planes.TryGetValue(ch, out Planes got) ? got : _planes[ch] = new Planes();
                int key = 0;
                if (_o.Drawn)
                {
                    // (Not Block.Equals: on a plain struct it boxes both, and this runs for every face of every cube.)
                    if (f == 4 && Painted(p) is Block paint)
                    {
                        Surface ps = Shapes.SurfaceOf(paint.Material);
                        key = Key(paint.Color, ps, (int)FaceCode(PaintedLike(p, paint), paint, Normals[f], false, ps) & 0xFF);
                    }
                    else key = Key(body, bodySurf, (int)FaceCode(p, b, Normals[f], false, bodySurf) & 0xFF);
                }
                var (u, v, k) = Face2(f, l);
                int plane = f * side + k;
                if (_o.Drawn && !hidden) Plane(planes.Drawn, plane, side)[v * side + u] = key;
                if (solid) Plane(planes.Solid, plane, side)[v * side + u] = 1;
            }
        }

        /// <summary>A cell in a face's plane: across the face, up it, and which plane.</summary>
        static (int U, int V, int Plane) Face2(int f, Int3 l) => f switch
        {
            0 or 2 => (l.X, l.Y, l.Z),
            1 or 3 => (l.Z, l.Y, l.X),
            _ => (l.X, l.Z, l.Y),
        };

        /// <summary>Back from a plane's cell to the cell, and the size of a w by h rectangle on it along x, y and z.</summary>
        static (Int3 Cell, V3 Size) Back(int f, int u, int v, int k, int w, int h) => f switch
        {
            0 or 2 => (new Int3(u, v, k), new V3(w, h, 1)),
            1 or 3 => (new Int3(k, v, u), new V3(1, h, w)),
            _ => (new Int3(u, k, v), new V3(w, 1, h)),
        };

        /// <summary>Turns the marked faces into rectangles, the fewest a greedy sweep finds.</summary>
        public void Flush()
        {
            if (!_o.Merged) return;
            int side = _o.ChunkSize;
            foreach (var (ch, planes) in _planes)
            {
                if (!_chunks.TryGetValue(ch, out ChunkMesh chunk)) _chunks[ch] = chunk = new ChunkMesh { Chunk = ch };
                V3 origin = V3.Of(ch * side);
                foreach (var (plane, mask) in planes.Drawn) Sweep(mask, plane, side, (f, cell, size, key) => DrawRect(chunk, f, origin + V3.Of(cell), size, key));
                foreach (var (plane, mask) in planes.Solid) Sweep(mask, plane, side, (f, cell, size, _) => SolidRect(chunk, f, origin + V3.Of(cell), size));
            }
            _planes.Clear();
        }

        static void Sweep(int[] mask, int plane, int side, Action<int, Int3, V3, int> emit)
        {
            int f = plane / side, k = plane % side;
            for (int v = 0; v < side; v++)
                for (int u = 0; u < side; u++)
                {
                    int key = mask[v * side + u];
                    if (key == 0) continue;
                    int w = 1;
                    while (u + w < side && mask[v * side + u + w] == key) w++;
                    int h = 1;
                    for (; v + h < side; h++)
                    {
                        bool same = true;
                        for (int x = 0; x < w && same; x++) same = mask[(v + h) * side + u + x] == key;
                        if (!same) break;
                    }
                    for (int y = 0; y < h; y++) Array.Clear(mask, (v + y) * side + u, w);
                    var (cell, size) = Back(f, u, v, k, w, h);
                    emit(f, cell, size, key);
                }
        }

        void DrawRect(ChunkMesh chunk, int f, V3 at, V3 size, int key)
        {
            key--;
            Surface surf = (Surface)(key >> 15 & 7);
            var buf = chunk.Surfaces[(int)surf];
            int fin = key >> 18 & 0xFF;
            float code = (fin & 63) == 0 ? 0f : Surfaces.Code((Finish)(fin & 63), fin >> 6, Math.Clamp((int)MathF.Round(_look.Wear * 3f), 0, 3), 3, false, false, 0, false, Surfaces.Under.None, (fin & 63) == (int)Finish.Glass ? 15 : 0);
            float shade = surf == Surface.Glow ? 1f : FaceShade[f] * 0.92f; // a little of the corners' shading, on the flat
            float r = ((key & 31) * 8 + 4) / 255f * shade, g = ((key >> 5 & 31) * 8 + 4) / 255f * shade, b = ((key >> 10 & 31) * 8 + 4) / 255f * shade;
            float alpha = surf == Surface.Glass ? 0.35f : 1f;
            Corners(f, out V3 c0, out V3 c1, out V3 c2, out V3 c3);
            V3 At(V3 c) => new(at.X + c.X * size.X, at.Y + c.Y * size.Y, at.Z + c.Z * size.Z);
            int i0 = buf.VertexCount;
            buf.Grid.Add(i0);
            V3 n = Normals[f];
            buf.Vertex(At(c0), n, r, g, b, alpha, code, _seed);
            buf.Vertex(At(c1), n, r, g, b, alpha, code, _seed);
            buf.Vertex(At(c2), n, r, g, b, alpha, code, _seed);
            buf.Vertex(At(c3), n, r, g, b, alpha, code, _seed);
            var idx = buf.Indices;
            idx.Add(i0); idx.Add(i0 + 2); idx.Add(i0 + 1); idx.Add(i0); idx.Add(i0 + 3); idx.Add(i0 + 2);
            chunk.Quads++;
        }

        void SolidRect(ChunkMesh chunk, int f, V3 at, V3 size)
        {
            Corners(f, out V3 c0, out V3 c1, out V3 c2, out V3 c3);
            V3 At(V3 c) => new(at.X + c.X * size.X, at.Y + c.Y * size.Y, at.Z + c.Z * size.Z);
            V3 a = At(c0), b = At(c1), c = At(c2), d = At(c3);
            Tri(chunk, a, b, c);
            Tri(chunk, a, c, d);
        }

        /// <summary>The finish laid on the floor block at <paramref name="p"/>: in the cell over it, or kept under what stands there.</summary>
        Block? Painted(Int3 p)
        {
            Int3 up = p + Int3.Up;
            Block a = _g.Get(up);
            if (BlockInfo.IsFinish(a.Type)) return a;
            if (a.IsAir || BlockInfo.IsFullCube(a.Type)) return null;
            Block under = _g.Under(up);
            return BlockInfo.IsFinish(under.Type) ? under : null;
        }

        /// <summary>
        /// Whose floor a painted top is coded as: its own, or under something standing on it, a bare neighbour's with
        /// the same finish, so the floor under a table is painted exactly as round it.
        /// </summary>
        Int3 PaintedLike(Int3 p, Block paint)
        {
            if (BlockInfo.IsFinish(_g.Get(p + Int3.Up).Type)) return p;
            foreach (Dir d in Dirs.Horizontal)
            {
                Int3 q = p + d.Vec();
                if (Same(_g.Get(q + Int3.Up), paint) && BlockInfo.IsFullCube(_g.Get(q).Type)) return q;
            }
            return p;
        }

        /// <summary>Two blocks alike in every field (Block.Equals would box both).</summary>
        static bool Same(in Block a, in Block b) =>
            a.Type == b.Type && a.Material == b.Material && a.Role == b.Role && a.Facing == b.Facing && a.Variant == b.Variant && a.Color == b.Color;

        void Cube(Int3 p, Block b)
        {
            var chunk = Chunk(p);
            bool glass = BlockInfo.IsTransparent(b.Material);
            Surface surf = Shapes.SurfaceOf(b.Material);
            float jitter = 1f + ((Hash.Cell(p, 3) % 1000) / 1000f * 0.1f - 0.06f) * _o.Mottle;
            // Glyph stone is black, with the glyph inlaid as thin glowing lines.
            bool glyph = b.Material == Material.Glyph;
            Rgb body = glyph ? b.Color.Lerp(new Rgb(0x1a, 0x1b, 0x22), 0.88f) : b.Color;
            if (glyph) surf = Surface.Matte;
            for (int f = 0; f < 6; f++)
            {
                Int3 n = p + Dirs.All[f].Vec();
                var nb = _g.Get(n);
                // (A round thing's rim piece whose flat side is this whole face covers it as a block would.)
                bool rim = f < 4 && BlockInfo.IsPlanCut(nb.Type) && Shapes.Covers(nb, Dirs.All[f].Opposite(), (0f, 1f));
                if (glass ? (!nb.IsAir && nb.Type == BlockType.Cube && (nb.Material == b.Material || !BlockInfo.IsTransparent(nb.Material))) || IsRock(n) && Hides(n) : Hides(n) || rim)
                    continue;
                // The top of a floor laid with a finish wears the finish, painted on (tiles, boards, carpet).
                if (f == 4 && Painted(p) is Block paint)
                {
                    Surface ps = Shapes.SurfaceOf(paint.Material);
                    float pc = FaceCode(PaintedLike(p, paint), paint, Normals[f], false, ps);
                    Face(chunk, ps, p, f, paint.Color, 1f, pc != 0f ? 1f : jitter, ao: true, pc);
                }
                else
                {
                    float code = FaceCode(p, b, Normals[f], false, surf);
                    // A finished face keeps its colour even: the shader's own variation does the work the mottle did.
                    Face(chunk, surf, p, f, body, glass ? 0.35f : 1f, code != 0f ? 1f : jitter, ao: surf != Surface.Glow, code);
                }
                if (glyph) Glyph(chunk, p, f, b.Color);
                if (b.Type == BlockType.Breakable && f < 4) Glyph(chunk, p, f, b.Color.Scale(0.35f), Surface.Matte, crack: true);
                if (_o.Collision && (!CollidesFull(n) || IsRock(n))) CollideFace(chunk, p, f);
            }
        }

        /// <summary>A glyph on one face: one of a few line figures, picked by the cell.</summary>
        void Glyph(ChunkMesh chunk, Int3 p, int f, Rgb color, Surface surface = Surface.Glow, bool crack = false)
        {
            const float t = 0.07f, lift = 0.012f;
            uint h = Hash.Cell(p, (uint)(41 + f));
            var lines = new List<(float u0, float v0, float u1, float v1)>(3);
            if (crack)
            {
                // Cracks: thin jagged strokes, a tell that the wall is weak here.
                float u = 0.2f + (h % 7) / 12f, v = 0.1f;
                for (int k = 0; k < 4; k++)
                {
                    float du = ((int)((h >> (k * 3)) % 5) - 2) * 0.06f, dv = 0.18f + (int)((h >> (k * 2 + 1)) % 3) * 0.04f; // signed: an unsigned wrap here once threw strokes 250,000 km out
                    lines.Add((Math.Min(u, u + du), v, Math.Max(u, u + du) + 0.035f, v + dv));
                    u += du;
                    v += dv;
                }
                lines.Add((0.55f, 0.6f, 0.82f, 0.635f));
            }
            else if (f >= 4)
            {
                // Floors and roofs: a square frame.
                lines.Add((0.2f, 0.2f, 0.8f, 0.2f + t)); lines.Add((0.2f, 0.8f - t, 0.8f, 0.8f));
                lines.Add((0.2f, 0.2f, 0.2f + t, 0.8f)); lines.Add((0.8f - t, 0.2f, 0.8f, 0.8f));
            }
            else switch (h % 5)
            {
                case 0: lines.Add((0.5f - t / 2, 0.1f, 0.5f + t / 2, 0.9f)); break;                               // a stroke
                case 1: lines.Add((0.15f, 0.62f, 0.85f, 0.62f + t)); lines.Add((0.15f, 0.32f, 0.6f, 0.32f + t)); break; // two bars
                case 2: lines.Add((0.3f, 0.15f, 0.3f + t, 0.85f)); lines.Add((0.3f, 0.15f, 0.75f, 0.15f + t)); break;   // an L
                case 3: lines.Add((0.2f, 0.5f, 0.8f, 0.5f + t)); lines.Add((0.5f - t / 2, 0.5f, 0.5f + t / 2, 0.9f)); break; // a T
                default: lines.Add((0.25f, 0.25f, 0.75f, 0.25f + t)); lines.Add((0.75f - t, 0.25f, 0.75f, 0.75f)); lines.Add((0.4f, 0.75f - t, 0.75f, 0.75f)); break;
            }
            var buf = chunk.Surfaces[(int)surface];
            V3 o = V3.Of(p), n = Normals[f];
            V3 At(float u, float v) => o + f switch
            {
                0 => new V3(u, v, -lift),
                2 => new V3(u, v, 1 + lift),
                1 => new V3(1 + lift, v, u),
                3 => new V3(-lift, v, u),
                4 => new V3(u, 1 + lift, v),
                _ => new V3(u, -lift, v),
            };
            foreach (var (u0, v0, u1, v1) in lines)
            {
                V3 a = At(u0, v0), b = At(u1, v0), c = At(u1, v1), d = At(u0, v1);
                // Wind it so it faces outward, then emit clockwise for Godot.
                if (V3.Dot(V3.Cross(b - a, c - a), n) < 0) (b, d) = (d, b);
                int i0 = buf.VertexCount;
                foreach (V3 v in new[] { a, b, c, d }) buf.Vertex(v, n, color.R / 255f, color.G / 255f, color.B / 255f, 1f);
                buf.Indices.AddRange(new[] { i0, i0 + 2, i0 + 1, i0, i0 + 3, i0 + 2 });
            }
        }

        public void Rock(Int3 p)
        {
            var chunk = Chunk(p);
            float jitter = 0.95f + (Hash.Cell(p, 9) % 1000) / 1000f * 0.08f;
            for (int f = 0; f < 6; f++)
            {
                Int3 n = p + Dirs.All[f].Vec();
                if (Occludes(n)) continue;
                // Don't wall off the plug's outside: beyond it is more ground.
                if (!InRockRect(n) || n.Y < _o.RockBottom) continue;
                bool top = f == 4 && n.Y == 0;
                Rgb c = top ? _o.GroundColorAt?.Invoke(p.X, p.Z) ?? _o.GroundColor : _o.RockColor;
                Face(chunk, Surface.Matte, p, f, c, 1f, top ? 1f : jitter, ao: true, PlainCode(top ? Finish.Soil : Finish.RockFace, damp: p.Y < 0));
                if (_o.Collision) CollideFace(chunk, p, f);
            }
        }

        /// <summary>One cube face with per-corner ambient occlusion.</summary>
        void Face(ChunkMesh chunk, Surface surf, Int3 p, int f, Rgb color, float alpha, float jitter, bool ao, float code = 0f)
        {
            var buf = chunk.Surfaces[(int)surf];
            V3 n = Normals[f];
            Corners(f, out V3 c0, out V3 c1, out V3 c2, out V3 c3);
            V3 o = V3.Of(p);
            float shade = (surf == Surface.Glow ? 1f : FaceShade[f]) * jitter;
            int i0 = buf.VertexCount;
            buf.Grid.Add(i0);
            Int3 outN = p + Dirs.All[f].Vec();
            float a0 = Corner(c0), a1 = Corner(c1), a2 = Corner(c2), a3 = Corner(c3);

            float Corner(V3 corner)
            {
                float a = 1f;
                if (ao)
                {
                    // Sample the three cells around this corner in the layer the face looks into.
                    Int3 s1 = Side(f, corner, 0), s2 = Side(f, corner, 1);
                    bool b1 = Occludes(outN + s1), b2 = Occludes(outN + s2), b3 = Occludes(outN + s1 + s2);
                    int level = b1 && b2 ? 3 : (b1 ? 1 : 0) + (b2 ? 1 : 0) + (b3 ? 1 : 0);
                    a = AoLevels[level];
                }
                float k = shade * a;
                buf.Vertex(o + corner, n, color.R / 255f * k, color.G / 255f * k, color.B / 255f * k, alpha, code, _seed);
                return a;
            }

            // Flip the diagonal so occlusion interpolates without a crease.
            var idx = buf.Indices;
            if (a0 + a2 >= a1 + a3)
            {
                idx.Add(i0); idx.Add(i0 + 2); idx.Add(i0 + 1); idx.Add(i0); idx.Add(i0 + 3); idx.Add(i0 + 2);
            }
            else
            {
                idx.Add(i0 + 1); idx.Add(i0 + 3); idx.Add(i0 + 2); idx.Add(i0 + 1); idx.Add(i0); idx.Add(i0 + 3);
            }
            chunk.Quads++;
        }

        /// <summary>Unit offsets along the two face axes, toward the side of the face this corner is on.</summary>
        static Int3 Side(int f, V3 c, int axis)
        {
            // Face axes: for N/S faces x and y; E/W z and y; top/bottom x and z.
            switch (f)
            {
                case 0: case 2: return axis == 0 ? new Int3(c.X > 0.5f ? 1 : -1, 0, 0) : new Int3(0, c.Y > 0.5f ? 1 : -1, 0);
                case 1: case 3: return axis == 0 ? new Int3(0, 0, c.Z > 0.5f ? 1 : -1) : new Int3(0, c.Y > 0.5f ? 1 : -1, 0);
                default: return axis == 0 ? new Int3(c.X > 0.5f ? 1 : -1, 0, 0) : new Int3(0, 0, c.Z > 0.5f ? 1 : -1);
            }
        }

        /// <summary>Corners of a unit-cube face, counter-clockwise seen from outside. Godot's front faces are
        /// clockwise, so every quad is emitted as (0, 2, 1) and (0, 3, 2).</summary>
        static void Corners(int f, out V3 a, out V3 b, out V3 c, out V3 d)
        {
            switch (f)
            {
                case 0: a = new(1, 0, 0); b = new(0, 0, 0); c = new(0, 1, 0); d = new(1, 1, 0); break;   // north (-z)
                case 1: a = new(1, 0, 1); b = new(1, 0, 0); c = new(1, 1, 0); d = new(1, 1, 1); break;   // east (+x)
                case 2: a = new(0, 0, 1); b = new(1, 0, 1); c = new(1, 1, 1); d = new(0, 1, 1); break;   // south (+z)
                case 3: a = new(0, 0, 0); b = new(0, 0, 1); c = new(0, 1, 1); d = new(0, 1, 0); break;   // west (-x)
                case 4: a = new(0, 1, 1); b = new(1, 1, 1); c = new(1, 1, 0); d = new(0, 1, 0); break;   // top
                default: a = new(0, 0, 0); b = new(1, 0, 0); c = new(1, 0, 1); d = new(0, 0, 1); break;  // bottom
            }
        }

        /// <summary>A whole cell face that collides: kept until the chunk is done, then merged with its coplanar neighbours (<see cref="MergeFaces"/>).</summary>
        void CollideFace(ChunkMesh chunk, Int3 p, int f)
        {
            if (!_faces.TryGetValue(chunk, out var list)) _faces[chunk] = list = new List<(int, Int3)>();
            list.Add((f, p));
        }

        readonly Dictionary<ChunkMesh, List<(int F, Int3 P)>> _faces = new();

        /// <summary>The axis a face looks along (z, x, z, x, y, y), and the two it spans.</summary>
        static readonly int[] FaceAxis = { 2, 0, 2, 0, 1, 1 };

        /// <summary>
        /// Every chunk's colliding cell faces as the fewest rectangles: those facing the same way in one plane are merged
        /// greedily, so a wall or a floor is a few triangles, not two a cell (the physics builds each chunk's shape on the main
        /// thread, at a cost that grows with its triangles).
        /// </summary>
        public void MergeFaces()
        {
            foreach (var (chunk, faces) in _faces)
            {
                foreach (var plane in faces.GroupBy(q => (q.F, Of(q.P, FaceAxis[q.F]))))
                {
                    int f = plane.Key.F, n = FaceAxis[f], ua = n == 0 ? 1 : 0, va = n == 2 ? 1 : 2;
                    var open = new HashSet<(int U, int V)>(plane.Select(q => (Of(q.P, ua), Of(q.P, va))));
                    foreach (var start in open.OrderBy(c => c.V).ThenBy(c => c.U).ToList())
                    {
                        if (!open.Contains(start)) continue;
                        int u1 = start.U, v1 = start.V;
                        while (open.Contains((u1 + 1, start.V))) u1++;
                        bool Row(int v) { for (int u = start.U; u <= u1; u++) if (!open.Contains((u, v))) return false; return true; }
                        while (Row(v1 + 1)) v1++;
                        for (int v = start.V; v <= v1; v++)
                            for (int u = start.U; u <= u1; u++) open.Remove((u, v));
                        var lo = new int[3];
                        var hi = new int[3];
                        lo[n] = plane.Key.Item2; hi[n] = plane.Key.Item2 + 1;
                        lo[ua] = start.U; hi[ua] = u1 + 1;
                        lo[va] = start.V; hi[va] = v1 + 1;
                        BoxFace(f, new V3(lo[0], lo[1], lo[2]), new V3(hi[0], hi[1], hi[2]), out V3 a, out V3 b, out V3 c, out V3 d);
                        Tri(chunk, a, b, c);
                        Tri(chunk, a, c, d);
                    }
                }
            }
            _faces.Clear();
        }

        static int Of(Int3 p, int axis) => axis == 0 ? p.X : axis == 1 ? p.Y : p.Z;

        static void Tri(ChunkMesh chunk, V3 a, V3 b, V3 c)
        {
            var l = chunk.Collision;
            l.Add(a.X); l.Add(a.Y); l.Add(a.Z);
            l.Add(b.X); l.Add(b.Y); l.Add(b.Z);
            l.Add(c.X); l.Add(c.Y); l.Add(c.Z);
        }

        void CollideBox(ChunkMesh chunk, V3 lo, V3 hi, Func<int, bool>? shut = null)
        {
            for (int f = 0; f < 6; f++)
            {
                if (shut != null && shut(f)) continue;
                BoxFace(f, lo, hi, out V3 a, out V3 b, out V3 c, out V3 d);
                Tri(chunk, a, b, c);
                Tri(chunk, a, c, d);
            }
        }

        static void BoxFace(int f, V3 lo, V3 hi, out V3 a, out V3 b, out V3 c, out V3 d)
        {
            Corners(f, out V3 ua, out V3 ub, out V3 uc, out V3 ud);
            V3 L(V3 u) => new(lo.X + (hi.X - lo.X) * u.X, lo.Y + (hi.Y - lo.Y) * u.Y, lo.Z + (hi.Z - lo.Z) * u.Z);
            a = L(ua); b = L(ub); c = L(uc); d = L(ud);
        }

        void AddBox(ChunkMesh chunk, Int3 p, ShapeBox box, Block b)
        {
            var buf = chunk.Surfaces[(int)box.Surface];
            V3 o = V3.Of(p);
            float alpha = box.Surface == Surface.Glass ? 0.4f : 1f;
            bool tilted = box.Tilt != default;
            V3 mid = (box.Lo + box.Hi) * 0.5f;
            // A box is finished as what it is made of: a crate's boards, a machine's plate, a slab's concrete; a pane is framed glass.
            float side = float.NaN, top = float.NaN, under = float.NaN;
            bool shaped = BlockInfo.IsShaped(b.Type) && !tilted;
            for (int f = 0; f < 6; f++)
            {
                if (box.SkipBottom && f == 5) continue;
                if (shaped && Shut(p, b, box.Lo, box.Hi, f, Hides)) continue;
                BoxFace(f, o + box.Lo, o + box.Hi, out V3 a, out V3 bb, out V3 c, out V3 d);
                V3 n = Normals[f];
                float k = box.Surface == Surface.Glow ? 1f : FaceShade[f];
                if (tilted)
                {
                    // Turned about its own middle: corners and normal alike, and shaded by where the face now looks.
                    V3 centre = o + mid;
                    a = centre + Shapes.Rotate(a - centre, box.Tilt); bb = centre + Shapes.Rotate(bb - centre, box.Tilt);
                    c = centre + Shapes.Rotate(c - centre, box.Tilt); d = centre + Shapes.Rotate(d - centre, box.Tilt);
                    n = Shapes.Rotate(n, box.Tilt);
                    if (box.Surface != Surface.Glow) k = n.Y >= 0 ? 0.86f + 0.14f * n.Y : 0.86f + 0.2f * n.Y;
                }
                float r = box.Color.R / 255f * k, g = box.Color.G / 255f * k, bl = box.Color.B / 255f * k;
                float code;
                if (box.Surface == Surface.Glass && b.Type == BlockType.Pane && !tilted) code = FaceCode(p, b, n, false, Surface.Glass);
                else if (n.Y > 0.5f) code = float.IsNaN(top) ? top = BoxCode(p, b, box.Surface, FaceSide.Roof) : top;
                else if (n.Y < -0.5f) code = float.IsNaN(under) ? under = BoxCode(p, b, box.Surface, FaceSide.Soffit) : under;
                else code = float.IsNaN(side) ? side = BoxCode(p, b, box.Surface, FaceSide.Out) : side;
                int i0 = buf.VertexCount;
                buf.Vertex(a, n, r, g, bl, alpha, code, _seed);
                buf.Vertex(bb, n, r, g, bl, alpha, code, _seed);
                buf.Vertex(c, n, r, g, bl, alpha, code, _seed);
                buf.Vertex(d, n, r, g, bl, alpha, code, _seed);
                var idx = buf.Indices;
                idx.Add(i0); idx.Add(i0 + 2); idx.Add(i0 + 1); idx.Add(i0); idx.Add(i0 + 3); idx.Add(i0 + 2);
                chunk.Quads++;
            }
        }

        /// <summary>A prop's or a small part's face code: its material's finish that way, at the place's wear.</summary>
        float BoxCode(Int3 p, Block b, Surface surf, FaceSide side)
        {
            if (!Finishes || surf is Surface.Glow or Surface.Flesh) return 0f;
            if (surf == Surface.Glass) return PlainCode(Finish.Glass);
            FinishPick pick = Pick(b, side, false, 0);
            if (pick.Finish == Finish.Plain) return 0f;
            float j = Hash.Cell(p, 0x3EA2u) % 1000 / 1000f;
            int wear = Math.Clamp((int)MathF.Floor(_look.Wear * 3f + (j - 0.5f) * 1.2f + 0.35f), 0, 3);
            bool painted = Surfaces.Coated(pick.Finish) && pick.Finish != Finish.Precast && Surfaces.Painted(b.Color, b.Material);
            return Surfaces.Code(pick.Finish, _look.ParamOf(pick), wear, 3, false, p.Y < 0, 0, painted, Surfaces.Under.None, 0);
        }

        void Ramp(ChunkMesh chunk, Int3 p, Block b, bool draw)
        {
            // Built rising toward north, then turned to face the block's direction; a chamfer is the same hung upside down.
            V3 o = V3.Of(p);
            bool hung = b.Type == BlockType.Chamfer;
            V3 R(V3 v) => o + Shapes.Turn(hung ? new V3(v.X, 1f - v.Y, v.Z) : v, b.Facing);
            V3 lowL = new(0, 0, 1), lowR = new(1, 0, 1), highL = new(0, 1, 0), highR = new(1, 1, 0), backL = new(0, 0, 0), backR = new(1, 0, 0);
            Surface rs = Shapes.SurfaceOf(b.Material);
            var buf = chunk.Surfaces[(int)rs];
            float j = 1f + ((Hash.Cell(p, 5) % 100) / 1000f - 0.05f) * _o.Mottle;
            V3 sLL = R(lowL), sLR = R(lowR), sHL = R(highL), sHR = R(highR), sBL = R(backL), sBR = R(backR);
            // The slope, its back and its sides are the roof's covering (a verge shows its edge); the underside an eave or a ceiling.
            float slopeCode = draw ? FaceCode(p, b, V3.Cross(sLR - sLL, sHR - sLL).Normalized() is var sn && sn.Y < 0 ? sn * -1f : V3.Cross(sLR - sLL, sHR - sLL).Normalized(), true, rs) : 0f;
            if (slopeCode != 0f) j = 1f;
            void Quad(V3 a, V3 bb, V3 c, V3 d, float shade, float code)
            {
                if (hung) (bb, d) = (d, bb); // mirrored, so wound the other way round
                V3 n = V3.Cross(bb - a, c - a).Normalized();
                int i0 = buf.VertexCount;
                foreach (V3 v in new[] { a, bb, c, d }) buf.Vertex(v, n, b.Color.R / 255f * shade * j, b.Color.G / 255f * shade * j, b.Color.B / 255f * shade * j, 1f, code, _seed);
                buf.Indices.AddRange(new[] { i0, i0 + 2, i0 + 1, i0, i0 + 3, i0 + 2 });
                chunk.Quads++;
            }
            void TriFace(V3 a, V3 bb, V3 c, float shade, float code)
            {
                if (hung) (bb, c) = (c, bb);
                V3 n = V3.Cross(bb - a, c - a).Normalized();
                int i0 = buf.VertexCount;
                foreach (V3 v in new[] { a, bb, c }) buf.Vertex(v, n, b.Color.R / 255f * shade * j, b.Color.G / 255f * shade * j, b.Color.B / 255f * shade * j, 1f, code, _seed);
                buf.Indices.AddRange(new[] { i0, i0 + 2, i0 + 1 });
            }
            if (draw)
            {
                Quad(sLL, sLR, sHR, sHL, 0.97f, slopeCode);                  // the slope
                // The tall back and the sides, where nothing whole hides them (a chamfer or a cove in a wall is against more wall).
                if (!Hides(p + b.Facing.Vec())) Quad(sBR, sBL, sHL, sHR, 0.82f, slopeCode);
                if (!Hides(p + b.Facing.RotateCw(3).Vec())) TriFace(sBL, sLL, sHL, 0.78f, slopeCode);
                if (!Hides(p + b.Facing.RotateCw(1).Vec())) TriFace(sLR, sBR, sHR, 0.78f, slopeCode);
                // The underside, for a ramp nothing stands under: an eave or a roof seen from inside (a chamfer's top, under what it hangs from).
                if (!Hides(p + (hung ? Int3.Up : Int3.Down))) Quad(sBL, sBR, sLR, sLL, 0.62f, FaceCode(p, b, new V3(0, hung ? 1 : -1, 0), false, rs));
            }
            if (_o.Collision)
            {
                void T(V3 a, V3 bb, V3 c) { if (hung) Tri(chunk, a, c, bb); else Tri(chunk, a, bb, c); }
                T(sLL, sLR, sHR); T(sLL, sHR, sHL);
                T(sBR, sBL, sHL); T(sBR, sHL, sHR);
                T(sBL, sLL, sHL); T(sLR, sBR, sHR);
            }
        }

        /// <summary>
        /// The player's corner slopes, built in the cell's own frame with north at z 0 and turned to the block's
        /// facing like a ramp. Corner: the tetrahedron under one raised corner (north-west, unturned). Inverted
        /// corner: the cube less the tetrahedron at the opposite top corner. A face on the cell's boundary is
        /// left out where a full neighbour hides it.
        /// </summary>
        void CornerSlope(ChunkMesh chunk, Int3 p, Block b, bool draw)
        {
            V3 o = V3.Of(p);
            // A chamfer's corners are the same hung upside down.
            bool hung = BlockInfo.IsHung(b.Type), outer = b.Type is BlockType.Corner or BlockType.ChamferCorner;
            V3 R(float x, float y, float z) => o + Shapes.Turn(new V3(x, hung ? 1f - y : y, z), b.Facing);
            Surface cs = Shapes.SurfaceOf(b.Material);
            var buf = chunk.Surfaces[(int)cs];
            float j = 1f + ((Hash.Cell(p, 5) % 100) / 1000f - 0.05f) * _o.Mottle;
            V3 centre = outer ? R(0.25f, 0.25f, 0.25f) : R(0.45f, 0.45f, 0.45f);
            void Face(V3[] pts, Int3? side, float shade)
            {
                if (hung && side is { } sv && sv.X == 0 && sv.Z == 0) side = sv * -1;
                // Left out where a neighbour covers it: drawn where it hides it, collided where it fills its cell.
                bool shown = draw && !(side is { } s && Hides(p + s)), solid = _o.Collision && !(side is { } s2 && CollidesFull(p + s2));
                if (!shown && !solid) return;
                V3 n = V3.Cross(pts[1] - pts[0], pts[2] - pts[0]).Normalized();
                // Wound to face away from the piece's middle, whatever the turn did.
                if (V3.Dot(n, pts[0] - centre) < 0) { Array.Reverse(pts); n = V3.Cross(pts[1] - pts[0], pts[2] - pts[0]).Normalized(); }
                if (shown)
                {
                    // Every face but the underside is the roof's covering, as on a ramp.
                    float code = FaceCode(p, b, n.Y < -0.5f ? n : new V3(0, 1, 0), n.Y > -0.5f, cs);
                    float jj = code != 0f ? 1f : j;
                    int i0 = buf.VertexCount;
                    foreach (V3 v in pts) buf.Vertex(v, n, b.Color.R / 255f * shade * jj, b.Color.G / 255f * shade * jj, b.Color.B / 255f * shade * jj, 1f, code, _seed);
                    for (int k = 1; k + 1 < pts.Length; k++) buf.Indices.AddRange(new[] { i0, i0 + k + 1, i0 + k });
                    chunk.Quads++;
                }
                if (solid)
                    for (int k = 1; k + 1 < pts.Length; k++) Tri(chunk, pts[0], pts[k], pts[k + 1]);
            }
            Int3 Side(float x, float z) => Shapes.Turn(new V3(x, 0.5f, z), b.Facing) is var v ? new Int3(v.X < 0.01f ? -1 : v.X > 0.99f ? 1 : 0, 0, v.Z < 0.01f ? -1 : v.Z > 0.99f ? 1 : 0) : default;
            if (outer)
            {
                V3 a = R(0, 0, 0), bb = R(1, 0, 0), c = R(0, 0, 1), apex = R(0, 1, 0);
                Face(new[] { a, bb, c }, Int3.Down, 0.62f);
                Face(new[] { a, bb, apex }, null, 0.82f);
                Face(new[] { a, c, apex }, null, 0.78f);
                Face(new[] { bb, c, apex }, null, 0.97f);
            }
            else
            {
                V3 a = R(0, 0, 0), bb = R(1, 0, 0), c = R(0, 0, 1), d = R(1, 0, 1), e = R(0, 1, 0), f = R(1, 1, 0), g = R(0, 1, 1);
                Face(new[] { a, bb, d, c }, Int3.Down, 0.62f);
                Face(new[] { a, bb, f, e }, Side(0.5f, 0f), 0.82f);
                Face(new[] { a, c, g, e }, Side(0f, 0.5f), 0.78f);
                Face(new[] { bb, d, f }, null, 0.8f);
                Face(new[] { c, d, g }, null, 0.8f);
                Face(new[] { e, f, g }, Int3.Up, 1f);
                Face(new[] { f, g, d }, null, 0.97f);
            }
        }

        /// <summary>
        /// A round thing's rim piece: its plan (<see cref="Shapes.Plan"/>) stood up the full height of its cell. Its top
        /// and bottom are left out under or over the same piece or a full block; a side on its cell's face where the
        /// neighbour covers it (a full block, or a piece whose face there takes in this one's); the cut always shows.
        /// It collides as drawn, less the sides a full neighbour fills.
        /// </summary>
        void Prism(ChunkMesh chunk, Int3 p, Block b, bool draw)
        {
            V3[] plan = Shapes.Plan(b);
            V3 o = V3.Of(p);
            Surface s = Shapes.SurfaceOf(b.Material);
            var buf = chunk.Surfaces[(int)s];
            float j = 1f + ((Hash.Cell(p, 5) % 100) / 1000f - 0.05f) * _o.Mottle;
            void Face(V3[] pts, V3 n, bool shown, bool solid, float shade)
            {
                if (shown)
                {
                    float code = FaceCode(p, b, n, false, s), k = shade * (code != 0f ? 1f : j);
                    int i0 = buf.VertexCount;
                    foreach (V3 v in pts) buf.Vertex(o + v, n, b.Color.R / 255f * k, b.Color.G / 255f * k, b.Color.B / 255f * k, 1f, code, _seed);
                    for (int i = 1; i + 1 < pts.Length; i++) buf.Indices.AddRange(new[] { i0, i0 + i + 1, i0 + i });
                    chunk.Quads++;
                }
                if (solid && _o.Collision)
                    for (int i = 1; i + 1 < pts.Length; i++) Tri(chunk, o + pts[0], o + pts[i], o + pts[i + 1]);
            }
            // The ends: the plan as wound (anticlockwise seen from below), at the bottom and, turned over, at the top.
            foreach (bool top in new[] { true, false })
            {
                Int3 n = p + (top ? Int3.Up : Int3.Down);
                Block nb = _g.Get(n);
                bool same = nb.Type == b.Type && nb.Facing == b.Facing && !BlockInfo.IsTransparent(nb.Material);
                bool shown = draw && !Hides(n) && !same, solid = !CollidesFull(n) && !same;
                if (!shown && !solid) continue;
                var pts = plan.Select(v => v with { Y = top ? 1f : 0f }).ToArray();
                if (top) Array.Reverse(pts, 1, pts.Length - 1); // (the first corner kept first: the fan's, which sees every edge)
                Face(pts, new V3(0, top ? 1 : -1, 0), shown, solid, top ? 1f : 0.66f);
            }
            // The sides: each edge of the plan, a wall from the bottom to the top. Its collision is one wall the height of the
            // run of this piece stacked over it (a tank's rim is the same piece course on course), from the run's foot.
            bool Same(Int3 q) { Block o = _g.Get(q); return o.Type == b.Type && o.Facing == b.Facing; }
            for (int i = 0; i < plan.Length; i++)
            {
                V3 a = plan[i], c = plan[(i + 1) % plan.Length];
                V3 n = new V3(c.Z - a.Z, 0, a.X - c.X).Normalized();
                Dir? side = Shapes.OnSide(a, c);
                bool Open(Int3 q, bool solid) => side is not Dir d
                    || !(solid ? CollidesFull(q + d.Vec()) : Hides(q + d.Vec())) && !Shapes.Covers(_g.Get(q + d.Vec()), d.Opposite(), Shapes.Span(a, c, d));
                bool shown = draw && Open(p, false), solid = _o.Collision && Open(p, true) && !(Same(p + Int3.Down) && Open(p + Int3.Down, true));
                int run = 1;
                while (solid && Same(p + Int3.Up * run) && Open(p + Int3.Up * run, true)) run++;
                float shade = 0.8f + 0.14f * MathF.Max(0f, n.Z) + 0.1f * MathF.Max(0f, -n.Z) + 0.04f * MathF.Max(0f, n.X);
                if (shown) Face(new[] { a, a with { Y = 1 }, c with { Y = 1 }, c }, n, true, false, shade);
                if (solid) Face(new[] { a, a with { Y = run }, c with { Y = run }, c }, n, false, true, shade);
            }
        }

        void Spike(ChunkMesh chunk, Int3 p, Block b)
        {
            // (Finished as what it is made of, as any box of its material is: never a flat colour.)
            Surface surf = Shapes.SurfaceOf(b.Material);
            var buf = chunk.Surfaces[(int)surf];
            float code = BoxCode(p, b, surf, FaceSide.Out);
            V3 o = V3.Of(p);
            V3 apex = o + new V3(0.5f, 1.25f, 0.5f);
            V3[] bs = { o + new V3(0.18f, 0, 0.18f), o + new V3(0.82f, 0, 0.18f), o + new V3(0.82f, 0, 0.82f), o + new V3(0.18f, 0, 0.82f) };
            for (int i = 0; i < 4; i++)
            {
                V3 a = bs[i], c = bs[(i + 1) % 4];
                V3 n = V3.Cross(apex - a, c - a).Normalized();
                float k = 0.75f + 0.25f * Math.Max(0f, n.Y + 0.3f) + (i % 2) * 0.08f;
                int i0 = buf.VertexCount;
                buf.Vertex(a, n, b.Color.R / 255f * k, b.Color.G / 255f * k, b.Color.B / 255f * k, 1f, code, _seed);
                buf.Vertex(c, n, b.Color.R / 255f * k, b.Color.G / 255f * k, b.Color.B / 255f * k, 1f, code, _seed);
                buf.Vertex(apex, n, b.Color.R / 255f * k * 1.1f, b.Color.G / 255f * k * 1.1f, b.Color.B / 255f * k * 1.1f, 1f, code, _seed);
                buf.Indices.AddRange(new[] { i0, i0 + 1, i0 + 2 });
            }
        }
    }
}

/// <summary>
/// One chunk's box of a grid, read through to the whole grid, so the mesher builds that chunk alone
/// with its neighbours' faces and shading seen exactly as in the whole build.
/// </summary>
public sealed class ChunkWindow : IBlockStructure
{
    readonly IBlockStructure _grid;
    public Int3 Min { get; }
    public Int3 Size { get; }

    public ChunkWindow(IBlockStructure grid, Int3 min, int size)
    {
        _grid = grid;
        Min = min;
        Size = new Int3(size, size, size);
    }

    public bool InBounds(Int3 p)
    {
        Int3 l = p - Min;
        return (uint)l.X < (uint)Size.X && (uint)l.Y < (uint)Size.Y && (uint)l.Z < (uint)Size.Z;
    }

    public Block Get(Int3 p) => _grid.Get(p);
    public void Set(Int3 p, Block b) => _grid.Set(p, b);
    public Block Under(Int3 p) => _grid.Under(p);
    public int CountBlocks() => 0;

    /// <summary>The look of the grid it reads through.</summary>
    public PlaceLook? Look => _grid is BlockGrid g ? g.Look : _grid is ChunkWindow w ? w.Look : null;
}

/// <summary>A box of a shape in cell units; <paramref name="Tilt"/> (pitch, yaw, roll, radians) turns it about its own middle (a piece of rubble lying as it landed).</summary>
public readonly record struct ShapeBox(V3 Lo, V3 Hi, Rgb Color, Surface Surface, bool SkipBottom = false, V3 Tilt = default);

/// <summary>What each non-cube block looks like: a few boxes in cell-local units (0 to 1).</summary>
public static class Shapes
{
    /// <summary>A floor finish's top over the floor it is painted on, m: none, it is paint (Mesher.Ctx.Painted).</summary>
    public const float PlateTop = 0f;

    public static readonly Rgb ScreenOn = new(0x7c, 0xff, 0xb0);
    public static readonly Rgb ScreenDim = new(0x2a, 0x6a, 0x48);

    public static Surface SurfaceOf(Material m) => m switch
    {
        Material.Lumen or Material.Glyph or Material.Crystal => Surface.Glow,
        Material.Glass => Surface.Glass,
        Material.Steel or Material.Alloy or Material.Bronze or Material.Scrap => Surface.Metal,
        _ => Surface.Matte,
    };

    /// <summary>Turns a vector by a tilt: roll about z, then pitch about x, then yaw about y (radians).</summary>
    public static V3 Rotate(V3 v, V3 t)
    {
        float cz = MathF.Cos(t.Z), sz = MathF.Sin(t.Z);
        v = new V3(v.X * cz - v.Y * sz, v.X * sz + v.Y * cz, v.Z);
        float cx = MathF.Cos(t.X), sx = MathF.Sin(t.X);
        v = new V3(v.X, v.Y * cx - v.Z * sx, v.Y * sx + v.Z * cx);
        float cy = MathF.Cos(t.Y), sy = MathF.Sin(t.Y);
        return new V3(v.X * cy + v.Z * sy, v.Y, -v.X * sy + v.Z * cy);
    }

    /// <summary>Rotates a cell-local point from north-facing to the given facing.</summary>
    public static V3 Turn(V3 v, Dir facing)
    {
        int q = facing switch { Dir.East => 1, Dir.South => 2, Dir.West => 3, _ => 0 };
        for (int i = 0; i < q; i++) v = new V3(1f - v.Z, v.Y, v.X);
        return v;
    }

    static ShapeBox Box(float x0, float y0, float z0, float x1, float y1, float z1, Rgb c, Surface s, Dir facing = Dir.North)
    {
        V3 a = Turn(new V3(x0, y0, z0), facing), b = Turn(new V3(x1, y1, z1), facing);
        return new ShapeBox(new V3(MathF.Min(a.X, b.X), MathF.Min(a.Y, b.Y), MathF.Min(a.Z, b.Z)),
            new V3(MathF.Max(a.X, b.X), MathF.Max(a.Y, b.Y), MathF.Max(a.Z, b.Z)), c, s);
    }

    /// <summary>Whether a pane's sheet runs along x (its facing is north or south, across z) rather than along z; set when the place was laid out (<see cref="PaneFacing"/>).</summary>
    public static bool PaneAlongX(Block b) => b.Facing is Dir.North or Dir.South;

    /// <summary>A pane with another of its wall's panes in front of it (a wall two cells thick has glass in both): the outer one is the sheet, so a window is one plane.</summary>
    public static bool PaneHidden(Block b, Int3 p, IBlockStructure g)
    {
        if (!b.Facing.IsHorizontal() || b.Role == Role.Prop) return false;
        Block front = g.Get(p + b.Facing.Vec());
        return front.Type == BlockType.Pane && front.Role != Role.Prop && PaneAlongX(front) == PaneAlongX(b);
    }

    public static (V3 lo, V3 hi) CollisionBox(Block b) => b.Type switch
    {
        BlockType.Pillar => (new V3(0.3f, 0, 0.3f), new V3(0.7f, 1, 0.7f)),
        BlockType.Rib => (new V3(0.15f, 0, 0.15f), new V3(0.85f, 1, 0.85f)),
        BlockType.Pane or BlockType.Lattice or BlockType.SealedDoor or BlockType.Growth => (new V3(0, 0, 0), new V3(1, 1, 1)),
        BlockType.Spike => (new V3(0.25f, 0, 0.25f), new V3(0.75f, 1, 0.75f)),
        BlockType.Rubble => (new V3(0.05f, 0, 0.05f), new V3(0.95f, Debris.Top(b.Variant), 0.95f)),
        BlockType.Core => (new V3(0.2f, 0, 0.2f), new V3(0.8f, 1.8f, 0.8f)),
        BlockType.Rail => (new V3(0, 0, 0), new V3(1, 1.05f, 1)),
        BlockType.Shelf or BlockType.Locker => (new V3(0.06f, 0, 0.1f), new V3(0.94f, 1f, 0.9f)),
        BlockType.Statue => (new V3(0.1f, 0, 0.1f), new V3(0.9f, 1.65f, 0.9f)),
        BlockType.Bed => (new V3(0.1f, 0, 0.05f), new V3(0.9f, 0.65f, 0.95f)),
        BlockType.Pump => (new V3(0.2f, 0, 0.25f), new V3(0.8f, 1.7f, 0.75f)),
        BlockType.Pipe when b.Variant == 1 => PipeBounds(b, 0.08f, 1.92f),
        BlockType.Pipe => PipeBounds(b, 0.1f, 0.9f),
        _ => (new V3(0.1f, 0, 0.1f), new V3(0.9f, 0.9f, 0.9f)),
    };

    /// <summary>
    /// What a block collides as, in its cell's frame: for furniture and fittings the solid parts of what is
    /// drawn (a table's top and four legs with the floor open between them, a locker's body against its
    /// back, a terminal's stand and its screen, a crate where it sits in its cell), for the rest its one box
    /// (<see cref="CollisionBox"/>). The body's collision, the creatures' and the people's ways all go by these.
    /// </summary>
    public static List<(V3 lo, V3 hi)> CollisionBoxes(Block b, Int3 p)
    {
        var l = new List<(V3, V3)>(5);
        void Add(float x0, float y0, float z0, float x1, float y1, float z1, Dir facing = Dir.North)
        {
            ShapeBox s = Box(x0, y0, z0, x1, y1, z1, default, Surface.Matte, facing);
            l.Add((s.Lo, s.Hi));
        }
        Dir f = b.Facing;
        switch (b.Type)
        {
            case BlockType.Terminal:
                Add(0.15f, 0, 0.3f, 0.85f, 0.8f, 0.85f, f);
                Add(0.2f, 0.8f, 0.42f, 0.8f, 1.35f, 0.6f, f);
                break;
            case BlockType.Crate:
            {
                uint h = Hash.Cell(p, 77);
                float R(int k) => ((h >> (k * 4)) & 15) / 15f;
                float lift = b.Variant == 1 ? 0.05f : 0f, sz = 0.55f + R(0) * 0.3f, ox = (1 - sz) * R(1), oz = (1 - sz) * R(2);
                Add(ox, 0, oz, ox + sz, sz * 0.9f + lift, oz + sz);
                break;
            }
            case BlockType.Table:
            case BlockType.Meal:
                Add(0.05f, 0.7f, 0.05f, 0.95f, 0.8f, 0.95f);
                foreach (var (x, z) in new[] { (0.1f, 0.1f), (0.82f, 0.1f), (0.1f, 0.82f), (0.82f, 0.82f) })
                    Add(x, 0, z, x + 0.08f, 0.7f, z + 0.08f);
                break;
            case BlockType.Bunk:
                Add(0.05f, 0, 0.1f, 0.95f, 0.6f, 0.9f);
                break;
            case BlockType.Barricade:
                Add(0, 0, 0.3f, 1, 0.35f, 0.7f, f);
                Add(0.1f, 0.35f, 0.25f, 0.9f, 0.7f, 0.65f, f);
                Add(0.25f, 0.7f, 0.35f, 0.85f, 1.0f, 0.75f, f);
                break;
            case BlockType.Machine:
                Add(0.05f, 0, 0.05f, 0.95f, 0.95f, 0.95f);
                Add(0.0f, 0.55f, 0.15f, 1.0f, 0.65f, 0.85f);
                break;
            case BlockType.Locker:
                Add(0.12f, 0, 0.25f, 0.88f, 1.0f, 0.9f, f);
                break;
            case BlockType.Bed:
                Add(0.1f, 0, 0.05f, 0.9f, 0.55f, 0.95f, f);
                Add(0.2f, 0.55f, 0.1f, 0.8f, 0.65f, 0.3f, f);
                break;
            case BlockType.Bench:
                Add(0.04f, 0.78f, 0.1f, 0.96f, 0.88f, 0.9f);
                Add(0.08f, 0, 0.14f, 0.92f, 0.78f, 0.86f);
                break;
            case BlockType.Tank:
                Add(0.15f, 0, 0.05f, 0.85f, 0.95f, 0.95f);
                Add(0.05f, 0, 0.15f, 0.95f, 0.95f, 0.85f);
                break;
            case BlockType.Pod:
                Add(0.18f, 0, 0.18f, 0.82f, 0.45f, 0.82f);
                Add(0.24f, 0.45f, 0.24f, 0.76f, 1.05f, 0.76f);
                break;
            case BlockType.Statue:
                Add(0.1f, 0, 0.1f, 0.9f, 0.3f, 0.9f);
                Add(0.3f, 0.3f, 0.35f, 0.7f, 1.3f, 0.65f);
                Add(0.36f, 1.3f, 0.38f, 0.64f, 1.65f, 0.62f);
                Add(0.15f, 0.9f, 0.42f, 0.85f, 1.05f, 0.58f);
                break;
            case BlockType.Pump:
                Add(0.15f, 0, 0.25f, 0.85f, 0.12f, 0.75f, f);
                Add(0.22f, 0.12f, 0.3f, 0.78f, 1.25f, 0.7f, f);
                Add(0.26f, 1.25f, 0.34f, 0.74f, 1.7f, 0.66f, f);
                break;
            case BlockType.Half or BlockType.Quarter or BlockType.Partition or BlockType.Beam or BlockType.Column:
                foreach (ShapeBox s in Structural(b, default, Surface.Matte)) l.Add((s.Lo, s.Hi));
                break;
            default:
                l.Add(CollisionBox(b));
                break;
        }
        return l;
    }

    /// <summary>
    /// The places' own shaped structure (<see cref="BlockInfo.IsShaped"/>) as boxes, drawn and collided alike: a half
    /// against its face, a quarter bar along its bottom edge, a thin wall across its facing (its normal, as a pane's),
    /// a beam under the top along its axis, a round column of eight flats. A chamfer is drawn as a hung slope instead.
    /// </summary>
    public static IEnumerable<ShapeBox> Structural(Block b, Rgb c, Surface s)
    {
        Dir f = b.Facing;
        switch (b.Type)
        {
            case BlockType.Half:
                yield return f switch
                {
                    Dir.Down => new ShapeBox(new V3(0, 0, 0), new V3(1, 0.5f, 1), c, s),
                    Dir.Up => new ShapeBox(new V3(0, 0.5f, 0), new V3(1, 1, 1), c, s),
                    _ => Box(0, 0, 0, 1, 1, 0.5f, c, s, f),
                };
                break;
            case BlockType.Quarter:
                yield return Box(0, 0, 0, 1, 0.5f, 0.5f, c, s, f.IsHorizontal() ? f : Dir.North);
                break;
            case BlockType.Partition:
                yield return Box(0, 0, 0.4f, 1, 1, 0.6f, c, s, f.IsHorizontal() ? f : Dir.North);
                break;
            case BlockType.Beam:
                yield return Box(0.35f, 0.7f, 0, 0.65f, 1, 1, c, s, f.IsHorizontal() ? f : Dir.North);
                break;
            case BlockType.Column:
                yield return new ShapeBox(new V3(0.27f, 0, 0.1f), new V3(0.73f, 1, 0.9f), c, s);
                yield return new ShapeBox(new V3(0.1f, 0, 0.27f), new V3(0.9f, 1, 0.73f), c, s);
                break;
        }
    }

    /// <summary>A shaped wall, floor or ceiling (and a slope cut from one), drawn from further off as the cube it was cut from.</summary>
    public static bool FarAsCube(Block b) => (BlockInfo.IsShaped(b.Type) && b.Type is not (BlockType.Beam or BlockType.Column or BlockType.Quarter) && !BlockInfo.IsPlanCut(b.Type)
        || b.Type is BlockType.Ramp or BlockType.Corner or BlockType.InvCorner && b.Role is Role.Wall or Role.Floor or Role.Decor) && !BlockInfo.IsTransparent(b.Material);

    static readonly V3[][] Plans = MakePlans();

    /// <summary>
    /// A rim piece's plan (<see cref="BlockInfo.IsPlanCut"/>): its corners in its cell (x east, z south, 0 to 1), turned to
    /// its facing and wound anticlockwise seen from below, the first corner one that sees every edge (a quarter round's centre).
    /// </summary>
    public static V3[] Plan(Block b) => Plans[(b.Type - BlockType.Wedge) * 4 + b.Facing switch { Dir.East => 1, Dir.South => 2, Dir.West => 3, _ => 0 }];

    static V3[][] MakePlans()
    {
        static V3 P(float x, float z) => new(x, 0, z);
        const int Steps = 6; // (a quarter round's arc in six flats)
        static IEnumerable<V3> Arc(Func<float, V3> at) => Enumerable.Range(0, Steps + 1).Select(i => at(i * MathF.PI / 2 / Steps));
        var canon = new[]
        {
            new[] { P(0, 1), P(0, 0), P(1, 1) },                                // Wedge
            new[] { P(0, 1), P(0, 0.5f), P(0.5f, 1) },                          // WedgeTip
            new[] { P(0, 1), P(0, 0), P(0.5f, 0), P(1, 0.5f), P(1, 1) },        // WedgeClip
            new[] { P(1, 1), P(0, 1), P(1, 0.5f) },                             // Slant
            new[] { P(0, 1), P(0, 0.5f), P(1, 1) },                             // SlantM
            new[] { P(0, 1), P(0, 0.5f), P(1, 0), P(1, 1) },                    // SlantFill
            new[] { P(0, 1), P(0, 0), P(1, 0.5f), P(1, 1) },                    // SlantFillM
            Arc(t => P(MathF.Sin(t), 1 - MathF.Cos(t))).Prepend(P(0, 1)).ToArray(),     // RoundOut: about the south-west corner
            Arc(t => P(1 - MathF.Cos(t), MathF.Sin(t))).Prepend(P(0, 1)).ToArray(),     // RoundIn: less a round about the north-east
        };
        var all = new V3[canon.Length * 4][];
        for (int t = 0; t < canon.Length; t++)
            for (int q = 0; q < 4; q++)
            {
                var pts = canon[t].Select(v => Turn(v, Dirs.Horizontal[q])).ToArray();
                float area = 0;
                for (int i = 0; i < pts.Length; i++) area += pts[i].X * pts[(i + 1) % pts.Length].Z - pts[(i + 1) % pts.Length].X * pts[i].Z;
                if (area < 0) Array.Reverse(pts, 1, pts.Length - 1);
                all[t * 4 + q] = pts;
            }
        return all;
    }

    /// <summary>The face of its cell a plan's edge lies on, if it lies on one.</summary>
    public static Dir? OnSide(V3 a, V3 c)
    {
        const float e = 0.001f;
        if (a.Z < e && c.Z < e) return Dir.North;
        if (a.Z > 1 - e && c.Z > 1 - e) return Dir.South;
        if (a.X < e && c.X < e) return Dir.West;
        if (a.X > 1 - e && c.X > 1 - e) return Dir.East;
        return null;
    }

    /// <summary>The stretch of a cell's face an edge on it takes, along the face (x on a north or south face, z on an east or west one).</summary>
    public static (float Lo, float Hi) Span(V3 a, V3 c, Dir side) => side is Dir.North or Dir.South ? (MathF.Min(a.X, c.X), MathF.Max(a.X, c.X)) : (MathF.Min(a.Z, c.Z), MathF.Max(a.Z, c.Z));

    /// <summary>
    /// Whether a block's face on one side of its cell takes in all of a stretch of that face, the whole height of the cell: a
    /// full opaque block, a half standing against that face or across it, a rim piece whose plan runs along it there.
    /// </summary>
    public static bool Covers(Block b, Dir side, (float Lo, float Hi) need)
    {
        if (b.IsAir || BlockInfo.IsTransparent(b.Material) || PlanOf(b) is not { } plan) return false;
        const float e = 0.001f;
        for (int i = 0; i < plan.Length; i++)
        {
            V3 a = plan[i], c = plan[(i + 1) % plan.Length];
            if (OnSide(a, c) != side) continue;
            var (lo, hi) = Span(a, c, side);
            if (lo <= need.Lo + e && hi >= need.Hi - e) return true;
        }
        return false;
    }

    static readonly V3[] Square = { new(0, 0, 0), new(1, 0, 0), new(1, 0, 1), new(0, 0, 1) };

    /// <summary>A block standing the full height of its cell, in plan as <see cref="Plan"/> gives it: a whole block's square, a half standing against a face, a rim piece; null for anything else.</summary>
    public static V3[]? PlanOf(Block b)
    {
        if (BlockInfo.IsFullCube(b.Type)) return Square;
        if (BlockInfo.IsPlanCut(b.Type)) return Plan(b);
        if (b.Type != BlockType.Half || !b.Facing.IsHorizontal()) return null;
        ShapeBox box = Box(0, 0, 0, 1, 1, 0.5f, default, Surface.Matte, b.Facing);
        return new[] { new V3(box.Lo.X, 0, box.Lo.Z), new V3(box.Hi.X, 0, box.Lo.Z), new V3(box.Hi.X, 0, box.Hi.Z), new V3(box.Lo.X, 0, box.Hi.Z) };
    }

    /// <summary>Whether a block's face on one side is the whole face of its cell (something can be hung on it, or stand against it).</summary>
    public static bool FlatToward(Block b, Dir side) => b.Type switch
    {
        BlockType.Cube or BlockType.Breakable => true,
        BlockType.Half => b.Facing == side,
        BlockType.Ramp => b.Facing == side || side == Dir.Down,
        BlockType.Chamfer => b.Facing == side || side == Dir.Up,
        _ when BlockInfo.IsPlanCut(b.Type) => side.IsHorizontal() && Covers(b, side, (0f, 1f)),
        _ => false,
    };

    /// <summary>Whether a shaped block runs on into its neighbour that way, their faces meeting whole (halves side by side, a thin wall up its storey, a beam along its line).</summary>
    public static bool RunsOn(Block a, Block n, Dir d)
    {
        if (n.Type != a.Type || n.Facing != a.Facing || n.Variant != a.Variant) return false;
        bool along(Dir axis) => d == axis || d == axis.Opposite();
        return a.Type switch
        {
            BlockType.Half => !along(a.Facing),
            BlockType.Quarter => d.IsHorizontal() && !along(a.Facing),
            BlockType.Partition => !along(a.Facing),
            BlockType.Beam => along(a.Facing),
            BlockType.Column => !d.IsHorizontal(),
            _ => false,
        };
    }

    static (V3 lo, V3 hi) PipeBounds(Block b, float lo, float hi) => b.Facing switch
    {
        Dir.Up or Dir.Down => (new V3(lo, 0, lo), new V3(hi, 1, hi)),
        Dir.East or Dir.West => (new V3(0, lo, lo), new V3(1, hi, hi)),
        _ => (new V3(lo, lo, 0), new V3(hi, hi, 1)),
    };

    /// <summary>A round pipe of eight flats along its axis, with a flange every few metres; a main (Variant 1) is 2 m across, drawn from its corner cell.</summary>
    static void Pipe(List<ShapeBox> l, Block b, Int3 p, Rgb c)
    {
        float k = b.Variant == 1 ? 2f : 1f;
        float a = 0.05f * k, d = 0.22f * k, e = 1f * k - a, f = 1f * k - d;
        bool flange = (b.Facing is Dir.Up or Dir.Down ? p.Y : b.Facing is Dir.East or Dir.West ? p.X : p.Z) % (b.Variant == 1 ? 8 : 6) == 0;
        void Add(float x0, float y0, float x1, float y1, float s0, float s1, Rgb col)
        {
            switch (b.Facing)
            {
                case Dir.Up or Dir.Down: l.Add(new ShapeBox(new V3(x0, s0, y0), new V3(x1, s1, y1), col, Surface.Metal)); break;
                case Dir.East or Dir.West: l.Add(new ShapeBox(new V3(s0, y0, x0), new V3(s1, y1, x1), col, Surface.Metal)); break;
                default: l.Add(new ShapeBox(new V3(x0, y0, s0), new V3(x1, y1, s1), col, Surface.Metal)); break;
            }
        }
        Add(d, a, f, e, 0, 1, c);
        Add(a, d, e, f, 0, 1, c);
        if (flange) Add(0, 0, k, k, 0.35f, 0.65f, c.Scale(0.7f));
    }

    /// <summary>Which ways a rail's line runs from its cell: toward each rail beside it (or a step up or down, as on a stair), on into a wall or post where the line ends, or across its Facing when it stands alone.</summary>
    public static (bool East, bool West, bool North, bool South, bool Alone) RailRun(Block b, Int3 p, IBlockStructure g)
    {
        static bool IsRail(Block n) => n.Type == BlockType.Rail;
        bool Joins(Dir d) { Int3 n = p + d.Vec(); return IsRail(g.Get(n)) || IsRail(g.Get(n + Int3.Up)) || IsRail(g.Get(n + Int3.Down)); }
        bool Solid(Dir d) { Block n = g.Get(p + d.Vec()); return BlockInfo.IsFullCube(n.Type) || n.Type is BlockType.Pillar or BlockType.Rib or BlockType.Lattice; }
        bool e = Joins(Dir.East), w = Joins(Dir.West), n = Joins(Dir.North), so = Joins(Dir.South);
        bool alongX = e || w, alongZ = n || so;
        if (!alongX && !alongZ)
        {
            // Alone: between two walls it spans the gap; else it faces its Facing, as a sign does, and runs across it.
            bool x = Solid(Dir.East) || Solid(Dir.West), z = Solid(Dir.North) || Solid(Dir.South);
            bool runX = x != z ? x : b.Facing is Dir.North or Dir.South or Dir.Up or Dir.Down;
            return runX ? (true, true, false, false, true) : (false, false, true, true, true);
        }
        // A line's end runs on into the wall or post it meets; it never turns into a wall beside it.
        if (alongX && !alongZ) { e |= Solid(Dir.East); w |= Solid(Dir.West); }
        if (alongZ && !alongX) { n |= Solid(Dir.North); so |= Solid(Dir.South); }
        return (e, w, n, so, false);
    }

    /// <summary>A rail's Variant when it follows a round's outline (<see cref="RimRail"/>).</summary>
    public const byte RimRailVariant = 1;

    /// <summary>A rim rail's set-out: how far in from the edge it runs, m; posts at most <c>RimSpacing</c> apart along it, m; and one at every turn sharper than <c>RimTurn</c>, radians.</summary>
    public const float RimInset = 0.08f, RimSpacing = 1.25f, RimTurn = 30 * MathF.PI / 180;

    /// <summary>
    /// The outline a rim rail runs along through its cell, in the place's metres (y 0): the edges of the plan under it that
    /// are the round's outline (its cuts, and its faces no neighbour covers), in the plan's winding, so each cell's line
    /// begins where the one before it ends and the round's inside is on its left.
    /// </summary>
    static List<V3> RimLine(IBlockStructure g, Int3 p)
    {
        Int3 at = p + Int3.Down;
        var line = new List<V3>();
        if (PlanOf(g.Get(at)) is not { } plan) return line;
        int n = plan.Length;
        bool Open(int i)
        {
            V3 a = plan[i], e = plan[(i + 1) % n];
            return OnSide(a, e) is not Dir d || !Covers(g.Get(at + d.Vec()), d.Opposite(), Span(a, e, d));
        }
        int start = Enumerable.Range(0, n).FirstOrDefault(i => Open(i) && !Open((i + n - 1) % n));
        for (int i = start, k = 0; k < n && Open(i); i = (i + 1) % n, k++)
        {
            if (line.Count == 0) line.Add(new V3(p.X + plan[i].X, 0, p.Z + plan[i].Z));
            line.Add(new V3(p.X + plan[(i + 1) % n].X, 0, p.Z + plan[(i + 1) % n].Z));
        }
        return line;
    }

    /// <summary>
    /// The rim rail a cell is part of, traced cell to cell along its outline (<see cref="RimLine"/>): each cell's line in
    /// order from the rail's start (its open end, or on a closed round its north-west-most cell), and whether it closes.
    /// </summary>
    static (List<(Int3 Cell, List<V3> Line)> Cells, bool Closed) RimChain(IBlockStructure g, Int3 p)
    {
        var lines = new Dictionary<Int3, List<V3>>();
        List<V3> LineOf(Int3 q) => lines.TryGetValue(q, out var l) ? l : lines[q] = g.Get(q) is { Type: BlockType.Rail, Variant: RimRailVariant } ? RimLine(g, q) : new();
        Int3? Step(Int3 q, bool on)
        {
            V3 end = on ? LineOf(q)[^1] : LineOf(q)[0];
            for (int dz = -1; dz <= 1; dz++)
                for (int dx = -1; dx <= 1; dx++)
                {
                    Int3 r = q + new Int3(dx, 0, dz);
                    if (r == q || LineOf(r) is not { Count: > 1 } m) continue;
                    V3 join = on ? m[0] : m[^1];
                    if (MathF.Abs(join.X - end.X) < 1e-3f && MathF.Abs(join.Z - end.Z) < 1e-3f) return r;
                }
            return null;
        }
        if (LineOf(p).Count < 2) return (new() { (p, LineOf(p)) }, false);
        Int3 start = p;
        bool closed = false;
        for (int i = 0; i < 4096 && Step(start, false) is Int3 back; i++)
        {
            if (back == p) { closed = true; break; }
            start = back;
        }
        var cells = new List<Int3> { start };
        for (Int3 q = start; cells.Count < 4096 && Step(q, true) is Int3 next && next != start; q = next) cells.Add(next);
        if (!cells.Contains(p)) (cells, closed) = (new() { p }, false);
        if (closed)
        {
            int k = cells.IndexOf(cells.MinBy(q => (q.Z, q.X)));
            cells = cells.Skip(k).Concat(cells.Take(k)).ToList();
        }
        return (cells.Select(q => (q, LineOf(q))).ToList(), closed);
    }

    /// <summary>
    /// A rail round a round thing's top, a hand in from its outline (<see cref="RimChain"/>), traced whole so every cell draws
    /// its share of one rail: runs mitred where it turns, cell to cell; posts on its line, at each end, at each turn sharper
    /// than <see cref="RimTurn"/>, and evenly between those no more than <see cref="RimSpacing"/> apart along it.
    /// </summary>
    static void RimRail(List<ShapeBox> l, Int3 p, IBlockStructure g, Rgb c, Surface s)
    {
        var (chain, closed) = RimChain(g, p);
        // The rail as one line (a closed one ends where it began), and the point each cell's share begins at.
        var pts = new List<V3>();
        var from = new List<int>();
        foreach (var (_, line) in chain)
        {
            from.Add(Math.Max(0, pts.Count - 1));
            pts.AddRange(pts.Count == 0 ? line : line.Skip(1));
        }
        int n = pts.Count, me = chain.FindIndex(q => q.Cell == p);
        if (n < 2) return;
        from.Add(n - 1);
        V3 Way(int k) => (pts[k + 1] - pts[k]).Normalized();
        // Each corner's way in and out (at an open end, the one it has), its turn, and the rail's point there, mitred in from the edge.
        var inset = new V3[n];
        var turn = new float[n];
        for (int k = 0; k < n; k++)
        {
            V3 da = k > 0 ? Way(k - 1) : closed ? Way(n - 2) : Way(0), de = k < n - 1 ? Way(k) : closed ? Way(0) : Way(n - 2);
            V3 na = new(-da.Z, 0, da.X), ne = new(-de.Z, 0, de.X);
            turn[k] = Math.Min(MathF.Acos(Math.Clamp(V3.Dot(da, de), -1f, 1f)), 2f);
            inset[k] = pts[k] + (na + ne) * (RimInset / Math.Max(0.2f, 1 + V3.Dot(na, ne)));
        }
        var at = new float[n];
        for (int k = 1; k < n; k++) { V3 d = inset[k] - inset[k - 1]; at[k] = at[k - 1] + MathF.Sqrt(d.X * d.X + d.Z * d.Z); }
        float length = at[n - 1];
        // The posts it must have (its ends, its sharp turns), and evenly between each two.
        var must = new List<float>();
        if (!closed) must.Add(0);
        for (int k = closed ? 0 : 1; k < n - 1; k++) if (turn[k] > RimTurn) must.Add(at[k]);
        if (must.Count == 0) must.Add(0);
        if (!closed) must.Add(length);
        var posts = new List<float>();
        for (int i = 0; i + (closed ? 0 : 1) < must.Count; i++)
        {
            float a = must[i], e = i + 1 < must.Count ? must[i + 1] : must[0] + length;
            int parts = Math.Max(1, (int)MathF.Ceiling((e - a) / RimSpacing - 1e-4f));
            for (int k = 0; k < parts; k++) posts.Add((a + (e - a) * k / parts) % length);
        }
        if (!closed) posts.Add(length);
        // This cell's runs, each lengthened at a turn so the outer edges meet, and the posts along its share.
        float lo = at[from[me]], hi = at[from[me + 1]];
        bool last = me == chain.Count - 1 && !closed;
        V3 o = new(p.X, 0, p.Z);
        for (int k = from[me]; k < from[me + 1]; k++)
        {
            V3 d = Way(k), a = inset[k] - o, e = inset[k + 1] - o;
            float yaw = MathF.Atan2(-d.Z, d.X);
            foreach (var (y0, y1, w, col) in new[] { (0.95f, 1.05f, 0.05f, c), (0.48f, 0.54f, 0.03f, c.Scale(0.8f)) })
            {
                V3 a1 = a - d * (w * MathF.Tan(turn[k] / 2)), e1 = e + d * (w * MathF.Tan(turn[k + 1] / 2)), m = (a1 + e1) * 0.5f;
                float half = V3.Dot(e1 - a1, d) / 2;
                l.Add(new ShapeBox(new V3(m.X - half, y0, m.Z - w), new V3(m.X + half, y1, m.Z + w), col, s, Tilt: new V3(0, yaw, 0)));
            }
        }
        foreach (float t in posts)
        {
            if (t < lo - 1e-4f || t >= hi - 1e-4f && !(last && t >= length - 1e-4f)) continue;
            int k = Math.Clamp(Array.FindLastIndex(at, v => v <= t + 1e-6f), 0, n - 2);
            V3 d = Way(k), q = inset[k] + d * (t - at[k]) - o;
            l.Add(new ShapeBox(new V3(q.X - 0.06f, 0, q.Z - 0.06f), new V3(q.X + 0.06f, 1.07f, q.Z + 0.06f), c, s, Tilt: new V3(0, MathF.Atan2(-d.Z, d.X), 0)));
        }
    }

    /// <summary>
    /// A railing or fence (bars, in the Wall role) drawn along the line it is part of (<see cref="RailRun"/>):
    /// a top rail and a middle rail toward each way the line runs, a post at its ends, corners and junctions and
    /// every other metre between; bars are upright bars across the run under a flat bar. Nothing runs off sideways.
    /// </summary>
    static void Rail(List<ShapeBox> l, Block b, Int3 p, IBlockStructure g, Rgb c, Surface s)
    {
        if (b.Variant == RimRailVariant) { RimRail(l, p, g, c, s); return; }
        var (e, w, n, so, _) = RailRun(b, p, g);
        bool straight = e && w && !n && !so || n && so && !e && !w;
        bool post = !straight || ((p.X + p.Z) & 1) == 0;
        // A run along x from the middle to each side it goes, then one along z; a lone arm starts at the middle.
        void Runs(float y0, float y1, float half, Rgb col)
        {
            if (e || w) l.Add(Box(w ? 0f : 0.5f - half, y0, 0.5f - half, e ? 1f : 0.5f + half, y1, 0.5f + half, col, s));
            if (n || so) l.Add(Box(0.5f - half, y0, n ? 0f : 0.5f - half, 0.5f + half, y1, so ? 1f : 0.5f + half, col, s));
        }
        if (b.Role == Role.Wall)
        {
            // Bars: upright bars an eighth of a metre apart across the run, a flat bar over them where nothing stands above.
            bool top = g.Get(p + Int3.Up).Type != BlockType.Rail;
            if (top) Runs(0.92f, 1f, 0.04f, c);
            bool x = e || w;
            for (int i = 0; i < 8; i++)
            {
                float a = 0.0625f + i * 0.125f;
                if (x && (a < 0.5f ? w : e) || !x && (a < 0.5f ? n : so) || Math.Abs(a - 0.5f) < 0.07f)
                    l.Add(x ? Box(a - 0.015f, 0, 0.485f, a + 0.015f, top ? 0.92f : 1f, 0.515f, c, s) : Box(0.485f, 0, a - 0.015f, 0.515f, top ? 0.92f : 1f, a + 0.015f, c, s));
            }
            return;
        }
        Runs(0.95f, 1.05f, 0.05f, c);
        // The post caps where runs meet, so no two rails' faces lie on one another.
        if (post) l.Add(Box(0.44f, 0, 0.44f, 0.56f, 1.07f, 0.56f, c, s));
        Runs(0.48f, 0.54f, 0.03f, c.Scale(0.8f));
    }

    public static List<ShapeBox> For(Block b, Int3 p, IBlockStructure g)
    {
        var l = new List<ShapeBox>(6);
        Rgb c = b.Color;
        Surface s = SurfaceOf(b.Material);
        uint h = Hash.Cell(p, 77);
        float R(int k) => ((h >> (k * 4)) & 15) / 15f;
        Rgb dark = c.Scale(0.6f);

        switch (b.Type)
        {
            case BlockType.Half or BlockType.Quarter or BlockType.Partition or BlockType.Beam or BlockType.Column:
                l.AddRange(Structural(b, c, s));
                break;
            case BlockType.Pillar:
                l.Add(Box(0.3f, 0, 0.3f, 0.7f, 1, 0.7f, c, s));
                break;
            case BlockType.Rib:
                l.Add(Box(0.18f, 0, 0.18f, 0.82f, 1, 0.82f, c, s));
                l.Add(Box(0.1f, 0.42f, 0.1f, 0.9f, 0.58f, 0.9f, c.Scale(0.85f), s));
                break;
            case BlockType.Pane:
                // A sheet in the plane of its wall, as its facing says (PaneFacing); a wall of several layers is one sheet, in its outermost.
                if (!PaneHidden(b, p, g)) l.Add(PaneAlongX(b) ? Box(0, 0, 0.42f, 1, 1, 0.58f, c, s) : Box(0.42f, 0, 0, 0.58f, 1, 1, c, s));
                break;
            case BlockType.Lattice:
            {
                const float t = 0.13f;
                for (int i = 0; i < 4; i++)
                {
                    float x = (i & 1) == 0 ? 0 : 1 - t, z = (i & 2) == 0 ? 0 : 1 - t;
                    l.Add(Box(x, 0, z, x + t, 1, z + t, c, s));
                }
                foreach (float y in new[] { 0f, 1 - t })
                {
                    l.Add(Box(t, y, 0, 1 - t, y + t, t, c, s));
                    l.Add(Box(t, y, 1 - t, 1 - t, y + t, 1, c, s));
                    l.Add(Box(0, y, t, t, y + t, 1 - t, c, s));
                    l.Add(Box(1 - t, y, t, 1, y + t, 1 - t, c, s));
                }
                break;
            }
            case BlockType.Door:
            {
                bool bottom = g.Get(p + Int3.Down).Type != BlockType.Door;
                if (b.Variant == DoorState.Open || b.Variant == DoorState.Jammed && bottom) break;
                bool plainDoor = b.Material == Material.Timber; // a house's door: a plain leaf, no hazard band
                l.Add(Box(0, 0, 0.4f, 1, 1, 0.6f, c, plainDoor ? Surface.Matte : Surface.Metal, b.Facing));
                if (bottom && !plainDoor) l.Add(Box(0, 0.35f, 0.38f, 1, 0.5f, 0.62f, new Rgb(0xe0, 0xa8, 0x00), Surface.Metal, b.Facing));
                break;
            }
            case BlockType.SealedDoor:
                l.Add(Box(0, 0, 0.3f, 1, 1, 0.7f, c, Surface.Metal, b.Facing));
                l.Add(Box(0.45f, 0, 0.25f, 0.55f, 1, 0.75f, new Rgb(0xd0, 0x90, 0x40), Surface.Metal, b.Facing)); // weld seam
                if (b.Variant == 1)
                    for (int i = 0; i < 3; i++)
                        l.Add(Box(0.15f + i * 0.12f, 0.2f + R(i) * 0.3f, 0.22f, 0.2f + i * 0.12f, 0.6f + R(i) * 0.3f, 0.3f, new Rgb(0x10, 0x08, 0x08), Surface.Matte, b.Facing));
                break;
            case BlockType.Terminal:
            {
                Rgb screen = b.Material == Material.DeadLumen ? ScreenDim : ScreenOn;
                l.Add(Box(0.15f, 0, 0.3f, 0.85f, 0.8f, 0.85f, c.Scale(0.7f), Surface.Metal, b.Facing));
                l.Add(Box(0.2f, 0.8f, 0.45f, 0.8f, 1.35f, 0.6f, c.Scale(0.5f), Surface.Metal, b.Facing));
                l.Add(Box(0.25f, 0.85f, 0.42f, 0.75f, 1.3f, 0.45f, screen, Surface.Glow, b.Facing));
                break;
            }
            case BlockType.Cache:
            {
                bool vault = b.Variant == 1;
                Rgb glow = vault ? new Rgb(0xff, 0xc8, 0x40) : new Rgb(0xff, 0xe0, 0x90);
                l.Add(Box(0.1f, 0, 0.2f, 0.9f, vault ? 0.7f : 0.55f, 0.8f, c.Scale(0.75f), Surface.Metal, b.Facing));
                l.Add(Box(0.07f, vault ? 0.7f : 0.55f, 0.17f, 0.93f, vault ? 0.82f : 0.65f, 0.83f, c, Surface.Metal, b.Facing));
                l.Add(Box(0.12f, 0.3f, 0.18f, 0.88f, 0.36f, 0.82f, glow, Surface.Glow, b.Facing));
                break;
            }
            case BlockType.Core:
            {
                bool dormant = b.Variant == 1;
                Rgb glow = dormant ? c.Scale(0.35f) : c;
                l.Add(Box(0.08f, 0, 0.08f, 0.92f, 0.3f, 0.92f, new Rgb(0x2a, 0x2e, 0x34), Surface.Metal));
                l.Add(Box(0.3f, 0.3f, 0.3f, 0.7f, 1.7f, 0.7f, glow, Surface.Glow));
                foreach (float y in new[] { 0.6f, 1.2f })
                    l.Add(Box(0.22f, y, 0.22f, 0.78f, y + 0.1f, 0.78f, new Rgb(0x3a, 0x40, 0x48), Surface.Metal));
                l.Add(Box(0.15f, 1.7f, 0.15f, 0.85f, 1.85f, 0.85f, new Rgb(0x2a, 0x2e, 0x34), Surface.Metal));
                break;
            }
            case BlockType.Crate:
            {
                float lift = b.Variant == 1 ? 0.05f : 0f;
                float sz = 0.55f + R(0) * 0.3f;
                float ox = (1 - sz) * R(1), oz = (1 - sz) * R(2);
                l.Add(Box(ox, 0, oz, ox + sz, sz * 0.9f + lift, oz + sz, c, s));
                l.Add(Box(ox - 0.01f, sz * 0.4f, oz - 0.01f, ox + sz + 0.01f, sz * 0.5f, oz + sz + 0.01f, dark, s));
                break;
            }
            case BlockType.Table:
            case BlockType.Meal:
            {
                l.Add(Box(0.05f, 0.7f, 0.05f, 0.95f, 0.8f, 0.95f, c, s));
                foreach (var (x, z) in new[] { (0.1f, 0.1f), (0.82f, 0.1f), (0.1f, 0.82f), (0.82f, 0.82f) })
                    l.Add(Box(x, 0, z, x + 0.08f, 0.7f, z + 0.08f, dark, s));
                if (b.Type == BlockType.Meal)
                {
                    Rgb plate = new(0xd8, 0xd0, 0xc0), food = new(0x8a, 0x5a, 0x2a);
                    for (int i = 0; i < 3; i++)
                    {
                        float x = 0.15f + i * 0.27f, z = 0.2f + R(i) * 0.45f;
                        l.Add(Box(x, 0.8f, z, x + 0.2f, 0.83f, z + 0.2f, plate, Surface.Matte));
                        if (R(i + 3) > 0.4f) l.Add(Box(x + 0.05f, 0.83f, z + 0.05f, x + 0.15f, 0.87f, z + 0.15f, food, Surface.Matte));
                    }
                    l.Add(Box(0.7f, 0.8f, 0.65f, 0.78f, 0.92f, 0.73f, new Rgb(0x6a, 0x7a, 0x8a), Surface.Matte)); // a cup, still there
                }
                break;
            }
            case BlockType.Bunk:
                l.Add(Box(0.05f, 0, 0.1f, 0.95f, 0.45f, 0.9f, dark, Surface.Metal));
                l.Add(Box(0.08f, 0.45f, 0.13f, 0.92f, 0.6f, 0.87f, c, Surface.Matte));
                break;
            case BlockType.Barricade:
                l.Add(Box(0, 0, 0.3f, 1, 0.35f, 0.7f, c, s, b.Facing));
                l.Add(Box(0.1f, 0.35f, 0.25f, 0.9f, 0.7f, 0.65f, c.Scale(0.8f), s, b.Facing));
                l.Add(Box(0.25f, 0.7f, 0.35f, 0.85f, 1.0f, 0.75f, c.Scale(0.65f), s, b.Facing));
                break;
            case BlockType.Remains:
            {
                l.Add(Box(0.25f, 0, 0.1f, 0.75f, 0.28f, 0.75f, c, Surface.Matte, b.Facing));
                l.Add(Box(0.32f, 0, 0.75f, 0.68f, 0.32f, 0.98f, c.Scale(0.8f), Surface.Matte, b.Facing));
                l.Add(Box(0.36f, 0.12f, 0.97f, 0.64f, 0.26f, 0.99f, new Rgb(0x30, 0x48, 0x58), Surface.Glass, b.Facing));
                break;
            }
            case BlockType.Plate:
                // Thin as a finish (it is walked through, never on): the floor it lies on meets a stair's step flush.
                l.Add(new ShapeBox(new V3(0, 0, 0), new V3(1, Shapes.PlateTop, 1), c, Surface.Matte));
                break;
            case BlockType.Scorch:
                l.Add(new ShapeBox(new V3(0.02f, 0, 0.02f), new V3(0.98f, 0.02f, 0.98f), c, Surface.Matte, true));
                break;
            case BlockType.Scatter:
                for (int i = 0; i < 3; i++)
                {
                    float x = R(i) * 0.7f, z = R(i + 3) * 0.7f;
                    l.Add(new ShapeBox(new V3(x, 0, z), new V3(x + 0.22f, 0.02f + i * 0.01f, z + 0.3f), i == 0 ? c : c.Scale(0.85f), Surface.Matte, true));
                }
                break;
            case BlockType.Rubble:
                Debris.Shape(l, b, p);
                break;
            case BlockType.Rebar:
                Debris.Bars(l, b, p);
                break;
            case BlockType.Ash:
                l.Add(new ShapeBox(new V3(0, 0, 0), new V3(1, 0.06f + R(0) * 0.05f, 1), c, Surface.Matte, true));
                if (R(1) > 0.5f) l.Add(Box(R(2) * 0.5f, 0, R(3) * 0.5f, R(2) * 0.5f + 0.4f, 0.16f, R(3) * 0.5f + 0.4f, c.Scale(0.95f), Surface.Matte));
                break;
            case BlockType.Growth:
            {
                // Lumps that bulge out of the cell; the Bloom doesn't respect the grid.
                for (int i = 0; i < 3; i++)
                {
                    float sz = 0.6f + R(i) * 0.5f;
                    float x = R(i + 3) * (1.1f - sz) - 0.05f, y = R(i + 5) * (1.1f - sz) - 0.05f, z = R(i + 1) * (1.1f - sz) - 0.05f;
                    l.Add(Box(x, y, z, x + sz, y + sz, z + sz, i == 1 ? c.Scale(0.75f) : c, Surface.Flesh));
                }
                // Glowing nodules push through the skin, and a vein runs over it.
                for (int i = 0; i < 1 + (R(7) > 0.45f ? 1 : 0); i++)
                {
                    float x = 0.15f + R(2 + i) * 0.55f, y = 0.2f + R(4 + i) * 0.55f, sz = 0.14f + R(6 + i) * 0.1f;
                    l.Add(Box(x, y, -0.08f, x + sz, y + sz, 1.08f, StateTransforms.BloomGlow, Surface.Glow));
                }
                if (R(1) > 0.4f)
                {
                    float z = 0.2f + R(3) * 0.6f;
                    l.Add(Box(-0.05f, 0.25f + R(5) * 0.5f, z, 1.05f, 0.31f + R(5) * 0.5f, z + 0.06f, new Rgb(0xff, 0x6a, 0xc0), Surface.Glow));
                }
                break;
            }
            case BlockType.Tendril:
                l.Add(Box(0.43f, 0, 0.43f, 0.57f, 1, 0.57f, c, Surface.Flesh));
                l.Add(Box(0.38f + R(0) * 0.1f, 0.3f, 0.38f, 0.52f + R(0) * 0.1f, 0.7f, 0.52f, c.Scale(0.8f), Surface.Flesh));
                if (b.Variant == 1 || R(1) > 0.6f) l.Add(Box(0.4f, 0, 0.4f, 0.6f, 0.14f, 0.6f, StateTransforms.BloomGlow, Surface.Glow));
                break;
            case BlockType.Pod:
            {
                bool glows = b.Material is Material.Lumen or Material.Bloom;
                Rgb shell = b.Material == Material.Bloom ? StateTransforms.BloomBruise : c.Scale(0.45f);
                l.Add(Box(0.18f, 0, 0.18f, 0.82f, 0.45f, 0.82f, shell, b.Material == Material.Bloom ? Surface.Flesh : Surface.Matte));
                l.Add(Box(0.24f, 0.45f, 0.24f, 0.76f, 0.95f, 0.76f, glows ? c : c.Scale(0.5f), glows ? Surface.Glow : Surface.Matte));
                l.Add(Box(0.3f, 0.95f, 0.3f, 0.7f, 1.05f, 0.7f, shell, Surface.Matte));
                break;
            }
            case BlockType.Plant:
                for (int i = 0; i < 3; i++)
                {
                    float x = 0.15f + R(i) * 0.6f, z = 0.15f + R(i + 3) * 0.6f, hgt = 0.25f + R(i + 5) * 0.55f;
                    l.Add(Box(x, 0, z, x + 0.1f, hgt, z + 0.1f, c.Scale(0.85f + i * 0.1f), Surface.Matte));
                }
                break;
            case BlockType.Machine:
                l.Add(Box(0.05f, 0, 0.05f, 0.95f, 0.95f, 0.95f, c, Surface.Metal));
                l.Add(Box(0.0f, 0.55f, 0.15f, 1.0f, 0.65f, 0.85f, c.Scale(0.6f), Surface.Metal));
                l.Add(Box(0.7f, 0.75f, -0.01f, 0.82f, 0.85f, 0.05f, new Rgb(0xff, 0xa0, 0x30), Surface.Glow));
                break;
            case BlockType.Crystal:
                for (int i = 0; i < 3; i++)
                {
                    float x = 0.15f + R(i) * 0.5f, z = 0.15f + R(i + 3) * 0.5f, hgt = 0.5f + R(i + 6) * 0.9f;
                    l.Add(Box(x, 0, z, x + 0.18f, hgt, z + 0.18f, c, Surface.Glow));
                }
                break;
            case BlockType.Stair:
                // Four steps rising toward Facing (built rising north, then turned).
                for (int i = 0; i < 4; i++)
                {
                    float z0 = 1f - (i + 1) * 0.25f;
                    l.Add(Box(0, 0, z0, 1, (i + 1) * 0.25f, z0 + 0.25f, i % 2 == 0 ? c : c.Scale(0.9f), s, b.Facing));
                }
                break;
            case BlockType.Ladder:
            {
                // Rails and rungs flat against the wall behind Facing (Facing looks out from the wall, at the climber).
                Rgb rail = c.Scale(0.85f);
                Dir wall = b.Facing.Opposite();
                l.Add(Box(0.18f, 0, 0.04f, 0.26f, 1, 0.14f, rail, Surface.Metal, wall));
                l.Add(Box(0.74f, 0, 0.04f, 0.82f, 1, 0.14f, rail, Surface.Metal, wall));
                for (int i = 0; i < 4; i++)
                    l.Add(Box(0.26f, 0.12f + i * 0.25f, 0.06f, 0.74f, 0.16f + i * 0.25f, 0.12f, c, Surface.Metal, wall));
                break;
            }
            case BlockType.Rail:
                Rail(l, b, p, g, c, s);
                break;
            case BlockType.Sign:
            {
                // A warning plate: hazard yellow with a dark band, or a radiation mark.
                l.Add(Box(0.1f, 0.15f, 0.3f, 0.9f, 0.85f, 0.38f, c, Surface.Matte, b.Facing));
                l.Add(Box(0.1f, 0.15f, 0.62f, 0.9f, 0.85f, 0.7f, c, Surface.Matte, b.Facing));
                Rgb mark = new(0x1a, 0x1a, 0x18);
                if (b.Variant == 1)
                {
                    l.Add(Box(0.42f, 0.42f, 0.27f, 0.58f, 0.58f, 0.73f, mark, Surface.Matte, b.Facing));
                    l.Add(Box(0.25f, 0.55f, 0.27f, 0.4f, 0.75f, 0.73f, mark, Surface.Matte, b.Facing));
                    l.Add(Box(0.6f, 0.55f, 0.27f, 0.75f, 0.75f, 0.73f, mark, Surface.Matte, b.Facing));
                    l.Add(Box(0.42f, 0.2f, 0.27f, 0.58f, 0.38f, 0.73f, mark, Surface.Matte, b.Facing));
                }
                else l.Add(Box(0.1f, 0.42f, 0.27f, 0.9f, 0.58f, 0.73f, mark, Surface.Matte, b.Facing));
                break;
            }
            case BlockType.Shelf:
                foreach (float x in new[] { 0.06f, 0.88f })
                    l.Add(Box(x, 0, 0.1f, x + 0.06f, 1, 0.9f, dark, Surface.Metal));
                for (int i = 0; i < 3; i++)
                {
                    float y = 0.05f + i * 0.32f;
                    l.Add(Box(0.06f, y, 0.1f, 0.94f, y + 0.04f, 0.9f, c, Surface.Metal));
                    if (R(i) > 0.3f) l.Add(Box(0.15f + R(i + 3) * 0.3f, y + 0.04f, 0.2f, 0.45f + R(i + 3) * 0.3f, y + 0.22f + R(i + 6) * 0.06f, 0.75f, c.Lerp(new Rgb(0x8a, 0x7a, 0x5a), 0.5f), Surface.Matte));
                }
                break;
            case BlockType.Locker:
                l.Add(Box(0.12f, 0, 0.25f, 0.88f, 1.0f, 0.9f, c, Surface.Metal, b.Facing));
                l.Add(Box(0.49f, 0.05f, 0.22f, 0.51f, 0.95f, 0.25f, dark, Surface.Metal, b.Facing));
                l.Add(Box(0.2f, 0.75f, 0.22f, 0.4f, 0.8f, 0.25f, dark, Surface.Metal, b.Facing));
                l.Add(Box(0.6f, 0.75f, 0.22f, 0.8f, 0.8f, 0.25f, dark, Surface.Metal, b.Facing));
                break;
            case BlockType.Puddle:
                l.Add(new ShapeBox(new V3(0.05f + R(0) * 0.2f, 0, 0.05f + R(1) * 0.2f), new V3(0.75f + R(2) * 0.2f, 0.015f, 0.7f + R(3) * 0.25f), c, Surface.Glass, true));
                break;
            case BlockType.Cable:
                for (int i = 0; i < 2; i++)
                {
                    float x = 0.25f + R(i) * 0.5f, z = 0.25f + R(i + 2) * 0.5f;
                    l.Add(Box(x, 0.2f + R(i + 4) * 0.4f, z, x + 0.04f, 1, z + 0.04f, c, Surface.Matte));
                }
                break;
            case BlockType.Bed:
                l.Add(Box(0.1f, 0, 0.05f, 0.9f, 0.4f, 0.95f, dark, Surface.Metal, b.Facing));
                l.Add(Box(0.12f, 0.4f, 0.07f, 0.88f, 0.55f, 0.93f, c, Surface.Matte, b.Facing));
                l.Add(Box(0.2f, 0.55f, 0.1f, 0.8f, 0.65f, 0.3f, c.Scale(1.05f), Surface.Matte, b.Facing));
                break;
            case BlockType.Bench:
                l.Add(Box(0.04f, 0.78f, 0.1f, 0.96f, 0.88f, 0.9f, c, Surface.Metal));
                l.Add(Box(0.08f, 0, 0.14f, 0.92f, 0.78f, 0.86f, c.Scale(0.65f), Surface.Metal));
                if (R(0) > 0.4f) l.Add(Box(0.2f, 0.88f, 0.3f, 0.45f, 1.05f, 0.6f, new Rgb(0x8a, 0xb8, 0xc0), Surface.Glass));
                if (R(1) > 0.5f) l.Add(Box(0.6f, 0.88f, 0.25f, 0.85f, 0.95f, 0.55f, c.Scale(1.2f), Surface.Metal));
                break;
            case BlockType.Tank:
                l.Add(Box(0.15f, 0, 0.05f, 0.85f, 0.95f, 0.95f, c, Surface.Metal));
                l.Add(Box(0.05f, 0, 0.15f, 0.95f, 0.95f, 0.85f, c, Surface.Metal));
                l.Add(Box(0.1f, 0.6f, 0.1f, 0.9f, 0.68f, 0.9f, c.Scale(0.7f), Surface.Metal));
                break;
            case BlockType.Statue:
                l.Add(Box(0.1f, 0, 0.1f, 0.9f, 0.3f, 0.9f, c.Scale(0.85f), Surface.Matte));
                l.Add(Box(0.3f, 0.3f, 0.35f, 0.7f, 1.3f, 0.65f, c, Surface.Matte));
                l.Add(Box(0.36f, 1.3f, 0.38f, 0.64f, 1.65f, 0.62f, c, Surface.Matte));
                l.Add(Box(0.15f, 0.9f, 0.42f, 0.85f, 1.05f, 0.58f, c.Scale(0.9f), Surface.Matte));
                break;
            case BlockType.Pump:
            {
                // A fuel pump: a body on a plinth, a dead display, a hose hanging on its side.
                l.Add(Box(0.15f, 0, 0.25f, 0.85f, 0.12f, 0.75f, c.Scale(0.6f), Surface.Metal, b.Facing));
                l.Add(Box(0.22f, 0.12f, 0.3f, 0.78f, 1.25f, 0.7f, c, Surface.Metal, b.Facing));
                l.Add(Box(0.26f, 1.25f, 0.34f, 0.74f, 1.7f, 0.66f, c.Scale(0.8f), Surface.Metal, b.Facing));
                l.Add(Box(0.3f, 1.3f, 0.32f, 0.7f, 1.62f, 0.34f, new Rgb(0x1c, 0x20, 0x22), Surface.Matte, b.Facing));
                l.Add(Box(0.8f, 0.45f, 0.45f, 0.85f, 1.1f, 0.5f, new Rgb(0x20, 0x20, 0x20), Surface.Matte, b.Facing));
                break;
            }
            case BlockType.Pipe:
                Pipe(l, b, p, c);
                break;
            case BlockType.Ramp:
            case BlockType.Spike:
            case BlockType.Corner:
            case BlockType.InvCorner:
            case BlockType.Chamfer or BlockType.ChamferCorner or BlockType.ChamferInvCorner:
            case >= BlockType.Wedge and <= BlockType.RoundIn: // (drawn as their plans: Mesher.Ctx.Prism)
                break;
            default:
                l.Add(Box(0.1f, 0, 0.1f, 0.9f, 0.9f, 0.9f, c, s));
                break;
        }
        return l;
    }
}
