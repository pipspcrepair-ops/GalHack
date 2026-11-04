using System;
using System.Collections.Generic;
using System.Linq;

namespace GalHack.Structures;

public enum PlaceError
{
    None,
    Occupied,
    /// <summary>No neighbouring block at all.</summary>
    NotAttached,
    /// <summary>Neighbours exist, but none shares a face with area on both sides (a slope's tip, a half block's open top).</summary>
    NoSharedFace,
    /// <summary>An empty structure only takes a core: every structure starts from its starter block.</summary>
    StartWithCore,
    OneCoreOnly,
    NotEnoughMaterials,
}

public enum RemoveStatus { Removed, Empty, CoreHoldsStructure }

public enum UpgradeStatus { Upgraded, NotArmor, AlreadyBest, NotEnoughMaterials, Empty }

public sealed record RemoveResult(RemoveStatus Status, Block Block, List<Structure> Wrecks);

/// <summary>
/// Mirror planes on up to three axes. A plane is stored doubled so it can sit on a cell centre
/// (even value: the cells on it mirror onto themselves) or between two cells (odd value), which
/// is how both odd- and even-width hulls stay symmetric.
/// </summary>
public struct Symmetry
{
    public bool X, Y, Z;
    public int X2, Y2, Z2;

    public readonly bool Any => X || Y || Z;
    public readonly bool On(int axis) => axis switch { 0 => X, 1 => Y, _ => Z };
    public readonly int Plane2(int axis) => axis switch { 0 => X2, 1 => Y2, _ => Z2 };
    public readonly Int3 Mirror(Int3 p, int axis) => p.With(axis, Plane2(axis) - p[axis]);

    public void Set(int axis, bool on, int plane2)
    {
        if (axis == 0) { X = on; X2 = plane2; }
        else if (axis == 1) { Y = on; Y2 = plane2; }
        else { Z = on; Z2 = plane2; }
    }

    /// <summary>All three planes through a cell's centre (usually the core), all switched off.</summary>
    public static Symmetry Through(Int3 cell) => new() { X2 = cell.X * 2, Y2 = cell.Y * 2, Z2 = cell.Z * 2 };
}

/// <summary>A block and where it goes, plus which mirror planes produced it (bit per axis).</summary>
public readonly record struct Placement(Int3 Pos, Block Block, int Mirrors);

/// <summary>Materials carried by the player. In creative mode placing is free but removing still pays out.</summary>
public sealed class Inventory
{
    public bool Creative = true;
    public readonly SortedDictionary<string, int> Items = new(StringComparer.Ordinal);

    public int this[string item] => Items.TryGetValue(item, out int n) ? n : 0;

    public void Add(Dictionary<string, int> items)
    {
        foreach (var (item, n) in items) Items[item] = this[item] + n;
    }

    public bool TryTake(Dictionary<string, int> items)
    {
        if (Creative) return true;
        if (items.Any(kv => this[kv.Key] < kv.Value)) return false;
        foreach (var (item, n) in items) Items[item] = this[item] - n;
        return true;
    }

    public int Total => Items.Values.Sum();
}

/// <summary>
/// The building rules: placing (no overlaps, attach through a shared face, one core), mirror
/// symmetry, removing with the multi-tool, upgrading armour in place, repairing and painting.
/// Every edit that can cut a section off runs the split, so blocks are never lost.
/// </summary>
public static class Building
{
    public const string DefaultOwner = "player";

    /// <summary>A new block at full health with no paint.</summary>
    public static Block Make(Structure s, BlockType type, ShapeDef shape = null, int rot = Orientations.Identity)
    {
        var b = new Block
        {
            Type = (ushort)type.Index,
            Shape = (byte)(type.IsArmor ? (shape ?? s.Catalogue.Cube).Index : 0),
            Rot = (byte)rot,
        };
        b.Hp = s.StatsOf(b).MaxHp;
        return b;
    }

    public static PlaceError Check(Structure s, Int3 p, in Block b)
    {
        if (s.Has(p)) return PlaceError.Occupied;
        BlockType t = s.TypeOf(b);
        if (s.Count == 0) return t.IsCore ? PlaceError.None : PlaceError.StartWithCore;
        if (t.IsCore && s.Core != null) return PlaceError.OneCoreOnly;

        bool touching = false;
        foreach (Dir d in Dirs.All)
        {
            if (!s.TryGet(p + d.Vec(), out Block n)) continue;
            touching = true;
            if (s.AttachCoverage(b, d) != Coverage.None && s.AttachCoverage(n, d.Opposite()) != Coverage.None)
                return PlaceError.None;
        }
        return touching ? PlaceError.NoSharedFace : PlaceError.NotAttached;
    }

