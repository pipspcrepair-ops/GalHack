using System;
using System.Collections.Generic;
using System.Globalization;
using System.Numerics;

namespace GalHack.Structures;

/// <summary>
/// Blocks that span several cells. A component may have a <see cref="BlockType.Size"/> in cells,
/// local to the block. Its anchor cell (local cell 0, 0, 0) holds the block: type, turn, flags,
/// hit points and paint. Every other cell of its size holds a filler: the same type, turn and
/// flags, plus the offset back to the anchor (<see cref="Block.ToAnchor"/>). A filler is never a
/// block of its own: it draws nothing, adds no collider, has no hit points and is never counted.
///
/// The size turns with the block: local cell (x, y, z) sits at anchor + R·(x, y, z). The parts in
/// the data stay in the −0.5..0.5 box, which stands for the whole size, so a part is scaled by the
/// size and moved to the size's centre before it is turned (<see cref="PartPoint"/>).
/// </summary>
public static class Footprint
{
    /// <summary>The largest a block may be along any axis: a filler's offset is a signed byte.</summary>
    public const int MaxSize = 128;

    public static readonly Int3 One = new(1, 1, 1);

    /// <summary>The block's cells in structure coordinates, anchor first.</summary>
    public static IEnumerable<Int3> Cells(Int3 anchor, BlockType t, int rot)
    {
        Mat3 m = Orientations.Matrix(rot);
        for (int z = 0; z < t.Size.Z; z++)
            for (int y = 0; y < t.Size.Y; y++)
                for (int x = 0; x < t.Size.X; x++)
                    yield return anchor + m.Apply(new Int3(x, y, z));
    }

    /// <summary>The smallest and largest offset from the anchor that the turned size covers (inclusive).</summary>
    public static (Int3 min, Int3 max) Box(BlockType t, int rot)
    {
        Int3 far = Orientations.Matrix(rot).Apply(t.Size - One);
        return (Int3.Min(Int3.Zero, far), Int3.Max(Int3.Zero, far));
    }

    /// <summary>A block's size in metres, for labels and tooltips: "2 × 2 × 1 m".</summary>
    public static string Label(BlockType t, float cell) =>
        string.Create(CultureInfo.InvariantCulture, $"{t.Size.X * cell:0.#} × {t.Size.Y * cell:0.#} × {t.Size.Z * cell:0.#} m");

    /// <summary>The turned size along structure axes, in cells.</summary>
    public static Int3 WorldSize(BlockType t, int rot)
    {
        var (min, max) = Box(t, rot);
        return max - min + One;
    }

    /// <summary>The centre of the block in cells, relative to its anchor's centre.</summary>
    public static Vector3 Centre(BlockType t, int rot) =>
        Orientations.Matrix(rot).Apply((t.Size - One).ToVector3() * 0.5f);

    /// <summary>
    /// A point of a part (in the −0.5..0.5 box) in the block's local frame measured from the
    /// anchor's centre, before the block is turned. A one-cell block leaves it as it is.
    /// </summary>
    public static Vector3 PartPoint(BlockType t, Vector3 p)
    {
        Vector3 size = t.Size.ToVector3();
        return p * size + (size - Vector3.One) * 0.5f;
    }

    /// <summary>
    /// The structure faces of this cell that are outer faces of its block (the next cell over is
    /// not the same block). One bit per <see cref="Dir"/>; every face of a one-cell block.
    /// </summary>
    public static byte OuterMask(BlockType t, in Block b)
    {
        if (!t.Multi) return 0x3f;
        Int3 local = Orientations.Matrix(b.Rot).Transposed().Apply(-b.ToAnchor), size = t.Size;
        byte mask = 0;
        foreach (Dir world in Dirs.All)
        {
            Int3 n = local + Orientations.ToLocal(b.Rot, world).Vec();
            if (n.X < 0 || n.Y < 0 || n.Z < 0 || n.X >= size.X || n.Y >= size.Y || n.Z >= size.Z) mask |= (byte)(1 << (int)world);
        }
        return mask;
    }

    /// <summary>The filler a cell of the block holds: the anchor's type, turn and flags, pointing back at the anchor.</summary>
    public static Block Filler(in Block anchorBlock, Int3 anchor, Int3 cell)
    {
        Int3 d = anchor - cell;
        return new Block
        {
            Type = anchorBlock.Type,
            Rot = anchorBlock.Rot,
            Flags = anchorBlock.Flags,
            Ox = (sbyte)d.X,
            Oy = (sbyte)d.Y,
            Oz = (sbyte)d.Z,
        };
    }

    /// <summary>
    /// Where the anchor goes so that the block's turned size holds <paramref name="target"/> and
    /// grows away from the face it is built against: out along <paramref name="away"/> (the
    /// direction from the block clicked to the new cell), and towards + on the other two axes.
    /// </summary>
    public static Int3 AnchorFor(BlockType t, int rot, Int3 target, Dir? away = null)
    {
        var (min, max) = Box(t, rot);
        Int3 anchor = target - min;
        if (away is Dir d && d.Sign() < 0) anchor = anchor.With(d.Axis(), target[d.Axis()] - max[d.Axis()]);
        return anchor;
    }

    /// <summary>The anchor of a block's mirror image across a plane: the mirrored cells, filled by the mirrored turn.</summary>
    public static Int3 MirrorAnchor(Int3 anchor, BlockType t, int rot, int mirroredRot, Func<Int3, Int3> mirror)
    {
        var low = new Int3(int.MaxValue, int.MaxValue, int.MaxValue);
        foreach (Int3 c in Cells(anchor, t, rot)) low = Int3.Min(low, mirror(c));
        return low - Box(t, mirroredRot).min;
    }
}
