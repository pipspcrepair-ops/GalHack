using System;
using System.Collections.Generic;

namespace GalHack.Structures;

/// <summary>
/// What a core is set to run as: anchored (Base), riding its hover plates near the ground (Hover)
/// or flying on its thrusters (Flight). Every structure has all three; what a mode lacks is only a
/// warning. Every block works in every mode; only what the blocks do changes.
/// </summary>
public enum CoreMode { Base, Hover, Flight }

public static class CoreModes
{
    /// <summary>A mode by name, old data's included: "capital" and "vessel" were the two flying modes and read as Flight.</summary>
    public static bool TryParse(string name, out CoreMode mode)
    {
        switch (name?.ToLowerInvariant())
        {
            case "base": mode = CoreMode.Base; return true;
            case "hover": mode = CoreMode.Hover; return true;
            case "flight" or "capital" or "vessel": mode = CoreMode.Flight; return true;
            default: mode = CoreMode.Flight; return false;
        }
    }
}

[Flags]
public enum BlockFlags : byte
{
    None = 0,
    /// <summary>A door that stands open.</summary>
    Open = 1,
    /// <summary>A light (or other component) switched off.</summary>
    Off = 2,
}

/// <summary>One placed block. A small struct so a chunk is a flat array the mesher can scan quickly.</summary>
public struct Block
{
    /// <summary>Index into <see cref="Catalogue.Types"/>; 0 means the cell is empty.</summary>
    public ushort Type;
    /// <summary>Index into <see cref="Catalogue.Shapes"/>. Armour only; components have their own parts.</summary>
    public byte Shape;
    /// <summary>One of the 24 <see cref="Orientations"/>.</summary>
    public byte Rot;
    public BlockFlags Flags;
    /// <summary>
    /// A filler's offset to its block's anchor, in cells (see <see cref="Footprint"/>). Zero on
    /// an anchor and on every one-cell block.
    /// </summary>
    public sbyte Ox, Oy, Oz;
    public float Hp;
    public FacePaint Paint;

    public readonly bool IsEmpty => Type == 0;
    public readonly bool Open => (Flags & BlockFlags.Open) != 0;
    public readonly bool Off => (Flags & BlockFlags.Off) != 0;

    /// <summary>One cell of a block that spans several, other than its anchor: it only points back.</summary>
    public readonly bool IsFiller => Ox != 0 || Oy != 0 || Oz != 0;

    /// <summary>From this cell to its block's anchor (zero on the anchor).</summary>
    public readonly Int3 ToAnchor => new(Ox, Oy, Oz);

    /// <summary>Same block apart from damage: used to merge runs in blueprints.</summary>
    public readonly bool SameAs(in Block o) =>
        Type == o.Type && Shape == o.Shape && Rot == o.Rot && Flags == o.Flags && Hp == o.Hp && Paint.Equals(o.Paint)
        && Ox == o.Ox && Oy == o.Oy && Oz == o.Oz;
}

/// <summary>16×16×16 cells. Structures store blocks in chunks so meshing and rebuilding stay local.</summary>
public sealed class Chunk
{
    public const int Bits = 4, Size = 1 << Bits, Mask = Size - 1, Volume = Size * Size * Size;
    public readonly Block[] Cells = new Block[Volume];
    public int Count;

    public static int Index(int x, int y, int z) => (x & Mask) | ((y & Mask) << Bits) | ((z & Mask) << (2 * Bits));
}

/// <summary>
/// The canonical structure model: a grid of blocks with one core. Ships, bases, POIs and wrecks
/// are all structures. It has no engine dependencies; the presentation layer watches
/// <see cref="DirtyChunks"/> and <see cref="Version"/> and redraws what changed.
///
/// Coordinates are cells. Cell (0, 0, 0) is centred on the structure's origin; +Y is up and -Z
/// is forward, matching Godot.
/// </summary>
public sealed class Structure
{
    public Catalogue Catalogue { get; }
    public GridDef Grid => Catalogue.Grid;
    public string Name = "Structure";

    /// <summary>Who owns it, set by placing the core. Null for an unowned wreck.</summary>
    public string Owner;

    /// <summary>The core's mode.</summary>
    public CoreMode Mode = CoreMode.Flight;

    /// <summary>The core's artificial gravity switch. It also needs power to work (see <see cref="StructureStats"/>).</summary>
    public bool GravityOn = true;

    /// <summary>Cargo in the shared storage, in storage units.</summary>
    public double CargoSu;

