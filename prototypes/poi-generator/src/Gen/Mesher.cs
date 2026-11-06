#nullable enable
using System;
using System.Collections.Generic;

namespace PoiGenerator.Gen;

/// <summary>Which material a face is drawn with.</summary>
/// <summary>Which material a face is drawn with. Flesh is the Hollow Bloom: lit, wet, faintly glowing.</summary>
public enum Surface { Matte, Metal, Glow, Glass, Flesh }

/// <summary>Plain vertex arrays for one surface of one chunk: positions, normals, sRGB colours.</summary>
public sealed class MeshBuffer
{
    public readonly List<float> Positions = new();
    public readonly List<float> Normals = new();
    public readonly List<float> Colors = new();
    public readonly List<int> Indices = new();
    public int VertexCount => Positions.Count / 3;

    public void Vertex(V3 p, V3 n, float r, float g, float b, float a)
    {
        Positions.Add(p.X); Positions.Add(p.Y); Positions.Add(p.Z);
        Normals.Add(n.X); Normals.Add(n.Y); Normals.Add(n.Z);
        Colors.Add(r); Colors.Add(g); Colors.Add(b); Colors.Add(a);
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
    /// <summary>
    /// How many cells of buried ground below ground level hide no faces. On a planet the ground only
    /// nearly reaches a wall: it dips round a room carved under the pad, and a cut or a bank can leave it
    /// short, so a wall's top few courses below ground are drawn rather than left open to the sky.
    /// </summary>
    public int GroundShow;
}

/// <summary>
/// Turns a block grid into chunked meshes. Faces between full cubes (and against buried rock)
/// are culled; each cube corner gets ambient occlusion from its neighbours, which gives the
/// flat-coloured blocks their depth. Props are a few boxes each, so a whole place stays a
/// handful of draw calls per chunk.
/// </summary>
public static class Mesher
{
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
        var list = new List<ChunkMesh>(chunks.Values);
        list.Sort((a, b) => (a.Chunk.Y, a.Chunk.Z, a.Chunk.X).CompareTo((b.Chunk.Y, b.Chunk.Z, b.Chunk.X)));
        return list;
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
        readonly byte[] _flags;
        readonly Int3 _fMin, _fSize;

        public Ctx(IBlockStructure g, CellMask occ, MeshOptions o, Dictionary<Int3, ChunkMesh> chunks)
        {
            _g = g;
            _occ = occ;
            _o = o;
            _chunks = chunks;

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
                        }
                        else if (o.Rock && p.Y <= Assembler.GroundY && !occ[p]) f = FOccludes | FCollides | FRock;
                        _flags[i] = f;
                    }
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

        public void Block(Int3 p, Block b)
        {
            if (b.Type == BlockType.Door && !_o.Doors) return;
            if (!_o.Detail && b.Type is BlockType.Scatter or BlockType.Scorch or BlockType.Ash or BlockType.Plant or BlockType.Remains) return;
            if (BlockInfo.IsFullCube(b.Type)) { Cube(p, b); return; }
            var chunk = Chunk(p);
            var boxes = Shapes.For(b, p, _g);
            foreach (var box in boxes) AddBox(chunk, p, box, b);
            if (b.Type == BlockType.Ramp) Ramp(chunk, p, b, draw: true);
            else if (b.Type == BlockType.Stair) Ramp(chunk, p, b, draw: false); // drawn as steps, walked as a slope
            else if (b.Type == BlockType.Spike) Spike(chunk, p, b);

            if (_o.Collision && BlockInfo.Collides(b.Type) && !BlockInfo.IsSlope(b.Type))
            {
                // Props collide as their bounding box.
                var (lo, hi) = Shapes.CollisionBox(b);
                CollideBox(chunk, V3.Of(p) + lo, V3.Of(p) + hi);
            }
        }

