using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using GalHack.Terrain;
using Godot;
using Planets.Gen;
using Planets.Surface;

namespace PlanetSurface.Terrain;

/// <summary>
/// The far ground, shaped by the graphics card. Square rings at doubling spacing (2, 4, 8, 16 m...) nest round the
/// eye, each drawn by fixed grid blocks that terrain.gdshader shapes from textures holding the ground round the eye at
/// that spacing: each point's height, how far it slides onto the cliff beside it, its normal, the relief round it (a
/// cliff's top and foot), its glow and its colour (<see cref="FarBuilder.Shape"/>, as the far rings always had them). Toward a
/// ring's outer edge its vertices slide onto the next ring's grid and take that ring's ground, so the two meet exactly:
/// no change of detail is ever seen, and no seam needs a curtain. As the eye moves, workers fill only the tiles of
/// texture newly in range, from the terrain source the voxels and collision come from; no mesh is ever rebuilt.
/// Every ring moves at once, a <see cref="Snap"/> at a time, on the coarsest ring's grid and the horizon's.
/// The CPU keeps the textures' copy, and reads the drawn ground from it exactly as the shader lays it.
/// </summary>
public sealed partial class ClipGround : Node3D
{
    /// <summary>Quads along a block's side. A ring is four blocks across; the middle four hold the finer ring.</summary>
    public const int Block = 96, Half = Block * 2;

    /// <summary>Texels along a tile's side (the unit filled and sent), tiles along a level's texture, and texels along it.</summary>
    public const int Tile = 32, Tiles = 16, Texels = Tile * Tiles;

    /// <summary>How far the rings move at a time, m.</summary>
    public const float Snap = 64f;

    /// <summary>Tiles sent to the graphics card in one frame at the most.</summary>
    const int Uploads = 16;

    /// <summary>A level's grid spacing, m.</summary>
    public static float Spacing(int level) => 2 << level;

    /// <summary>
    /// Where a ring's vertices slide onto the next ring's grid, m from the eye (the larger of the two ways in plan):
    /// none at the start, all at the end. Its outer edge is never nearer than the end, nor the finer ring's edge (its
    /// inner one) further than the start, wherever the eye is between two snaps.
    /// </summary>
    public static (float From, float To) Band(int level) => (0.75f * Half * Spacing(level) - Snap / 2f, Half * Spacing(level) - Snap / 2f);

    sealed class Level
    {
        public float S;
        /// <summary>Per texel: the drawn height, how far the ground rises above it and falls below it within 16 m (the rock's cliff top and foot), glow.</summary>
        public readonly float[] Data = new float[Texels * Texels * 4];
        /// <summary>Per texel: the normal's x and z, and how far the point slides onto the cliff beside it, in grid steps along x and z.</summary>
        public readonly Half[] Shape = new Half[Texels * Texels * 4];
        public readonly Half[] Color = new Half[Texels * Texels * 4];
        /// <summary>Per texel, kept here only: the ground as sampled (before any slide), the river's level and whether it is in the river (far chunks' flora and water read them).</summary>
        public readonly float[] Raw = new float[Texels * Texels], Water = new float[Texels * Texels];
        public readonly bool[] Wet = new bool[Texels * Texels];
        /// <summary>Per slot: the tile it holds (canonical-free clip coordinates, in tiles), whether it is in, and its fill's version.</summary>
        public readonly (int X, int Z)[] Held = new (int, int)[Tiles * Tiles];
        public readonly byte[] State = new byte[Tiles * Tiles];
        public readonly int[] Version = new int[Tiles * Tiles];
        /// <summary>Per slot: its lowest and highest ground, for the blocks' bounds (a block culled by them, in view and in each shadow split).</summary>
        public readonly float[] Low = new float[Tiles * Tiles], High = new float[Tiles * Tiles];
        public readonly MeshInstance3D[] Blocks = new MeshInstance3D[16];
    }

    const byte Wanted = 0, Filling = 1, In = 2;

    sealed class TileJob
    {
        public int Level, Slot, Version, Tx, Tz, Generation;
        public float[] Data, Raw, Water;
        public Half[] Shape, Color;
        public bool[] Wet;
        public Exception Error;
    }

    Level[] _levels;
    PlanetTerrainSource _source;
    GroundPalette _palette;
    ShaderMaterial _material;
    int _width, _height;

    /// <summary>Clip coordinates are the frame's plus this: the whole planets the frame has moved since the world was set.</summary>
    double _shiftX, _shiftZ;
    Vector2 _eye;
    double _qx = double.NaN, _qz = double.NaN;
    int _floor = int.MaxValue;
    readonly ConcurrentQueue<TileJob> _done = new();
    readonly List<TileJob> _ready = new();
    readonly List<(int Level, int Slot, float Rank)> _wanted = new();
    int _inFlight, _generation;
    static readonly ArrayMesh BlockMesh = MakeBlock();
    RenderingDevice _rd;