    public PaintPalette Palette { get; } = new();

    /// <summary>Chunks whose blocks (or whose neighbours' blocks) changed since the presentation last looked.</summary>
    public readonly HashSet<Int3> DirtyChunks = new();

    /// <summary>Bumped on every edit, so derived data (stats, rooms) knows when to recompute.</summary>
    public int Version { get; private set; }

    /// <summary>Occupied cells. A block that spans several cells counts each of them (<see cref="BlockCount"/> counts it once).</summary>
    public int Count { get; private set; }

    /// <summary>Blocks: one per anchor, however many cells each spans.</summary>
    public int BlockCount => Count - _fillers;

    /// <summary>Blocks aboard that span several cells. While there are none, nothing pays for them.</summary>
    public int MultiCellBlocks { get; private set; }

    int _fillers;
    public Int3? Core { get; private set; }
    public bool IsWreck => Core == null;

    readonly Dictionary<Int3, Chunk> _chunks = new();
    int _boundsVersion = -1;
    Int3 _min, _max;

    public Structure(Catalogue catalogue) => Catalogue = catalogue;

    public IReadOnlyDictionary<Int3, Chunk> Chunks => _chunks;

    public static Int3 ChunkOf(Int3 p) => new(p.X >> Chunk.Bits, p.Y >> Chunk.Bits, p.Z >> Chunk.Bits);
    public static Int3 ChunkOrigin(Int3 chunk) => chunk * Chunk.Size;

    public bool TryGet(Int3 p, out Block block)
    {
        if (_chunks.TryGetValue(ChunkOf(p), out Chunk chunk))
        {
            block = chunk.Cells[Chunk.Index(p.X, p.Y, p.Z)];
            return !block.IsEmpty;
        }
        block = default;
        return false;
    }

    public Block Get(Int3 p) => TryGet(p, out Block b) ? b : default;
    public bool Has(Int3 p) => TryGet(p, out _);

    /// <summary>The anchor of the block that holds this cell: the cell itself unless it is a filler.</summary>
    public Int3 AnchorOf(Int3 p) => TryGet(p, out Block b) ? p + b.ToAnchor : p;

    /// <summary>The block that holds a cell, read at its anchor (a filler reads as its whole block).</summary>
    public bool TryGetBlock(Int3 p, out Int3 anchor, out Block block)
    {
        if (!TryGet(p, out block)) { anchor = p; return false; }
        anchor = p + block.ToAnchor;
        if (block.IsFiller && !TryGet(anchor, out block)) return false;
        return true;
    }

    /// <summary>Every cell a block at <paramref name="anchor"/> fills, anchor first.</summary>
    public IEnumerable<Int3> CellsOf(Int3 anchor, in Block b) => Footprint.Cells(anchor, Catalogue.Types[b.Type], b.Rot);

    /// <summary>A point given in a block's -0.5..0.5 box (a chair's seat or eye), in the structure's cells.</summary>
    public System.Numerics.Vector3 PointOf(Int3 anchor, in Block b, System.Numerics.Vector3 box) =>
        anchor.ToVector3() + Orientations.Matrix(b.Rot).Apply(Footprint.PartPoint(Catalogue.Types[b.Type], box));

    /// <summary>The middle of a block in cell units: its anchor's centre, or the middle of all its cells.</summary>
    public System.Numerics.Vector3 CentreOf(Int3 anchor, in Block b) =>
        anchor.ToVector3() + Footprint.Centre(Catalogue.Types[b.Type], b.Rot);

    /// <summary>
    /// Writes a block with no rules at all (placement rules live in <see cref="Building"/>). Pass
    /// an empty block to clear it. A block that spans several cells is written whole from its
    /// anchor, fillers and all; clearing (or overwriting) any of its cells clears all of them.
    /// </summary>
    public void Set(Int3 p, Block block)
    {
        bool multi = !block.IsEmpty && !block.IsFiller && Catalogue.Types[block.Type].Multi;
        if (MultiCellBlocks > 0 || _fillers > 0)
        {
            // Whatever block held this cell goes first, whole, unless this rewrites that same block's cell.
            if (TryGet(p, out Block old) && InMulti(old) && !(block.IsFiller && block.Type == old.Type && block.ToAnchor == old.ToAnchor))
                ClearBlock(p + old.ToAnchor);
        }
        if (multi)
            foreach (Int3 cell in CellsOf(p, block))
            {
                if (cell == p) continue;
                if (TryGet(cell, out Block there) && InMulti(there) && cell + there.ToAnchor != p) ClearBlock(cell + there.ToAnchor);
                SetCell(cell, Footprint.Filler(block, p, cell));
            }
        SetCell(p, block);
    }

