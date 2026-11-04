#nullable enable
using System;
using System.Collections.Generic;

namespace PoiGenerator.Gen;

/// <summary>
/// The shape of a block: what the mesher draws and what collides. A stand-in for the canonical
/// block-and-component model, which isn't merged yet; only <see cref="IBlockStructure"/> and
/// <see cref="Block"/> leak out of the generator, so swapping models later is local.
/// </summary>
public enum BlockType : byte
{
    Air,
    Cube,       // full block; faces between cubes are culled
    Ramp,       // 45-degree wedge rising toward Facing; walkable
    Pillar,     // slim post
    Rib,        // thick post
    Pane,       // thin glass panel
    Lattice,    // open cube frame (Kessari)
    Spike,      // tapered spike pointing up
    Door,       // door panel in a socket; never blocks a path
    SealedDoor, // a welded door in a capped socket; something is behind it
    Terminal,
    Cache,      // loot container
    Core,       // the base's core: hijack it or destroy it
    Crate,
    Table,
    Meal,       // a table with an abandoned meal on it
    Bunk,
    Barricade,
    Remains,
    Scorch,     // flat burn mark
    Scatter,    // loose papers, cans, cables: never blocks
    Rubble,
    Ash,
    Growth,     // Hollow Bloom mass
    Tendril,    // hanging Bloom strand: never blocks
    Pod,
    Plant,
    Vine,
    Machine,
    Crystal,
}

/// <summary>What a block is made of. Drives how it renders and what salvage it gives.</summary>
public enum Material : byte
{
    Rock, Soil, Concrete, Steel, Alloy, Scrap, Timber, Chitin, Resin, Bone, Basalt, Bronze,
    Glass, Lumen, DeadLumen, Glyph, Crystal, Bloom, Flesh, Plant, Ash, Cloth, Ceramic,
}

/// <summary>The part a block plays in the place. State transforms read it.</summary>
public enum Role : byte
{
    None, Floor, Wall, Roof, Window, Light, Decor, Door, Loot, Core, Terminal, Prop, Story,
    Debris, Growth, Rock,
}

/// <summary>One grid cell. Eight bytes, so a dense grid stays small.</summary>
public struct Block
{
    public BlockType Type;
    public Material Material;
    public Role Role;
    public Dir Facing;
    public Rgb Color;
    public byte Variant; // door state (see DoorState), prop look

    public Block(BlockType type, Material material, Rgb color, Role role, Dir facing = Dir.North, byte variant = 0)
    {
        Type = type;
        Material = material;
        Color = color;
        Role = role;
        Facing = facing;
        Variant = variant;
    }

    public static readonly Block Air = default;
    public readonly bool IsAir => Type == BlockType.Air;
    public override readonly string ToString() => $"{Type}/{Material}/{Role}";
}

/// <summary>Stored in <see cref="Block.Variant"/> on doors.</summary>
public static class DoorState
{
    public const byte Closed = 0, Jammed = 1, Open = 2;
}

public static class BlockInfo
{
    /// <summary>Solid to bodies and to the pathfinder.</summary>
    public static bool Collides(BlockType t) => t switch
    {
        BlockType.Air or BlockType.Door or BlockType.Scorch or BlockType.Scatter or BlockType.Tendril
            or BlockType.Plant or BlockType.Vine or BlockType.Ash or BlockType.Remains => false,
        _ => true,
    };

    /// <summary>You can stand on top of it.</summary>
    public static bool Supports(BlockType t) => t is BlockType.Cube or BlockType.Lattice or BlockType.Crate;

    /// <summary>Fills its cell completely, so it hides its neighbours' faces.</summary>
    public static bool IsFullCube(BlockType t) => t == BlockType.Cube;

    public static bool IsTransparent(Material m) => m == Material.Glass;

    /// <summary>Glows on its own (unless the power is off).</summary>
    public static bool IsEmissive(Material m) => m is Material.Lumen or Material.Glyph or Material.Crystal;

