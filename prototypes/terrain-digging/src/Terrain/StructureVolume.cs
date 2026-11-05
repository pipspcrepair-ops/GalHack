using System;
using System.Collections.Generic;
using System.Numerics;

namespace TerrainDigging.Terrain;

/// <summary>
/// Which structure, and which part of it, a <see cref="StructureVolume"/> belongs to: the
/// caller's own structure id and, say, the block's cell in that structure's grid. Ordered, so
/// every machine applies volumes in the same order.
/// <see cref="Copy"/> tells apart the copies of one part across a planet's seams (0 is the part
/// itself); removing a key removes all its copies.
/// <see cref="StoreOwner"/> is reserved for the store's own axis-aligned blocks.
/// </summary>
public readonly record struct VolumeKey(int Owner, int X, int Y, int Z, int Copy = 0) : IComparable<VolumeKey>
{
    /// <summary>Owner id the store uses for its own blocks (<see cref="ChunkStore.PlaceStructure"/>).</summary>
    public const int StoreOwner = int.MinValue;

    /// <summary>Most copies one part can have: itself and one across each seam and corner of a torus.</summary>
    public const int MaxCopies = 9;

    /// <summary>A whole structure as one part.</summary>
    public static VolumeKey Of(int owner) => new(owner, 0, 0, 0);

    /// <summary>The key of one of the store's own blocks.</summary>
    public static VolumeKey ForBlock(int structureId) => new(StoreOwner, structureId, 0, 0);

    /// <summary>The same part's copy number n (across a seam).</summary>
    public VolumeKey AsCopy(int n) => this with { Copy = n };

    public int CompareTo(VolumeKey o) =>
        Owner != o.Owner ? Owner.CompareTo(o.Owner) : X != o.X ? X.CompareTo(o.X) : Y != o.Y ? Y.CompareTo(o.Y)
        : Z != o.Z ? Z.CompareTo(o.Z) : Copy.CompareTo(o.Copy);
}