    bool InMulti(in Block b) => b.IsFiller || Catalogue.Types[b.Type].Multi;

    /// <summary>Clears every cell still pointing at <paramref name="anchor"/>: a whole block, or what's left of a broken one.</summary>
    void ClearBlock(Int3 anchor)
    {
        if (!TryGet(anchor, out Block a)) return;
        foreach (Int3 cell in CellsOf(anchor, a))
            if (TryGet(cell, out Block f) && f.Type == a.Type && cell + f.ToAnchor == anchor) SetCell(cell, default);
    }

    /// <summary>Writes one cell, nothing else.</summary>
    void SetCell(Int3 p, Block block)
    {
        Int3 c = ChunkOf(p);
        if (!_chunks.TryGetValue(c, out Chunk chunk))
        {
            if (block.IsEmpty) return;
            chunk = new Chunk();
            _chunks[c] = chunk;
        }

        ref Block cell = ref chunk.Cells[Chunk.Index(p.X, p.Y, p.Z)];
        if (_lights.Count > 0 && (block.IsEmpty || !Catalogue.Types[block.Type].IsLight)) _lights.Remove(p);
        bool was = !cell.IsEmpty, now = !block.IsEmpty;
        if (was && Core == p) Core = null;
        if (was && cell.IsFiller) _fillers--;
        else if (was && Catalogue.Types[cell.Type].Multi) MultiCellBlocks--;
        cell = block;
        if (now && block.IsFiller) _fillers++;
        else if (now && Catalogue.Types[block.Type].Multi) MultiCellBlocks++;
        if (now && !block.IsFiller && Catalogue.Types[block.Type].IsCore) Core = p;

        if (!was && now) { chunk.Count++; Count++; }
        else if (was && !now)
        {
            chunk.Count--;
            Count--;
            if (chunk.Count == 0) _chunks.Remove(c);
        }
        MarkDirty(p);
    }

    public void Clear(Int3 p) => Set(p, default);

    readonly Dictionary<Int3, LightSettings> _lights = new();

    /// <summary>The lights given a colour or animation (the rest glow their block's own colour, steady).</summary>
    public IReadOnlyDictionary<Int3, LightSettings> LightStyles => _lights;

    /// <summary>A light block's colour and animation: what was set, else its type's glow colour, steady.</summary>
    public LightSettings LightAt(Int3 p) =>
        _lights.TryGetValue(p, out LightSettings s) ? s : TryGet(p, out Block b) ? LightSettings.Steady(Catalogue.Types[b.Type].GlowColor) : LightSettings.Steady(0xffffff);

    /// <summary>Sets a light block's colour and animation. False if the cell is not a light.</summary>
    public bool SetLight(Int3 p, LightSettings settings)
    {
        if (!TryGet(p, out Block b) || !Catalogue.Types[b.Type].IsLight) return false;
        if (settings == LightSettings.Steady(Catalogue.Types[b.Type].GlowColor)) _lights.Remove(p); else _lights[p] = settings;
        MarkDirty(p);
        return true;
    }

    /// <summary>
    /// Changes only a block's hit points: damage that doesn't destroy it, or a repair. Nothing
    /// about its shape changes, so only its own chunk redraws (never a neighbour across a chunk
    /// edge) and <see cref="Version"/> stands still: stats, rooms and colliders stay valid.
    /// </summary>
    public void SetHp(Int3 p, float hp)
    {
        if (_fillers > 0) p = AnchorOf(p);
        Int3 c = ChunkOf(p);
        if (!_chunks.TryGetValue(c, out Chunk chunk)) return;
        ref Block cell = ref chunk.Cells[Chunk.Index(p.X, p.Y, p.Z)];
        if (cell.IsEmpty) return;
        cell.Hp = hp;
        DirtyChunks.Add(c);
    }

    /// <summary>Hits since the presentation last looked, for sparks and marks. Capped: nobody may be looking.</summary>
    public readonly List<BlockHit> RecentHits = new();

    internal void NoteHit(in BlockHit hit)
    {
        if (RecentHits.Count < MaxRecentHits) RecentHits.Add(hit);
    }

    const int MaxRecentHits = 256;