    /// <summary>Structural blocks: the ones counted when a ruin takes the place apart.</summary>
    public static bool IsStructure(Role r) => r is Role.Floor or Role.Wall or Role.Roof or Role.Window or Role.Light or Role.Decor;
}

/// <summary>
/// The only view of a structure the rest of the game needs. The canonical blueprint model can
/// implement this and the assembler, transforms, validator and mesher keep working.
/// </summary>
public interface IBlockStructure
{
    Int3 Min { get; }
    Int3 Size { get; }
    bool InBounds(Int3 p);
    Block Get(Int3 p);
    void Set(Int3 p, Block b);
    int CountBlocks();
}

/// <summary>A dense block grid over a box. Out-of-bounds reads are air.</summary>
public sealed class BlockGrid : IBlockStructure
{
    public Int3 Min { get; }
    public Int3 Size { get; }
    public Int3 Max => Min + Size - new Int3(1, 1, 1);
    readonly Block[] _cells;

    public BlockGrid(Int3 min, Int3 size)
    {
        Min = min;
        Size = size;
        _cells = new Block[size.X * size.Y * size.Z];
    }

    public BlockGrid Clone()
    {
        var copy = new BlockGrid(Min, Size);
        Array.Copy(_cells, copy._cells, _cells.Length);
        return copy;
    }

    public bool InBounds(Int3 p)
    {
        Int3 l = p - Min;
        return (uint)l.X < (uint)Size.X && (uint)l.Y < (uint)Size.Y && (uint)l.Z < (uint)Size.Z;
    }

    int Index(Int3 p)
    {
        Int3 l = p - Min;
        return (l.Y * Size.Z + l.Z) * Size.X + l.X;
    }

    public Block Get(Int3 p) => InBounds(p) ? _cells[Index(p)] : Block.Air;

    public void Set(Int3 p, Block b)
    {
        if (InBounds(p)) _cells[Index(p)] = b;
    }

    public ref Block At(Int3 p) => ref _cells[Index(p)];

    public int CountBlocks()
    {
        int n = 0;
        foreach (var b in _cells) if (!b.IsAir) n++;
        return n;
    }

    /// <summary>Every non-air cell, in a fixed order (y, then z, then x).</summary>
    public IEnumerable<(Int3 Cell, Block Block)> Blocks()
    {
        for (int y = 0; y < Size.Y; y++)
            for (int z = 0; z < Size.Z; z++)
                for (int x = 0; x < Size.X; x++)
                {
                    var b = _cells[(y * Size.Z + z) * Size.X + x];
                    if (!b.IsAir) yield return (Min + new Int3(x, y, z), b);
                }
    }

    /// <summary>Every cell position, in the same fixed order.</summary>
    public IEnumerable<Int3> Cells()
    {
        for (int y = 0; y < Size.Y; y++)
            for (int z = 0; z < Size.Z; z++)
                for (int x = 0; x < Size.X; x++)
                    yield return Min + new Int3(x, y, z);
    }
}

/// <summary>A bit per cell over the same box as a grid. Used for occupancy and protection masks.</summary>
public sealed class CellMask
{
    public Int3 Min { get; }
    public Int3 Size { get; }
    readonly bool[] _bits;

    public CellMask(Int3 min, Int3 size)
    {
        Min = min;
        Size = size;
        _bits = new bool[size.X * size.Y * size.Z];
    }

    public CellMask Clone()
    {
        var c = new CellMask(Min, Size);
        Array.Copy(_bits, c._bits, _bits.Length);
        return c;
    }

    bool In(Int3 p, out int i)
    {
        Int3 l = p - Min;
        if ((uint)l.X < (uint)Size.X && (uint)l.Y < (uint)Size.Y && (uint)l.Z < (uint)Size.Z)
        {
            i = (l.Y * Size.Z + l.Z) * Size.X + l.X;
            return true;
        }
        i = -1;
        return false;
    }

    public bool this[Int3 p]
    {
        get => In(p, out int i) && _bits[i];
        set { if (In(p, out int i)) _bits[i] = value; }
    }
}