    /// <summary>The levels of this world: how many rings.</summary>
    public int Levels => _levels?.Length ?? 0;

    /// <summary>The finest ring drawn (<see cref="Levels"/>: none yet). Every ring is drawn once it is 0.</summary>
    public int Floor => _floor;
    public bool Complete => _levels != null && _floor == 0;
    public bool Shown => _levels != null && _floor < _levels.Length;

    /// <summary>Tile fills waiting or running.</summary>
    public int Pending => _inFlight + _wanted.Count;

    /// <summary>The workers' time on tiles so far (stopwatch ticks), and how many: measurements read them.</summary>
    public static long FillTicks;
    public static int Filled;

    /// <summary>The most tiles filled at once.</summary>
    public int Workers = 3;

    /// <summary>
    /// How many rings a detail distance wants: the coarsest reaches well past it, so the horizon's own coarse mesh
    /// never takes over inside it.
    /// </summary>
    public static int LevelsFor(float distance)
    {
        int n = 4;
        while (n < 5 && Half * Spacing(n - 1) < distance + 1200f) n++;
        return n;
    }

    public void SetWorld(PlanetTerrainSource source, GroundPalette palette, int width, int height, ShaderMaterial material, int levels)
    {
        Clear();
        _source = source;
        _palette = palette;
        _width = width;
        _height = height;
        _material = material;
        _levels = new Level[levels];
        for (int l = 0; l < levels; l++)
        {
            var lv = _levels[l] = new Level { S = Spacing(l) };
            Array.Fill(lv.Held, (int.MinValue, int.MinValue));
            for (int b = 0; b < 16; b++)
            {
                var m = new MeshInstance3D
                {
                    Name = $"Ground {l} {b}", Mesh = BlockMesh, MaterialOverride = material, Visible = false,
                    PhysicsInterpolationMode = PhysicsInterpolationModeEnum.Off,
                    // The sun's real-time shadows reach a few hundred metres: the two finest rings cast them.
                    CastShadow = l <= 1 ? GeometryInstance3D.ShadowCastingSetting.On : GeometryInstance3D.ShadowCastingSetting.Off,
                };
                lv.Blocks[b] = m;
                AddChild(m);
            }
        }
        var bands = new Vector4[8];
        for (int l = 0; l < levels; l++)
        {
            var (from, to) = Band(l);
            bands[l] = new Vector4(from, to, Spacing(l), 0f);
        }
        material.SetShaderParameter("clip_ground", true);
        material.SetShaderParameter("clip_band", bands);
        material.SetShaderParameter("clip_top", levels - 1);
        material.SetShaderParameter("clip_texels", Texels);
        material.SetShaderParameter("clip_shift", Vector2.Zero);
        MakeTextures(levels);
    }

    /// <summary>The frame moved by whole planets (the eye crossed a seam), by this much: the ground keeps its place.</summary>
    public void Shift(float dx, float dz)
    {
        _shiftX -= dx;
        _shiftZ -= dz;
        _material?.SetShaderParameter("clip_shift", new Vector2((float)_shiftX, (float)_shiftZ));
        _qx = double.NaN; // the blocks are placed again
    }

    public void Clear()
    {
        if (_levels != null)
            foreach (Level lv in _levels)
                foreach (MeshInstance3D m in lv.Blocks) m?.QueueFree();
        _levels = null;
        _ready.Clear();
        _wanted.Clear();
        // Fills still running belong to the world before: they are dropped as they land.
        _done.Clear();
        _inFlight = 0;
        _generation++;
        _floor = int.MaxValue;
        _shiftX = _shiftZ = 0;
        _qx = _qz = double.NaN;
        FreeTextures();
    }

    // ---- Every frame --------------------------------------------------------------------------

    /// <summary>Lands finished tiles and sends them, asks for the tiles now in range, and lays out the rings round the eye (frame).</summary>
    public void Step(Vector3 eye)
    {
        if (_levels == null) return;
        _eye = new Vector2(eye.X, eye.Z);
        double ex = eye.X + _shiftX, ez = eye.Z + _shiftZ;
        Land();
        Send();
        Want(ex, ez);
        Start();
        Lay(ex, ez);
        _material.SetShaderParameter("clip_eye", _eye);
    }