    /// <summary>Marks the cell's chunk dirty, and neighbouring chunks when it sits on a chunk edge (their culling changes).</summary>
    public void MarkDirty(Int3 p)
    {
        Version++;
        Int3 c = ChunkOf(p);
        DirtyChunks.Add(c);
        foreach (Dir d in Dirs.All)
        {
            Int3 n = ChunkOf(p + d.Vec());
            if (n != c) DirtyChunks.Add(n);
            // A block spanning several cells is drawn from its anchor's chunk: a change beside any
            // of its cells can hide or show its faces there.
            if (_fillers > 0 && TryGet(p + d.Vec(), out Block nb) && nb.IsFiller) DirtyChunks.Add(ChunkOf(p + d.Vec() + nb.ToAnchor));
        }
    }

    /// <summary>
    /// Every block, once, at its anchor: what counting, costing, saving and copying want. A
    /// block spanning several cells is listed at its anchor only (<see cref="Cells"/> lists them all).
    /// </summary>
    public IEnumerable<(Int3 pos, Block block)> Blocks()
    {
        foreach (var (p, b) in Cells())
            if (!b.IsFiller) yield return (p, b);
    }

    /// <summary>Every occupied cell, fillers included: what space, air and light want.</summary>
    public IEnumerable<(Int3 pos, Block block)> Cells()
    {
        foreach (var (c, chunk) in _chunks)
        {
            Int3 origin = ChunkOrigin(c);
            for (int i = 0; i < Chunk.Volume; i++)
            {
                if (chunk.Cells[i].IsEmpty) continue;
                yield return (origin + new Int3(i & Chunk.Mask, (i >> Chunk.Bits) & Chunk.Mask, i >> (2 * Chunk.Bits)), chunk.Cells[i]);
            }
        }
    }

    /// <summary>Smallest and largest occupied cell. Only valid while <see cref="Count"/> is above zero.</summary>
    public (Int3 min, Int3 max) Bounds()
    {
        if (_boundsVersion == Version) return (_min, _max);
        _min = new Int3(int.MaxValue, int.MaxValue, int.MaxValue);
        _max = new Int3(int.MinValue, int.MinValue, int.MinValue);
        foreach (var (p, _) in Cells())
        {
            _min = Int3.Min(_min, p);
            _max = Int3.Max(_max, p);
        }
        _boundsVersion = Version;
        return (_min, _max);
    }

    // ---- What a block's faces do -----------------------------------------------------------

    public BlockType TypeOf(in Block b) => Catalogue.Types[b.Type];
    public ShapeDef ShapeOf(in Block b) => Catalogue.Shapes[b.Shape];
    public BlockStats StatsOf(in Block b) => Catalogue.StatsFor(b);

    public float MaxHp(in Block b) => Catalogue.MaxHp(b);

    /// <summary>How much of a cell face the block touches, for attaching blocks to each other.</summary>
    public Coverage AttachCoverage(in Block b, Dir world)
    {
        int bit = 1 << (int)world;
        return (Catalogue.FullMask(b) & bit) != 0 ? Coverage.Full
            : (Catalogue.TouchMask(b) & bit) != 0 ? Coverage.Partial : Coverage.None;
    }

    /// <summary>True if the block's face fully and opaquely covers the cell face, so the neighbour's face behind it is hidden.</summary>
    public bool Occludes(in Block b, Dir world) => (Catalogue.OccludeMask(b) & (1 << (int)world)) != 0;

    /// <summary>True if air cannot pass this face of the block. Doors only seal while closed.</summary>
    public bool Seals(in Block b, Dir world) => (Catalogue.SealMask(b) & (1 << (int)world)) != 0;

    /// <summary>True if a character cannot walk through the block's cell.</summary>
    public bool IsSolid(in Block b)
    {
        BlockType t = TypeOf(b);
        if (t.IsArmor) return true;
        if (t.IsDoor) return !b.Open;
        return t.Collide != Collide.None;
    }

    /// <summary>Two neighbouring blocks are joined if both touch their shared face (structural connection).</summary>
    public bool Joined(Int3 a, Dir d)
    {
        if (!TryGet(a, out Block ba) || !TryGet(a + d.Vec(), out Block bb)) return false;
        // The cells of one block always hold together.
        if ((ba.IsFiller || bb.IsFiller) && ba.Type == bb.Type && ba.ToAnchor - bb.ToAnchor == d.Vec()) return true;
        return (Catalogue.TouchMask(ba) & (1 << (int)d)) != 0 && (Catalogue.TouchMask(bb) & (1 << (int)d.Opposite())) != 0;
    }
}
