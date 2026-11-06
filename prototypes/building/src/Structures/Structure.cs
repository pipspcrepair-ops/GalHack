using System;
using System.Collections.Generic;

namespace GalHack.Structures;

/// <summary>A large-grid core runs a base (it never flies) or a capital vessel. Everything else is shared.</summary>
public enum CoreMode { Base, Capital }

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
    public float Hp;
    public FacePaint Paint;

    public readonly bool IsEmpty => Type == 0;
    public readonly bool Open => (Flags & BlockFlags.Open) != 0;
    public readonly bool Off => (Flags & BlockFlags.Off) != 0;

    /// <summary>Same block apart from damage: used to merge runs in blueprints.</summary>
    public readonly bool SameAs(in Block o) =>
        Type == o.Type && Shape == o.Shape && Rot == o.Rot && Flags == o.Flags && Hp == o.Hp && Paint.Equals(o.Paint);
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
    public GridDef Grid { get; }
    public string Name = "Structure";

    /// <summary>Who owns it, set by placing the core. Null for an unowned wreck.</summary>
    public string Owner;

    public CoreMode Mode = CoreMode.Capital;

    /// <summary>The core's artificial gravity switch. It also needs power to work (see <see cref="StructureStats"/>).</summary>
    public bool GravityOn = true;

    /// <summary>Cargo in the shared storage, in storage units.</summary>
    public double CargoSu;

    public PaintPalette Palette { get; } = new();

    /// <summary>Chunks whose blocks (or whose neighbours' blocks) changed since the presentation last looked.</summary>
    public readonly HashSet<Int3> DirtyChunks = new();

    /// <summary>Bumped on every edit, so derived data (stats, rooms) knows when to recompute.</summary>
    public int Version { get; private set; }

    public int Count { get; private set; }
    public Int3? Core { get; private set; }
    public bool IsWreck => Core == null;

    readonly Dictionary<Int3, Chunk> _chunks = new();
    int _boundsVersion = -1;
    Int3 _min, _max;

    public Structure(Catalogue catalogue, GridDef grid)
    {
        Catalogue = catalogue;
        Grid = grid;
    }

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

    /// <summary>
    /// Writes a cell with no rules at all (placement rules live in <see cref="Building"/>). Pass
    /// an empty block to clear it.
    /// </summary>
    public void Set(Int3 p, Block block)
    {
        Int3 c = ChunkOf(p);
        if (!_chunks.TryGetValue(c, out Chunk chunk))
        {
            if (block.IsEmpty) return;
            chunk = new Chunk();
            _chunks[c] = chunk;
        }

        ref Block cell = ref chunk.Cells[Chunk.Index(p.X, p.Y, p.Z)];
        bool was = !cell.IsEmpty, now = !block.IsEmpty;
        if (was && Core == p) Core = null;
        cell = block;
        if (now && Catalogue.Types[block.Type].IsCore) Core = p;

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

    /// <summary>
    /// Changes only a block's hit points: damage that doesn't destroy it, or a repair. Nothing
    /// about its shape changes, so only its own chunk redraws (never a neighbour across a chunk
    /// edge) and <see cref="Version"/> stands still: stats, rooms and colliders stay valid.
    /// </summary>
    public void SetHp(Int3 p, float hp)
    {
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
        }
    }

    public IEnumerable<(Int3 pos, Block block)> Blocks()
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
        foreach (var (p, _) in Blocks())
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
    public BlockStats StatsOf(in Block b) => Catalogue.StatsFor(b, Grid);

    public float MaxHp(in Block b) => Catalogue.MaxHp(b, Grid);

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
        return (Catalogue.TouchMask(ba) & (1 << (int)d)) != 0 && (Catalogue.TouchMask(bb) & (1 << (int)d.Opposite())) != 0;
    }
}