    void Land()
    {
        while (_done.TryDequeue(out TileJob job))
        {
            if (job.Generation != _generation) continue;
            _inFlight--;
            Level lv = _levels[job.Level];
            if (lv.Version[job.Slot] != job.Version) continue; // the slot went to another tile meanwhile
            if (job.Error != null)
            {
                GD.PushError($"ground tile {job.Tx},{job.Tz} of level {job.Level} failed: {job.Error}");
                lv.State[job.Slot] = Wanted;
                continue;
            }
            int sx = job.Slot % Tiles * Tile, sz = job.Slot / Tiles * Tile;
            for (int j = 0; j < Tile; j++)
            {
                Array.Copy(job.Data, j * Tile * 4, lv.Data, ((sz + j) * Texels + sx) * 4, Tile * 4);
                Array.Copy(job.Shape, j * Tile * 4, lv.Shape, ((sz + j) * Texels + sx) * 4, Tile * 4);
                Array.Copy(job.Color, j * Tile * 4, lv.Color, ((sz + j) * Texels + sx) * 4, Tile * 4);
                Array.Copy(job.Raw, j * Tile, lv.Raw, (sz + j) * Texels + sx, Tile);
                Array.Copy(job.Water, j * Tile, lv.Water, (sz + j) * Texels + sx, Tile);
                Array.Copy(job.Wet, j * Tile, lv.Wet, (sz + j) * Texels + sx, Tile);
            }
            float low = float.MaxValue, high = float.MinValue;
            for (int k = 0; k < Tile * Tile; k++)
            {
                low = MathF.Min(low, job.Data[k * 4]);
                high = MathF.Max(high, job.Data[k * 4]);
            }
            (lv.Low[job.Slot], lv.High[job.Slot]) = (low, high);
            if (_rd == null) lv.State[job.Slot] = In; // no graphics card to send it to (headless): the copy here is all there is
            else _ready.Add(job);
        }
    }

    /// <summary>Up to <see cref="Uploads"/> landed tiles to the graphics card: side by side into a staging texture, then each copied into its slot.</summary>
    void Send()
    {
        if (_ready.Count == 0) return;
        int n = Math.Min(_ready.Count, Uploads), row = Tile * Uploads;
        var data = new float[row * Tile * 4];
        var shape = new Half[row * Tile * 4];
        var color = new Half[row * Tile * 4];
        var copies = new (uint Level, Vector3 To)[n];
        int sent = 0;
        for (int t = 0; t < n; t++)
        {
            TileJob job = _ready[t];
            Level lv = _levels[job.Level];
            if (lv.Version[job.Slot] != job.Version) continue;
            for (int j = 0; j < Tile; j++)
            {
                Array.Copy(job.Data, j * Tile * 4, data, (j * row + sent * Tile) * 4, Tile * 4);
                Array.Copy(job.Shape, j * Tile * 4, shape, (j * row + sent * Tile) * 4, Tile * 4);
                Array.Copy(job.Color, j * Tile * 4, color, (j * row + sent * Tile) * 4, Tile * 4);
            }
            copies[sent++] = ((uint)job.Level, new Vector3(job.Slot % Tiles * Tile, job.Slot / Tiles * Tile, 0f));
            lv.State[job.Slot] = In;
        }
        _ready.RemoveRange(0, n);
        if (sent == 0) return;
        RenderingDevice rd = _rd;
        (Rid Tex, Rid Stage)[] targets = _textures;
        RenderingServer.CallOnRenderThread(Callable.From(() =>
        {
            rd.TextureUpdate(targets[0].Stage, 0, MemoryMarshal.AsBytes(data.AsSpan()));
            rd.TextureUpdate(targets[1].Stage, 0, MemoryMarshal.AsBytes(shape.AsSpan()));
            rd.TextureUpdate(targets[2].Stage, 0, MemoryMarshal.AsBytes(color.AsSpan()));
            var size = new Vector3(Tile, Tile, 1f);
            for (int t = 0; t < sent; t++)
                foreach (var (tex, stage) in targets)
                    rd.TextureCopy(stage, tex, new Vector3(t * Tile, 0f, 0f), copies[t].To, size, 0, 0, 0, copies[t].Level);
        }));
    }

    /// <summary>Each level's tiles round the eye (clip coordinates): a slot whose tile has gone takes the one come into range.</summary>
    void Want(double ex, double ez)
    {
        _wanted.Clear();
        for (int l = 0; l < _levels.Length; l++)
        {
            Level lv = _levels[l];
            double size = Tile * lv.S;
            int cx = (int)Math.Floor(ex / size), cz = (int)Math.Floor(ez / size);
            for (int tz = cz - Tiles / 2; tz < cz + Tiles / 2; tz++)
            for (int tx = cx - Tiles / 2; tx < cx + Tiles / 2; tx++)
            {
                int slot = Mod(tz, Tiles) * Tiles + Mod(tx, Tiles);
                if (lv.Held[slot] != (tx, tz))
                {
                    lv.Held[slot] = (tx, tz);
                    lv.State[slot] = Wanted;
                    lv.Version[slot]++;
                }
                if (lv.State[slot] != Wanted) continue;
                // Nearest first, by its own texels; a tile the rings need now before any, the coarsest first (so the
                // land shows whole as soon as it can, and sharpens from there).
                double mx = (tx + 0.5) * size - ex, mz = (tz + 0.5) * size - ez;
                float away = (float)(Math.Max(Math.Abs(mx), Math.Abs(mz)) / size);
                bool now = away < (Half + Snap / lv.S) / Tile + 1f;
                _wanted.Add((l, slot, now ? (_levels.Length - l) * 100f + away : 1000f + away));
            }
        }
    }