    /// <summary>
    /// Places one block. Placing a core on an empty structure starts it; placing one on a wreck
    /// claims the wreck for <paramref name="owner"/>.
    /// </summary>
    public static PlaceError Place(Structure s, Int3 p, Block b, Inventory inventory = null, string owner = DefaultOwner)
    {
        PlaceError error = Check(s, p, b);
        if (error != PlaceError.None) return error;
        if (inventory != null && !inventory.TryTake(s.Catalogue.CostOf(b, s.Grid))) return PlaceError.NotEnoughMaterials;
        b.Hp = s.StatsOf(b).MaxHp;
        s.Set(p, b);
        if (s.TypeOf(b).IsCore) s.Owner = owner;
        return PlaceError.None;
    }

    /// <summary>The block and its mirror images (up to 8). Copies landing on a plane collapse onto the original.</summary>
    public static List<Placement> MirrorCopies(Structure s, Int3 p, Block b, Symmetry sym)
    {
        var copies = new List<Placement> { new(p, b, 0) };
        for (int axis = 0; axis < 3; axis++)
        {
            if (!sym.On(axis)) continue;
            int count = copies.Count;
            for (int i = 0; i < count; i++)
            {
                Int3 m = sym.Mirror(copies[i].Pos, axis);
                if (copies.Any(c => c.Pos == m)) continue;
                copies.Add(new Placement(m, MirrorBlock(s, copies[i].Block, axis), copies[i].Mirrors | (1 << axis)));
            }
        }
        return copies;
    }

    /// <summary>A block's mirror image: orientation through the shape's own symmetry, and its face paint swapped across the plane.</summary>
    public static Block MirrorBlock(Structure s, Block b, int axis)
    {
        BlockType t = s.TypeOf(b);
        Mat3 symmetry = t.IsArmor ? s.ShapeOf(b).MirrorSymmetry : Mat3.Mirror(0); // components are left-right symmetric
        b.Rot = (byte)Orientations.Mirror(b.Rot, axis, symmetry);
        FacePaint mirrored = default;
        foreach (Dir d in Dirs.All) mirrored[d.Mirror(axis)] = b.Paint[d];
        b.Paint = mirrored;
        return b;
    }

    /// <summary>
    /// Places a block and its mirror images. The original must succeed; each copy is placed if
    /// it can be, retrying so a copy may attach to another copy placed in the same action.
    /// </summary>
    public static List<(Placement copy, PlaceError error)> PlaceSymmetric(
        Structure s, Int3 p, Block b, Symmetry sym, Inventory inventory = null, string owner = DefaultOwner)
    {
        List<Placement> copies = MirrorCopies(s, p, b, sym);
        var results = new List<(Placement, PlaceError)>();
        PlaceError first = Place(s, copies[0].Pos, copies[0].Block, inventory, owner);
        results.Add((copies[0], first));
        if (first != PlaceError.None) return results;

        var pending = copies.Skip(1).ToList();
        var errors = new Dictionary<Int3, PlaceError>();
        bool progress = true;
        while (pending.Count > 0 && progress)
        {
            progress = false;
            for (int i = pending.Count - 1; i >= 0; i--)
            {
                PlaceError e = Place(s, pending[i].Pos, pending[i].Block, inventory, owner);
                if (e == PlaceError.None)
                {
                    results.Add((pending[i], e));
                    pending.RemoveAt(i);
                    progress = true;
                }
                else errors[pending[i].Pos] = e;
            }
        }
        foreach (Placement c in pending) results.Add((c, errors[c.Pos]));
        return results;
    }

    /// <summary>True if removing this block would cut blocks off from the core (how many in <paramref name="cutOff"/>).</summary>
    public static bool WouldSever(Structure s, Int3 p, out int cutOff)
    {
        cutOff = Connectivity.CutOffWithout(s, p);
        return cutOff > 0;
    }