        void Cube(Int3 p, Block b)
        {
            var chunk = Chunk(p);
            bool glass = BlockInfo.IsTransparent(b.Material);
            Surface surf = Shapes.SurfaceOf(b.Material);
            float jitter = 0.94f + (Hash.Cell(p, 3) % 1000) / 1000f * 0.1f;
            // Glyph stone is black, with the glyph inlaid as thin glowing lines.
            bool glyph = b.Material == Material.Glyph;
            Rgb body = glyph ? b.Color.Lerp(new Rgb(0x1a, 0x1b, 0x22), 0.88f) : b.Color;
            if (glyph) surf = Surface.Matte;
            for (int f = 0; f < 6; f++)
            {
                Int3 n = p + Dirs.All[f].Vec();
                var nb = _g.Get(n);
                if (glass ? (!nb.IsAir && nb.Type == BlockType.Cube && (nb.Material == b.Material || !BlockInfo.IsTransparent(nb.Material))) || IsRock(n) : Hides(n))
                    continue;
                Face(chunk, surf, p, f, body, glass ? 0.35f : 1f, jitter, ao: surf != Surface.Glow);
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
                Face(chunk, Surface.Matte, p, f, c, 1f, top ? 1f : jitter, ao: true);
                if (_o.Collision) CollideFace(chunk, p, f);
            }
        }