    void Start()
    {
        if (_inFlight >= Workers || _wanted.Count == 0) return;
        _wanted.Sort(static (a, b) => a.Rank.CompareTo(b.Rank));
        foreach (var (l, slot, _) in _wanted)
        {
            if (_inFlight >= Workers) break;
            Level lv = _levels[l];
            lv.State[slot] = Filling;
            _inFlight++;
            var job = new TileJob { Level = l, Slot = slot, Version = lv.Version[slot], Tx = lv.Held[slot].X, Tz = lv.Held[slot].Z, Generation = _generation };
            PlanetTerrainSource source = _source;
            GroundPalette palette = _palette;
            int w = _width, h = _height;
            float s = lv.S;
            Task.Run(() =>
            {
                Fill(job, s, source, palette, w, h);
                _done.Enqueue(job);
            });
        }
    }

    /// <summary>
    /// The rings round the eye: every level centred on the same snapped point, drawn from the coarsest down to the finest whose
    /// texture holds all of its ring (each ring's hole is the next finer one's; the finest drawn fills its own).
    /// </summary>
    void Lay(double ex, double ez)
    {
        double qx = Snap * Math.Round(ex / Snap), qz = Snap * Math.Round(ez / Snap);
        int floor = _levels.Length;
        for (int l = _levels.Length - 1; l >= 0 && Holds(l, qx, qz); l--) floor = l;
        if (floor == _floor && qx == _qx && qz == _qz) return;
        _floor = floor;
        _qx = qx;
        _qz = qz;
        for (int l = 0; l < _levels.Length; l++)
        {
            Level lv = _levels[l];
            float s = lv.S;
            double x0 = qx - Half * s - _shiftX, z0 = qz - Half * s - _shiftZ;
            for (int b = 0; b < 16; b++)
            {
                int bi = b % 4, bj = b / 4;
                bool inner = bi is 1 or 2 && bj is 1 or 2;
                MeshInstance3D m = lv.Blocks[b];
                m.Visible = l >= floor && (!inner || l == floor);
                if (!m.Visible) continue;
                m.Transform = new Transform3D(Basis.FromScale(new Vector3(s, s, s)), new Vector3((float)(x0 + bi * Block * s), 0f, (float)(z0 + bj * Block * s)));
                // The block is flat: its bounds are its ground's, and the next ring's it takes toward its edge, with room for the
                // round world's sinking far off.
                int gx = (int)Math.Round(qx / s) - Half + bi * Block, gz = (int)Math.Round(qz / s) - Half + bj * Block;
                var (low, high) = Range(l, gx, gz, Block);
                if (l + 1 < _levels.Length)
                {
                    var (low2, high2) = Range(l + 1, FloorDiv(gx, 2), FloorDiv(gz, 2), Block / 2);
                    (low, high) = (MathF.Min(low, low2), MathF.Max(high, high2));
                }
                float room = 4f + TerrainStreamer.RoundSink(Half * s * 1.5f);
                m.CustomAabb = new Aabb(new Vector3(0f, (low - room) / s, 0f), new Vector3(Block, (high - low + 2f * room) / s, Block));
            }
        }
    }

    /// <summary>
    /// A far chunk's grid samples (<see cref="FarJob.Height"/>) from the ground a level holds, if all of it is in: (nx + 2) by
    /// (nz + 2) points the level's spacing apart, the first a step before a point of the frame. False when any of it isn't.
    /// </summary>
    public bool Samples(int level, Vector2 at, int nx, int nz, out float[] height, out float[] riverLevel, out bool[] river)
    {
        height = riverLevel = null;
        river = null;
        if (_levels == null || level >= _levels.Length) return false;
        Level lv = _levels[level];
        int gx0 = (int)Math.Round((at.X + _shiftX) / lv.S) - 1, gz0 = (int)Math.Round((at.Y + _shiftZ) / lv.S) - 1, g = nx + 2, rows = nz + 2;
        for (int tz = FloorDiv(gz0, Tile); tz <= FloorDiv(gz0 + rows - 1, Tile); tz++)
        for (int tx = FloorDiv(gx0, Tile); tx <= FloorDiv(gx0 + g - 1, Tile); tx++)
        {
            int slot = Mod(tz, Tiles) * Tiles + Mod(tx, Tiles);
            if (lv.Held[slot] != (tx, tz) || lv.State[slot] != In) return false;
        }
        height = new float[g * rows];
        riverLevel = new float[g * rows];
        river = new bool[g * rows];
        for (int j = 0; j < rows; j++)
        for (int i = 0; i < g; i++)
        {
            int t = Texel(gx0 + i, gz0 + j), k = j * g + i;
            (height[k], riverLevel[k], river[k]) = (lv.Raw[t], lv.Water[t], lv.Wet[t]);
        }
        return true;
    }
    /// <summary>Whether a level's texture holds all of its ring round a centre (clip coordinates), and the texel round it the normals read.</summary>
    bool Holds(int l, double qx, double qz)
    {
        Level lv = _levels[l];
        int gx = (int)Math.Round(qx / lv.S), gz = (int)Math.Round(qz / lv.S);
        int t0x = FloorDiv(gx - Half - 1, Tile), t1x = FloorDiv(gx + Half + 1, Tile);
        int t0z = FloorDiv(gz - Half - 1, Tile), t1z = FloorDiv(gz + Half + 1, Tile);
        for (int tz = t0z; tz <= t1z; tz++)
        for (int tx = t0x; tx <= t1x; tx++)
        {
            int slot = Mod(tz, Tiles) * Tiles + Mod(tx, Tiles);
            if (lv.Held[slot] != (tx, tz) || lv.State[slot] != In) return false;
        }
        return true;
    }