    /// <summary>
    /// The multi-tool's take-down: the block's materials go to the inventory. The core can only
    /// be picked up when it is the last block, since it holds the structure together. Anything
    /// the removal cuts off becomes a wreck (returned), never lost.
    /// </summary>
    public static RemoveResult Remove(Structure s, Int3 p, Inventory inventory)
    {
        if (!s.TryGet(p, out Block b)) return new RemoveResult(RemoveStatus.Empty, default, new List<Structure>());
        if (s.TypeOf(b).IsCore && s.Count > 1) return new RemoveResult(RemoveStatus.CoreHoldsStructure, b, new List<Structure>());
        inventory?.Add(s.Catalogue.CostOf(b, s.Grid));
        s.Clear(p);
        return new RemoveResult(RemoveStatus.Removed, b, Connectivity.SplitOff(s));
    }

    /// <summary>Steel → hardened → combat, in place. Shape, orientation and paint are untouched.</summary>
    public static UpgradeStatus Upgrade(Structure s, Int3 p, Inventory inventory = null)
    {
        if (!s.TryGet(p, out Block b)) return UpgradeStatus.Empty;
        BlockType t = s.TypeOf(b);
        if (!t.IsArmor) return UpgradeStatus.NotArmor;
        if (t.Upgrade == null) return UpgradeStatus.AlreadyBest;

        Block upgraded = b;
        upgraded.Type = (ushort)t.Upgrade.Index;
        var before = s.Catalogue.CostOf(b, s.Grid);
        var extra = s.Catalogue.CostOf(upgraded, s.Grid)
            .Select(kv => (kv.Key, n: kv.Value - (before.TryGetValue(kv.Key, out int had) ? had : 0)))
            .Where(x => x.n > 0)
            .ToDictionary(x => x.Key, x => x.n);
        if (inventory != null && !inventory.TryTake(extra)) return UpgradeStatus.NotEnoughMaterials;

        upgraded.Hp = s.StatsOf(upgraded).MaxHp;
        s.Set(p, upgraded);
        return UpgradeStatus.Upgraded;
    }

    /// <summary>Brings a damaged block back to full health. Returns false if there was nothing to fix.</summary>
    public static bool Repair(Structure s, Int3 p)
    {
        if (!s.TryGet(p, out Block b)) return false;
        float max = s.StatsOf(b).MaxHp;
        if (b.Hp >= max - 0.01f) return false;
        b.Hp = max;
        s.Set(p, b);
        return true;
    }

    public static bool IsDamaged(Structure s, Int3 p) =>
        s.TryGet(p, out Block b) && b.Hp < s.StatsOf(b).MaxHp - 0.01f;

    /// <summary>Paints one face. <paramref name="paint"/> null strips it back to bare material.</summary>
    public static bool PaintFace(Structure s, Int3 p, Dir face, Paint? paint)
    {
        if (!s.TryGet(p, out Block b)) return false;
        ushort slot = paint is Paint value ? s.Palette.Intern(value) : (ushort)0;
        if (b.Paint[face] == slot) return false;
        b.Paint[face] = slot;
        s.Set(p, b);
        return true;
    }

    public static Paint? PaintOf(Structure s, Int3 p, Dir face)
    {
        if (!s.TryGet(p, out Block b) || b.Paint[face] == 0) return null;
        return s.Palette[b.Paint[face]];
    }

    /// <summary>
    /// Switches a large-grid core between base and capital. Small-grid cores are always vessels,
    /// and a wreck has no core to switch.
    /// </summary>
    public static bool ToggleCoreMode(Structure s)
    {
        if (s.Core is not Int3 core || s.Grid.Size == GridSize.Small) return false;
        s.Mode = s.Mode == CoreMode.Base ? CoreMode.Capital : CoreMode.Base;
        s.MarkDirty(core);
        return true;
    }

    /// <summary>Opens or closes a door, switches a light, or flips the core's gravity. Returns false if the block does nothing.</summary>
    public static bool Toggle(Structure s, Int3 p)
    {
        if (!s.TryGet(p, out Block b)) return false;
        BlockType t = s.TypeOf(b);
        if (t.IsDoor) b.Flags ^= BlockFlags.Open;
        else if (t.IsLight) b.Flags ^= BlockFlags.Off;
        else if (t.IsCore)
        {
            s.GravityOn = !s.GravityOn;
            s.MarkDirty(p);
            return true;
        }
        else return false;
        s.Set(p, b);
        return true;
    }
}