        /// <summary>One cube face with per-corner ambient occlusion.</summary>
        void Face(ChunkMesh chunk, Surface surf, Int3 p, int f, Rgb color, float alpha, float jitter, bool ao)
        {
            var buf = chunk.Surfaces[(int)surf];
            V3 n = Normals[f];
            Corners(f, out V3 c0, out V3 c1, out V3 c2, out V3 c3);
            V3 o = V3.Of(p);
            float shade = (surf == Surface.Glow ? 1f : FaceShade[f]) * jitter;
            int i0 = buf.VertexCount;
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
                buf.Vertex(o + corner, n, color.R / 255f * k, color.G / 255f * k, color.B / 255f * k, alpha);
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

        void CollideFace(ChunkMesh chunk, Int3 p, int f)
        {
            Corners(f, out V3 a, out V3 b, out V3 c, out V3 d);
            V3 o = V3.Of(p);
            Tri(chunk, o + a, o + b, o + c);
            Tri(chunk, o + a, o + c, o + d);
        }

        static void Tri(ChunkMesh chunk, V3 a, V3 b, V3 c)
        {
            var l = chunk.Collision;
            l.Add(a.X); l.Add(a.Y); l.Add(a.Z);
            l.Add(b.X); l.Add(b.Y); l.Add(b.Z);
            l.Add(c.X); l.Add(c.Y); l.Add(c.Z);
        }

        void CollideBox(ChunkMesh chunk, V3 lo, V3 hi)
        {
            for (int f = 0; f < 6; f++)
            {
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
            for (int f = 0; f < 6; f++)
            {
                if (box.SkipBottom && f == 5) continue;
                BoxFace(f, o + box.Lo, o + box.Hi, out V3 a, out V3 bb, out V3 c, out V3 d);
                float k = box.Surface == Surface.Glow ? 1f : FaceShade[f];
                float r = box.Color.R / 255f * k, g = box.Color.G / 255f * k, bl = box.Color.B / 255f * k;
                int i0 = buf.VertexCount;
                buf.Vertex(a, Normals[f], r, g, bl, alpha);
                buf.Vertex(bb, Normals[f], r, g, bl, alpha);
                buf.Vertex(c, Normals[f], r, g, bl, alpha);
                buf.Vertex(d, Normals[f], r, g, bl, alpha);
                var idx = buf.Indices;
                idx.Add(i0); idx.Add(i0 + 2); idx.Add(i0 + 1); idx.Add(i0); idx.Add(i0 + 3); idx.Add(i0 + 2);
                chunk.Quads++;
            }
        }

        void Ramp(ChunkMesh chunk, Int3 p, Block b, bool draw)
        {
            // Built rising toward north, then turned to face the block's direction.
            V3 o = V3.Of(p);
            V3 R(V3 v) => o + Shapes.Turn(v, b.Facing);
            V3 lowL = new(0, 0, 1), lowR = new(1, 0, 1), highL = new(0, 1, 0), highR = new(1, 1, 0), backL = new(0, 0, 0), backR = new(1, 0, 0);
            var buf = chunk.Surfaces[(int)Shapes.SurfaceOf(b.Material)];
            float j = 0.95f + (Hash.Cell(p, 5) % 100) / 1000f;
            void Quad(V3 a, V3 bb, V3 c, V3 d, float shade)
            {
                V3 n = V3.Cross(bb - a, c - a).Normalized();
                int i0 = buf.VertexCount;
                foreach (V3 v in new[] { a, bb, c, d }) buf.Vertex(v, n, b.Color.R / 255f * shade * j, b.Color.G / 255f * shade * j, b.Color.B / 255f * shade * j, 1f);
                buf.Indices.AddRange(new[] { i0, i0 + 2, i0 + 1, i0, i0 + 3, i0 + 2 });
                chunk.Quads++;
            }
            void TriFace(V3 a, V3 bb, V3 c, float shade)
            {
                V3 n = V3.Cross(bb - a, c - a).Normalized();
                int i0 = buf.VertexCount;
                foreach (V3 v in new[] { a, bb, c }) buf.Vertex(v, n, b.Color.R / 255f * shade * j, b.Color.G / 255f * shade * j, b.Color.B / 255f * shade * j, 1f);
                buf.Indices.AddRange(new[] { i0, i0 + 2, i0 + 1 });
            }
            V3 sLL = R(lowL), sLR = R(lowR), sHL = R(highL), sHR = R(highR), sBL = R(backL), sBR = R(backR);
            if (draw)
            {
                Quad(sLL, sLR, sHR, sHL, 0.97f);                  // the slope
                Quad(sBR, sBL, sHL, sHR, 0.82f);                  // the tall back
                TriFace(sBL, sLL, sHL, 0.78f);                    // sides
                TriFace(sLR, sBR, sHR, 0.78f);
                // The underside, for a ramp nothing stands under: an eave or a roof seen from inside.
                if (!Occludes(p + Int3.Down)) Quad(sBL, sBR, sLR, sLL, 0.62f);
            }
            if (_o.Collision)
            {
                Tri(chunk, sLL, sLR, sHR); Tri(chunk, sLL, sHR, sHL);
                Tri(chunk, sBR, sBL, sHL); Tri(chunk, sBR, sHL, sHR);
                Tri(chunk, sBL, sLL, sHL); Tri(chunk, sLR, sBR, sHR);
            }
        }

        void Spike(ChunkMesh chunk, Int3 p, Block b)
        {
            var buf = chunk.Surfaces[(int)Surface.Matte];
            V3 o = V3.Of(p);
            V3 apex = o + new V3(0.5f, 1.25f, 0.5f);
            V3[] bs = { o + new V3(0.18f, 0, 0.18f), o + new V3(0.82f, 0, 0.18f), o + new V3(0.82f, 0, 0.82f), o + new V3(0.18f, 0, 0.82f) };
            for (int i = 0; i < 4; i++)
            {
                V3 a = bs[i], c = bs[(i + 1) % 4];
                V3 n = V3.Cross(apex - a, c - a).Normalized();
                float k = 0.75f + 0.25f * Math.Max(0f, n.Y + 0.3f) + (i % 2) * 0.08f;
                int i0 = buf.VertexCount;
                buf.Vertex(a, n, b.Color.R / 255f * k, b.Color.G / 255f * k, b.Color.B / 255f * k, 1f);
                buf.Vertex(c, n, b.Color.R / 255f * k, b.Color.G / 255f * k, b.Color.B / 255f * k, 1f);
                buf.Vertex(apex, n, b.Color.R / 255f * k * 1.1f, b.Color.G / 255f * k * 1.1f, b.Color.B / 255f * k * 1.1f, 1f);
                buf.Indices.AddRange(new[] { i0, i0 + 1, i0 + 2 });
            }
        }
    }
}

public readonly record struct ShapeBox(V3 Lo, V3 Hi, Rgb Color, Surface Surface, bool SkipBottom = false);

/// <summary>What each non-cube block looks like: a few boxes in cell-local units (0 to 1).</summary>
public static class Shapes
{
    public static readonly Rgb ScreenOn = new(0x7c, 0xff, 0xb0);
    public static readonly Rgb ScreenDim = new(0x2a, 0x6a, 0x48);

    public static Surface SurfaceOf(Material m) => m switch
    {
        Material.Lumen or Material.Glyph or Material.Crystal => Surface.Glow,
        Material.Glass => Surface.Glass,
        Material.Steel or Material.Alloy or Material.Bronze or Material.Scrap => Surface.Metal,
        _ => Surface.Matte,
    };

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