    /// <summary>The lowest and highest ground a level holds over grid points (gx..gx+n, gz..gz+n) and their neighbours, from its tiles.</summary>
    (float Low, float High) Range(int l, int gx, int gz, int n)
    {
        Level lv = _levels[l];
        float low = float.MaxValue, high = float.MinValue;
        for (int tz = FloorDiv(gz - 1, Tile); tz <= FloorDiv(gz + n + 1, Tile); tz++)
        for (int tx = FloorDiv(gx - 1, Tile); tx <= FloorDiv(gx + n + 1, Tile); tx++)
        {
            int slot = Mod(tz, Tiles) * Tiles + Mod(tx, Tiles);
            if (lv.Held[slot] != (tx, tz) || lv.State[slot] != In) continue;
            low = MathF.Min(low, lv.Low[slot]);
            high = MathF.Max(high, lv.High[slot]);
        }
        return low <= high ? (low, high) : (-400f, 1500f);
    }
    // ---- Filling a tile (on a worker) --------------------------------------------------------

    /// <summary>Grid points worked out round a tile beyond its own (with the sampled ring past them), so its edge points are shaped as the tile beside it shapes them.</summary>
    const int Margin = 1;

    /// <summary>
    /// One tile of a level from the terrain source, shaped as the far rings always were (<see cref="FarBuilder.Shape"/>): the
    /// ground at the level's spacing (its finer octaves left out), each point slid onto the cliff beside it and sampled again
    /// there, a cliff's points lit by the faces round them, coloured by the palette's soil-to-rock rule, with how far the ground
    /// rises and falls within 16 m. Its squares are split along the blocks' one diagonal.
    /// </summary>
    static void Fill(TileJob job, float s, PlanetTerrainSource source, GroundPalette palette, int width, int height)
    {
        long start = Stopwatch.GetTimestamp();
        try
        {
            const int N = Tile + 2 * Margin;
            var grid = new FarBuilder.Grid(N, N, -Margin);
            int g = grid.G;
            var h = new float[g * g];
            var water = new float[g * g];
            var wet = new bool[g * g];
            double x0 = Mathx.WrapX(((double)job.Tx * Tile - Margin) * s, width), z0 = Mathx.Wrap(((double)job.Tz * Tile - Margin) * s, height);
            for (int j = 0; j < g; j++)
            for (int i = 0; i < g; i++)
            {
                GroundSample sample = source.Ground(x0 + (i - 1) * s, z0 + (j - 1) * s, s);
                h[j * g + i] = sample.Height;
                water[j * g + i] = sample.RiverLevel;
                wet[j * g + i] = sample.InRiver;
            }
            long t0 = start; FarBuilder.Prof(ref t0, 6); // PROFILE (temporary)
            int span = (N - 1) * (int)s;
            FarBuilder.Surface surface = FarBuilder.Shape(grid, span, span, s, x0, z0, h, wet, source, palette, Margin);
            job.Data = new float[Tile * Tile * 4];
            job.Shape = new Half[Tile * Tile * 4];
            job.Color = new Half[Tile * Tile * 4];
            job.Raw = new float[Tile * Tile];
            job.Water = new float[Tile * Tile];
            job.Wet = new bool[Tile * Tile];
            for (int j = 0; j < Tile; j++)
            for (int i = 0; i < Tile; i++)
            {
                int k = (j + Margin) * N + i + Margin, o = (j * Tile + i) * 4;
                Vector3 v = surface.Vertices[k], n = surface.Normals[k];
                Vector2 relief = surface.Relief[k];
                Color c = surface.Colors[k];
                job.Data[o] = v.Y;
                job.Data[o + 1] = relief.X;
                job.Data[o + 2] = relief.Y;
                job.Data[o + 3] = surface.Flags[k].X;
                job.Shape[o] = (Half)n.X;
                job.Shape[o + 1] = (Half)n.Z;
                job.Shape[o + 2] = (Half)(v.X / s - (i + Margin));
                job.Shape[o + 3] = (Half)(v.Z / s - (j + Margin));
                job.Color[o] = (Half)c.R;
                job.Color[o + 1] = (Half)c.G;
                job.Color[o + 2] = (Half)c.B;
                job.Color[o + 3] = (Half)c.A;
                int r = (j + Margin + 1) * g + i + Margin + 1;
                (job.Raw[o / 4], job.Water[o / 4], job.Wet[o / 4]) = (h[r], water[r], wet[r]);
            }
        }
        catch (Exception e)
        {
            job.Error = e;
        }
        long t1 = start; FarBuilder.Prof(ref t1, 7); // PROFILE (temporary)
        Interlocked.Add(ref FillTicks, Stopwatch.GetTimestamp() - start);
        Interlocked.Increment(ref Filled);
    }