/// <summary>
/// A box a structure occupies in the world, at any orientation: one block of a base or vessel,
/// or a run of them. The terrain inside it is cleared (cells whose centres lie inside) and the
/// ground around it is drawn flush against its faces (see <see cref="ChunkStore.ClearForStructure"/>).
///
/// Plain numbers, built once: a centre, three unit axes and half the size along each. Axes within
/// a millionth of a world axis snap to it exactly, so blocks at 0° and 90° headings test exactly.
/// </summary>
public readonly struct StructureVolume : IEquatable<StructureVolume>
{
    public readonly Vector3 Center;
    public readonly Vector3 AxisX, AxisY, AxisZ;
    public readonly Vector3 Half;

    /// <summary>The world-space box around it.</summary>
    public readonly Vector3 Min, Max;

    StructureVolume(Vector3 center, Vector3 ax, Vector3 ay, Vector3 az, Vector3 half)
    {
        Center = center;
        AxisX = ax;
        AxisY = ay;
        AxisZ = az;
        Half = half;
        var extent = new Vector3(
            MathF.Abs(ax.X) * half.X + MathF.Abs(ay.X) * half.Y + MathF.Abs(az.X) * half.Z,
            MathF.Abs(ax.Y) * half.X + MathF.Abs(ay.Y) * half.Y + MathF.Abs(az.Y) * half.Z,
            MathF.Abs(ax.Z) * half.X + MathF.Abs(ay.Z) * half.Y + MathF.Abs(az.Z) * half.Z);
        Min = center - extent;
        Max = center + extent;
    }

    /// <summary>A box of this size, centred here and turned by this rotation (a block of a vessel or base).</summary>
    public StructureVolume(Vector3 center, Quaternion rotation, Vector3 size)
        : this(center, Snap(Vector3.Transform(Vector3.UnitX, rotation)), Snap(Vector3.Transform(Vector3.UnitY, rotation)),
            Snap(Vector3.Transform(Vector3.UnitZ, rotation)), size * 0.5f)
    {
    }

    /// <summary>An axis-aligned box between two corners.</summary>
    public static StructureVolume AxisAligned(Vector3 min, Vector3 max) =>
        new((min + max) * 0.5f, Vector3.UnitX, Vector3.UnitY, Vector3.UnitZ, (max - min) * 0.5f);

    /// <summary>Exactly a box of whole cells.</summary>
    public static StructureVolume Box(CellBox box) => AxisAligned(box.Min.ToVector(), box.Max.ToVector());

    static Vector3 Snap(Vector3 axis)
    {
        static float S(float v) => MathF.Abs(v) < 1e-6f ? 0f : MathF.Abs(v - 1f) < 1e-6f ? 1f : MathF.Abs(v + 1f) < 1e-6f ? -1f : v;
        var snapped = new Vector3(S(axis.X), S(axis.Y), S(axis.Z));
        // Only an axis that lands exactly on a world axis is snapped; anything else keeps its full precision.
        return MathF.Abs(snapped.LengthSquared() - 1f) < 1e-12f && (snapped.X == 0f ? 1 : 0) + (snapped.Y == 0f ? 1 : 0) + (snapped.Z == 0f ? 1 : 0) == 2
            ? snapped
            : Vector3.Normalize(axis);
    }

    /// <summary>A point in the box's own frame: offsets along its three axes from its centre.</summary>
    public Vector3 Local(Vector3 p)
    {
        Vector3 d = p - Center;
        return new Vector3(Vector3.Dot(d, AxisX), Vector3.Dot(d, AxisY), Vector3.Dot(d, AxisZ));
    }

    /// <summary>
    /// Whether a point is inside, half-open on each axis (−half ≤ x &lt; half), so a cell centre
    /// on the face two touching blocks share belongs to exactly one of them.
    /// </summary>
    public bool Contains(Vector3 p)
    {
        Vector3 l = Local(p);
        return -Half.X <= l.X && l.X < Half.X && -Half.Y <= l.Y && l.Y < Half.Y && -Half.Z <= l.Z && l.Z < Half.Z;
    }

    /// <summary>Whether a cell's centre lies inside: the cells a structure clears.</summary>
    public bool ContainsCell(Int3 cell) => Contains(cell.Center);

    /// <summary>The cells the box's world bounds touch.</summary>
    public CellBox CellBounds => new(Int3.Floor(Min), Int3.Floor(Max) + Int3.One);

    /// <summary>Whether its world bounds overlap a box (closed, grown by a margin).</summary>
    public bool BoundsOverlap(Vector3 lo, Vector3 hi, float margin = 0f) =>
        Min.X <= hi.X + margin && lo.X <= Max.X + margin && Min.Y <= hi.Y + margin && lo.Y <= Max.Y + margin &&
        Min.Z <= hi.Z + margin && lo.Z <= Max.Z + margin;

    /// <summary>The same box moved (a copy across a planet's seam).</summary>
    public StructureVolume Offset(Vector3 by) => new(Center + by, AxisX, AxisY, AxisZ, Half);

    /// <summary>Every cell whose centre lies inside, in canonical order.</summary>
    public List<Int3> CellsInside()
    {
        var cells = new List<Int3>();
        CellBox b = CellBounds;
        for (int x = b.Min.X; x < b.Max.X; x++)
            for (int y = b.Min.Y; y < b.Max.Y; y++)
                for (int z = b.Min.Z; z < b.Max.Z; z++)
                {
                    var cell = new Int3(x, y, z);
                    if (ContainsCell(cell)) cells.Add(cell);
                }
        return cells;
    }

    public bool Equals(StructureVolume o) => Center == o.Center && AxisX == o.AxisX && AxisY == o.AxisY && AxisZ == o.AxisZ && Half == o.Half;
    public override bool Equals(object obj) => obj is StructureVolume o && Equals(o);
    public override int GetHashCode() => HashCode.Combine(Center, AxisX, AxisY, AxisZ, Half);
    public override string ToString() => $"box at {Center} half {Half} axes {AxisX} {AxisY} {AxisZ}";
}