    public static (V3 lo, V3 hi) CollisionBox(Block b) => b.Type switch
    {
        BlockType.Pillar => (new V3(0.3f, 0, 0.3f), new V3(0.7f, 1, 0.7f)),
        BlockType.Rib => (new V3(0.15f, 0, 0.15f), new V3(0.85f, 1, 0.85f)),
        BlockType.Pane or BlockType.Lattice or BlockType.SealedDoor or BlockType.Growth => (new V3(0, 0, 0), new V3(1, 1, 1)),
        BlockType.Spike => (new V3(0.25f, 0, 0.25f), new V3(0.75f, 1, 0.75f)),
        BlockType.Rubble => (new V3(0.05f, 0, 0.05f), new V3(0.95f, 0.45f, 0.95f)),
        BlockType.Core => (new V3(0.2f, 0, 0.2f), new V3(0.8f, 1.8f, 0.8f)),
        BlockType.Rail => (new V3(0, 0, 0), new V3(1, 1.05f, 1)),
        BlockType.Shelf or BlockType.Locker => (new V3(0.06f, 0, 0.1f), new V3(0.94f, 1f, 0.9f)),
        BlockType.Statue => (new V3(0.1f, 0, 0.1f), new V3(0.9f, 1.65f, 0.9f)),
        BlockType.Bed => (new V3(0.1f, 0, 0.05f), new V3(0.9f, 0.65f, 0.95f)),
        BlockType.Pump => (new V3(0.2f, 0, 0.25f), new V3(0.8f, 1.7f, 0.75f)),
        _ => (new V3(0.1f, 0, 0.1f), new V3(0.9f, 0.9f, 0.9f)),
    };

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
            case BlockType.Pillar:
                l.Add(Box(0.3f, 0, 0.3f, 0.7f, 1, 0.7f, c, s));
                break;
            case BlockType.Rib:
                l.Add(Box(0.18f, 0, 0.18f, 0.82f, 1, 0.82f, c, s));
                l.Add(Box(0.1f, 0.42f, 0.1f, 0.9f, 0.58f, 0.9f, c.Scale(0.85f), s));
                break;
            case BlockType.Pane:
            {
                bool alongX = !g.Get(p + Dir.East.Vec()).IsAir || !g.Get(p + Dir.West.Vec()).IsAir;
                l.Add(alongX ? Box(0, 0, 0.42f, 1, 1, 0.58f, c, s) : Box(0.42f, 0, 0, 0.58f, 1, 1, c, s));
                break;
            }
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
                l.Add(new ShapeBox(new V3(0, 0, 0), new V3(1, 0.1f, 1), c, Surface.Matte, true));
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
                for (int i = 0; i < 4; i++)
                {
                    float sz = 0.25f + R(i) * 0.3f, x = R(i + 2) * (1 - sz), z = R(i + 4) * (1 - sz);
                    l.Add(Box(x, 0, z, x + sz, sz * 0.8f, z + sz, c.Scale(0.8f + R(i + 1) * 0.3f), Surface.Matte));
                }
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
                // Rails and rungs against the wall behind the climber (the north side, turned by Facing).
                Rgb rail = c.Scale(0.85f);
                l.Add(Box(0.18f, 0, 0.04f, 0.26f, 1, 0.14f, rail, Surface.Metal, b.Facing));
                l.Add(Box(0.74f, 0, 0.04f, 0.82f, 1, 0.14f, rail, Surface.Metal, b.Facing));
                for (int i = 0; i < 4; i++)
                    l.Add(Box(0.26f, 0.12f + i * 0.25f, 0.06f, 0.74f, 0.16f + i * 0.25f, 0.12f, c, Surface.Metal, b.Facing));
                break;
            }
            case BlockType.Rail:
                l.Add(Box(0.45f, 0, 0.45f, 0.55f, 1.0f, 0.55f, c, Surface.Metal));
                l.Add(Box(0, 0.95f, 0.44f, 1, 1.05f, 0.56f, c, Surface.Metal));
                l.Add(Box(0.44f, 0.95f, 0, 0.56f, 1.05f, 1, c, Surface.Metal));
                l.Add(Box(0, 0.5f, 0.46f, 1, 0.56f, 0.54f, c.Scale(0.8f), Surface.Metal));
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
            case BlockType.Ramp:
            case BlockType.Spike:
                break;
            default:
                l.Add(Box(0.1f, 0, 0.1f, 0.9f, 0.9f, 0.9f, c, s));
                break;
        }
        return l;
    }
}