    // ---- The ground as drawn (the shader's vertex stage, on the CPU) ------------------------

    /// <summary>A level's point as its own grid has it, in the frame: position (slid), normal and how bare it is.</summary>
    (Vector3 Pos, Vector3 Normal, float Bare) Own(int l, int gx, int gz)
    {
        Level lv = _levels[l];
        float s = lv.S;
        int t = Texel(gx, gz) * 4;
        float nx = (float)lv.Shape[t], nz = (float)lv.Shape[t + 1];
        Vector2 at = new Vector2(gx + (float)lv.Shape[t + 2], gz + (float)lv.Shape[t + 3]) * s - new Vector2((float)_shiftX, (float)_shiftZ);
        return (new Vector3(at.X, lv.Data[t], at.Y), new Vector3(nx, MathF.Sqrt(MathF.Max(0f, 1f - nx * nx - nz * nz)), nz), (float)lv.Color[t + 3]);
    }

    /// <summary>A drawn vertex, morphed toward the next level's grid as terrain.gdshader does it (by where its grid point stands).</summary>
    (Vector3 Pos, Vector3 Normal, float Bare) Vertex(int l, int gx, int gz)
    {
        var p = Own(l, gx, gz);
        float s = _levels[l].S;
        float k = Morph(l, (float)(gx * (double)s - _shiftX), (float)(gz * (double)s - _shiftZ));
        if (k <= 0f) return p;
        var q = Own(l + 1, (gx - (gx & 1)) / 2, (gz - (gz & 1)) / 2);
        if (k >= 1f) return q;
        return (p.Pos.Lerp(q.Pos, k), p.Normal.Lerp(q.Normal, k).Normalized(), Mathf.Lerp(p.Bare, q.Bare, k));
    }

    float Morph(int l, float x, float z)
    {
        if (l >= _levels.Length - 1) return 0f;
        var (from, to) = Band(l);
        float t = Math.Clamp((MathF.Max(MathF.Abs(x - _eye.X), MathF.Abs(z - _eye.Y)) - from) / (to - from), 0f, 1f);
        return t * t * (3f - 2f * t);
    }

    static int Texel(int gx, int gz) => Mod(gz, Texels) * Texels + Mod(gx, Texels);

    /// <summary>A level's ring as drawn, in the frame: x0, z0, x1, z1. Zero while it isn't.</summary>
    public Vector4 Ring(int l)
    {
        if (_levels == null || l < _floor || l >= _levels.Length) return Vector4.Zero;
        float r = Half * _levels[l].S;
        float x = (float)(_qx - _shiftX), z = (float)(_qz - _shiftZ);
        return new Vector4(x - r, z - r, x + r, z + r);
    }

    /// <summary>The outer edge of the drawn rings (where the horizon takes over), in the frame. Zero while none is drawn.</summary>
    public Vector4 Outer => Shown ? Ring(_levels.Length - 1) : Vector4.Zero;

    /// <summary>The level drawn at a point of the frame, or -1.</summary>
    int LevelAt(float x, float z)
    {
        if (!Shown) return -1;
        for (int l = _floor; l < _levels.Length; l++)
        {
            Vector4 r = Ring(l);
            if (x >= r.X && x <= r.Z && z >= r.Y && z <= r.W) return l;
        }
        return -1;
    }

    /// <summary>Whether the rings draw ground over a point of the frame.</summary>
    public bool Covers(Vector3 p) => LevelAt(p.X, p.Z) >= 0;

    /// <summary>The height of the ground as drawn under a point of the frame (the triangle over it, morphed), or null where none is.</summary>
    public float? Height(float x, float z)
    {
        int l = LevelAt(x, z);
        if (l < 0) return null;
        float s = _levels[l].S;
        return Over((gx, gz) => Vertex(l, gx, gz).Pos, (int)Math.Floor((x + _shiftX) / s), (int)Math.Floor((z + _shiftZ) / s), x, z);
    }

    /// <summary>
    /// The height of a grid's triangles (each square split b-c) over a point, searched in the squares round grid square (ix, iz):
    /// a slid point may have taken its triangle into the square beside. Null where none is over it.
    /// </summary>
    static float? Over(Func<int, int, Vector3> vertex, int ix, int iz, float x, float z)
    {
        for (int dj = -1; dj <= 1; dj++)
        for (int di = -1; di <= 1; di++)
        {
            int gx = ix + di, gz = iz + dj;
            Vector3 a = vertex(gx, gz), b = vertex(gx + 1, gz), c = vertex(gx, gz + 1), d = vertex(gx + 1, gz + 1);
            if (Inside(a, b, c, x, z) is float y1) return y1;
            if (Inside(b, d, c, x, z) is float y2) return y2;
        }
        return null;
    }

    /// <summary>
    /// One level's ground anywhere on the planet (canonical metres), as its own grid draws it (unmorphed), shaped tile by
    /// tile as the workers shape it: tests hold the water and the near ground to it.
    /// </summary>
    public sealed class Reader(int level, PlanetTerrainSource source, GroundPalette palette, int width, int height)
    {
        readonly float _s = Spacing(level);
        readonly Dictionary<(int, int), TileJob> _tiles = new();

        Vector3 Point(int gx, int gz)
        {
            int tx = FloorDiv(gx, Tile), tz = FloorDiv(gz, Tile);
            if (!_tiles.TryGetValue((tx, tz), out TileJob t))
            {
                t = new TileJob { Level = level, Tx = tx, Tz = tz };
                Fill(t, _s, source, palette, width, height);
                if (t.Error != null) throw t.Error;
                _tiles[(tx, tz)] = t;
            }
            int o = ((gz - tz * Tile) * Tile + gx - tx * Tile) * 4;
            return new Vector3((gx + (float)t.Shape[o + 2]) * _s, t.Data[o], (gz + (float)t.Shape[o + 3]) * _s);
        }

        public float Height(double x, double z) => Over(Point, (int)Math.Floor(x / _s), (int)Math.Floor(z / _s), (float)x, (float)z) ?? float.NaN;
    }

    static float? Inside(Vector3 a, Vector3 b, Vector3 c, float x, float z)
    {
        float det = (b.X - a.X) * (c.Z - a.Z) - (c.X - a.X) * (b.Z - a.Z);
        if (MathF.Abs(det) < 1e-6f) return null;
        float u = ((b.X - x) * (c.Z - z) - (c.X - x) * (b.Z - z)) / det, v = ((c.X - x) * (a.Z - z) - (a.X - x) * (c.Z - z)) / det, w = 1f - u - v;
        if (u < -1e-4f || v < -1e-4f || w < -1e-4f) return null;
        return u * a.Y + v * b.Y + w * c.Y;
    }

    /// <summary>The drawn rings' triangles within a radius of a point of the frame, block by block, as the shader lays them (tests fire rays at them).</summary>
    public IEnumerable<(Vector3[] Vertices, int[] Indices, Vector3[] Normals, float[] Bare)> Drawn(Vector3 near, float radius)
    {
        if (!Shown) yield break;
        for (int l = _floor; l < _levels.Length; l++)
        {
            float s = _levels[l].S;
            int g0x = (int)Math.Round(_qx / s) - Half, g0z = (int)Math.Round(_qz / s) - Half;
            for (int b = 0; b < 16; b++)
            {
                int bi = b % 4, bj = b / 4;
                if (l > _floor && bi is 1 or 2 && bj is 1 or 2) continue;
                int bx = g0x + bi * Block, bz = g0z + bj * Block;
                float cx = (float)((bx + Block / 2) * (double)s - _shiftX), cz = (float)((bz + Block / 2) * (double)s - _shiftZ);
                if (MathF.Max(MathF.Abs(cx - near.X), MathF.Abs(cz - near.Z)) > radius + Block * s * 0.5f) continue;
                const int N = Block + 1;
                var v = new Vector3[N * N];
                var normals = new Vector3[N * N];
                var bare = new float[N * N];
                for (int j = 0; j < N; j++)
                for (int i = 0; i < N; i++)
                    (v[j * N + i], normals[j * N + i], bare[j * N + i]) = Vertex(l, bx + i, bz + j);
                var indices = new List<int>(Block * Block * 6);
                void Tri(int p, int q, int r)
                {
                    Vector3 e1 = v[q] - v[p], e2 = v[r] - v[p];
                    if (e1.Cross(e2).LengthSquared() < 1e-10f) return; // collapsed by the morph
                    indices.Add(p); indices.Add(q); indices.Add(r);
                }
                for (int j = 0; j < Block; j++)
                for (int i = 0; i < Block; i++)
                {
                    int a = j * N + i, bb = a + 1, c = a + N, d = c + 1;
                    Tri(a, bb, c);
                    Tri(bb, d, c);
                }
                yield return (v, indices.ToArray(), normals, bare);
            }
        }
    }

    // ---- The graphics card's side ------------------------------------------------------------

    /// <summary>One block: a flat grid of quads a unit apart, split along the same diagonal everywhere (the next level's coarse quads keep it).</summary>
    static ArrayMesh MakeBlock()
    {
        const int N = Block + 1;
        var v = new Vector3[N * N];
        var n = new Vector3[N * N];
        for (int j = 0; j < N; j++)
        for (int i = 0; i < N; i++)
        {
            v[j * N + i] = new Vector3(i, 0f, j);
            n[j * N + i] = Vector3.Up;
        }
        var indices = new int[Block * Block * 6];
        int t = 0;
        for (int j = 0; j < Block; j++)
        for (int i = 0; i < Block; i++)
        {
            int a = j * N + i, b = a + 1, c = a + N, d = c + 1;
            indices[t++] = a; indices[t++] = b; indices[t++] = c;
            indices[t++] = b; indices[t++] = d; indices[t++] = c;
        }
        var arrays = new Godot.Collections.Array();
        arrays.Resize((int)Mesh.ArrayType.Max);
        arrays[(int)Mesh.ArrayType.Vertex] = v;
        arrays[(int)Mesh.ArrayType.Normal] = n;
        arrays[(int)Mesh.ArrayType.Color] = new Color[N * N];
        arrays[(int)Mesh.ArrayType.TexUV] = new Vector2[N * N];
        arrays[(int)Mesh.ArrayType.TexUV2] = new Vector2[N * N];
        arrays[(int)Mesh.ArrayType.Index] = indices;
        var mesh = new ArrayMesh();
        mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays);
        return mesh;
    }

    /// <summary>The shader's three textures (data, shape, colour) as they are named there, and each one's texel format.</summary>
    static readonly (string Name, RenderingDevice.DataFormat Format)[] Channels =
    {
        ("clip_data", RenderingDevice.DataFormat.R32G32B32A32Sfloat),
        ("clip_shape", RenderingDevice.DataFormat.R16G16B16A16Sfloat),
        ("clip_color", RenderingDevice.DataFormat.R16G16B16A16Sfloat),
    };

    /// <summary>Each channel's texture array on the graphics card, and the staging texture tiles are sent through.</summary>
    (Rid Tex, Rid Stage)[] _textures;

    void MakeTextures(int levels)
    {
        _rd = RenderingServer.GetRenderingDevice();
        if (_rd == null) return;
        const RenderingDevice.TextureUsageBits Usage = RenderingDevice.TextureUsageBits.SamplingBit | RenderingDevice.TextureUsageBits.CanUpdateBit | RenderingDevice.TextureUsageBits.CanCopyToBit;
        const RenderingDevice.TextureUsageBits Staging = RenderingDevice.TextureUsageBits.CanUpdateBit | RenderingDevice.TextureUsageBits.CanCopyFromBit;
        _textures = new (Rid, Rid)[Channels.Length];
        for (int c = 0; c < Channels.Length; c++)
        {
            var format = Channels[c].Format;
            Rid tex = _rd.TextureCreate(new RDTextureFormat
            {
                Format = format, TextureType = RenderingDevice.TextureType.Type2DArray, Width = Texels, Height = Texels, Depth = 1, ArrayLayers = (uint)levels, Mipmaps = 1, UsageBits = Usage,
            }, new RDTextureView());
            Rid stage = _rd.TextureCreate(new RDTextureFormat
            {
                Format = format, TextureType = RenderingDevice.TextureType.Type2D, Width = Tile * Uploads, Height = Tile, Depth = 1, ArrayLayers = 1, Mipmaps = 1, UsageBits = Staging,
            }, new RDTextureView());
            _textures[c] = (tex, stage);
            _material.SetShaderParameter(Channels[c].Name, new Texture2DArrayRD { TextureRdRid = tex });
        }
    }

    void FreeTextures()
    {
        if (_rd == null) return;
        foreach (var (name, _) in Channels) _material?.SetShaderParameter(name, default(Variant));
        foreach (var (tex, stage) in _textures)
        {
            _rd.FreeRid(stage);
            _rd.FreeRid(tex);
        }
        _textures = null;
        _rd = null;
    }
    public override void _ExitTree() => Clear();

    static int Mod(int v, int m) => (v % m + m) % m;
    static int FloorDiv(int v, int d) => (int)Math.Floor(v / (double)d);
}
