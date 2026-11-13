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

/// <summary>What plating (or upgrading) a block did. WrongBase: the plate goes on another base block (glass only on windows).</summary>
public enum UpgradeStatus { Upgraded, NotArmor, AlreadyBest, NotEnoughMaterials, Empty, WrongBase }

public enum RepairStatus { Repaired, NotDamaged, NotEnoughMaterials }

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

    /// <summary>
    /// Whether a block can go with its anchor at <paramref name="p"/>. A block spanning several
    /// cells needs every one of them free, and attaches through any of its outer faces.
    /// </summary>
    public static PlaceError Check(Structure s, Int3 p, in Block b)
    {
        if (Blocked(s, p, b).Count > 0) return PlaceError.Occupied;
        BlockType t = s.TypeOf(b);
        if (s.Count == 0) return t.IsCore ? PlaceError.None : PlaceError.StartWithCore;
        if (t.IsCore && s.Core != null) return PlaceError.OneCoreOnly;

        bool touching = false;
        foreach (Int3 cell in s.CellsOf(p, b))
        {
            Block mine = cell == p ? b : Footprint.Filler(b, p, cell);
            byte outer = Footprint.OuterMask(t, mine);
            foreach (Dir d in Dirs.All)
            {
                if ((outer & (1 << (int)d)) == 0 || !s.TryGet(cell + d.Vec(), out Block n)) continue;
                touching = true;
                if (s.AttachCoverage(mine, d) != Coverage.None && s.AttachCoverage(n, d.Opposite()) != Coverage.None)
                    return PlaceError.None;
            }
        }
        return touching ? PlaceError.NoSharedFace : PlaceError.NotAttached;
    }

    /// <summary>The cells a block anchored at <paramref name="p"/> would need that are already taken (the ghost shows them red).</summary>
    public static List<Int3> Blocked(Structure s, Int3 p, in Block b)
    {
        var blocked = new List<Int3>();
        foreach (Int3 cell in s.CellsOf(p, b))
            if (s.Has(cell)) blocked.Add(cell);
        return blocked;
    }

    /// <summary>
    /// Places one block. Placing a core on an empty structure starts it; placing one on a wreck
    /// claims the wreck for <paramref name="owner"/>.
    /// </summary>
    public static PlaceError Place(Structure s, Int3 p, Block b, Inventory inventory = null, string owner = DefaultOwner)
    {
        PlaceError error = Check(s, p, b);
        if (error != PlaceError.None) return error;
        if (inventory != null && !inventory.TryTake(s.Catalogue.CostOf(b))) return PlaceError.NotEnoughMaterials;
        b.Hp = s.StatsOf(b).MaxHp;
        s.Set(p, b);
        if (s.TypeOf(b).IsCore) s.Owner = owner;
        return PlaceError.None;
    }

    /// <summary>The block and its mirror images (up to 8). Copies landing on a plane collapse onto the original.</summary>
    public static List<Placement> MirrorCopies(Structure s, Int3 p, Block b, Symmetry sym)
    {
        var copies = new List<Placement> { new(p, b, 0) };
        BlockType t = s.TypeOf(b);
        for (int axis = 0; axis < 3; axis++)
        {
            if (!sym.On(axis)) continue;
            int count = copies.Count;
            for (int i = 0; i < count; i++)
            {
                Block mirrored = MirrorBlock(s, copies[i].Block, axis);
                Int3 m = Footprint.MirrorAnchor(copies[i].Pos, t, copies[i].Block.Rot, mirrored.Rot, c => sym.Mirror(c, axis));
                if (copies.Any(c => Overlap(s, c.Pos, c.Block, m, mirrored))) continue;
                copies.Add(new Placement(m, mirrored, copies[i].Mirrors | (1 << axis)));
            }
        }
        return copies;
    }

    static bool Overlap(Structure s, Int3 a, Block ba, Int3 b, Block bb) => s.CellsOf(a, ba).Intersect(s.CellsOf(b, bb)).Any();

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
    /// The multi-tool's take-down: the block's materials go to the inventory. The core is never
    /// taken down, not even as the last block: it can only be destroyed or hijacked (design 693).
    /// Anything the removal cuts off becomes a wreck (returned), never lost.
    /// </summary>
    public static RemoveResult Remove(Structure s, Int3 p, Inventory inventory)
    {
        // Any cell of a block spanning several takes the whole block down, for one block's worth.
        if (!s.TryGetBlock(p, out p, out Block b)) return new RemoveResult(RemoveStatus.Empty, default, new List<Structure>());
        if (s.TypeOf(b).IsCore) return new RemoveResult(RemoveStatus.CoreHoldsStructure, b, new List<Structure>());
        inventory?.Add(s.Catalogue.CostOf(b));
        s.Clear(p);
        return new RemoveResult(RemoveStatus.Removed, b, Connectivity.SplitOff(s));
    }

    /// <summary>The next plating up, in place: base → iron → hardened → combat, window → glass (see <see cref="Plate"/>).</summary>
    public static UpgradeStatus Upgrade(Structure s, Int3 p, Inventory inventory = null)
    {
        if (!s.TryGetBlock(p, out _, out Block b)) return UpgradeStatus.Empty;
        BlockType t = s.TypeOf(b);
        if (!t.IsArmor) return UpgradeStatus.NotArmor;
        return t.Upgrade == null ? UpgradeStatus.AlreadyBest : Plate(s, p, t.Upgrade, inventory);
    }

    // ---- Plating ----------------------------------------------------------------------------
    // A structural block's material is its plating: a plated block is its own type, its base block
    // plus that material's plates. Spraying plates on (or a different plating over the old) and
    // stripping them give back the old plating's worth by the block's hit-point share, rounded
    // down, so a damaged block returns less. Shape, orientation and paint are untouched.

    /// <summary>Sprays <paramref name="plated"/>'s plates onto an armour block: a bare base takes them, a plated one swaps its plating. The block comes out whole.</summary>
    public static UpgradeStatus Plate(Structure s, Int3 p, BlockType plated, Inventory inventory = null)
    {
        if (!s.TryGetBlock(p, out p, out Block b)) return UpgradeStatus.Empty;
        BlockType t = s.TypeOf(b);
        if (!t.IsArmor || !plated.IsPlated) return UpgradeStatus.NotArmor;
        if (t == plated) return UpgradeStatus.AlreadyBest;
        if ((t.Base ?? t) != plated.Base) return UpgradeStatus.WrongBase;
        Block next = b;
        next.Type = (ushort)plated.Index;
        if (inventory != null && !inventory.TryTake(new Dictionary<string, int> { [plated.Plate] = s.Catalogue.PlatesOf(next) }))
            return UpgradeStatus.NotEnoughMaterials;
        inventory?.Add(PlatingWorth(s, b));
        next.Hp = s.StatsOf(next).MaxHp;
        s.Set(p, next);
        return UpgradeStatus.Upgraded;
    }

    /// <summary>Strips a block's plating back to its base block, which keeps the block's hit-point share.</summary>
    public static UpgradeStatus Strip(Structure s, Int3 p, Inventory inventory = null)
    {
        if (!s.TryGetBlock(p, out p, out Block b)) return UpgradeStatus.Empty;
        BlockType t = s.TypeOf(b);
        if (!t.IsArmor) return UpgradeStatus.NotArmor;
        if (!t.IsPlated) return UpgradeStatus.AlreadyBest;
        inventory?.Add(PlatingWorth(s, b));
        float share = b.Hp / s.StatsOf(b).MaxHp;
        Block bare = b;
        bare.Type = (ushort)t.Base.Index;
        bare.Hp = s.StatsOf(bare).MaxHp * share;
        s.Set(p, bare);
        return UpgradeStatus.Upgraded;
    }

    /// <summary>What a block's plating is worth back: its plates by the block's hit-point share, rounded down.</summary>
    public static Dictionary<string, int> PlatingWorth(Structure s, in Block b)
    {
        BlockType t = s.TypeOf(b);
        int n = t.IsPlated ? (int)MathF.Floor(s.Catalogue.PlatesOf(b) * b.Hp / s.StatsOf(b).MaxHp + 1e-4f) : 0;
        return n > 0 ? new Dictionary<string, int> { [t.Plate] = n } : new Dictionary<string, int>();
    }

    /// <summary>Brings a damaged block back to full health. Returns false if there was nothing to fix.</summary>
    public static bool Repair(Structure s, Int3 p) => Repair(s, p, 1f);

    /// <summary>
    /// Gives a damaged block back a share of its full health (the look steps back with it, one
    /// damage stage at a time). Returns false if there was nothing to fix.
    /// </summary>
    public static bool Repair(Structure s, Int3 p, float share)
    {
        if (!s.TryGetBlock(p, out p, out Block b)) return false;
        float max = s.StatsOf(b).MaxHp;
        if (b.Hp >= max - 0.01f) return false;
        s.SetHp(p, MathF.Min(max, b.Hp + max * share));
        return true;
    }

    /// <summary>The share of its bill a repair of this much of a block's health costs: repairs consume materials.</summary>
    public static float RepairShare(Structure s, Int3 p, float share = 1f)
    {
        if (!s.TryGetBlock(p, out _, out Block b)) return 0f;
        float max = s.StatsOf(b).MaxHp;
        return MathF.Min(share, MathF.Max(0f, (max - b.Hp) / max));
    }

    /// <summary>
    /// What repairing this much of the block costs: that share of its recipe, rounded up, so no
    /// repair is free. A plated block is topped up with its plates alone.
    /// </summary>
    public static Dictionary<string, int> RepairCost(Structure s, Int3 p, float share = 1f)
    {
        float paid = RepairShare(s, p, share);
        if (paid <= 0f) return new Dictionary<string, int>();
        s.TryGetBlock(p, out _, out Block b);
        BlockType t = s.TypeOf(b);
        Dictionary<string, int> bill = t.IsPlated ? new() { [t.Plate] = s.Catalogue.PlatesOf(b) } : s.Catalogue.CostOf(b);
        return bill.ToDictionary(kv => kv.Key, kv => (int)MathF.Ceiling(kv.Value * paid));
    }

    /// <summary>A repair paid for from an inventory (a structure's shared storage, the player's bag). Nothing is restored unless it can pay.</summary>
    public static RepairStatus Repair(Structure s, Int3 p, Inventory inventory, float share = 1f)
    {
        if (!IsDamaged(s, p)) return RepairStatus.NotDamaged;
        if (inventory != null && !inventory.TryTake(RepairCost(s, p, share))) return RepairStatus.NotEnoughMaterials;
        Repair(s, p, share);
        return RepairStatus.Repaired;
    }

    public static bool IsDamaged(Structure s, Int3 p) =>
        s.TryGetBlock(p, out _, out Block b) && b.Hp < s.StatsOf(b).MaxHp - 0.01f;

    /// <summary>Paints one face (a block spanning several cells takes it on that whole side). <paramref name="paint"/> null strips it back to bare material.</summary>
    public static bool PaintFace(Structure s, Int3 p, Dir face, Paint? paint)
    {
        if (!s.TryGetBlock(p, out p, out Block b)) return false;
        ushort slot = paint is Paint value ? s.Palette.Intern(value) : (ushort)0;
        if (b.Paint[face] == slot) return false;
        b.Paint[face] = slot;
        s.Set(p, b);
        return true;
    }

    public static Paint? PaintOf(Structure s, Int3 p, Dir face)
    {
        if (!s.TryGetBlock(p, out _, out Block b) || b.Paint[face] == 0) return null;
        return s.Palette[b.Paint[face]];
    }

    /// <summary>Sets the core's mode. One value changes; no block does. A wreck has no core to set.</summary>
    public static bool SetCoreMode(Structure s, CoreMode mode)
    {
        if (s.Core is not Int3 core) return false;
        s.Mode = mode;
        s.MarkDirty(core);
        return true;
    }

    /// <summary>Steps a core to its next mode: Base, Hover, Flight, and round again. Every structure has all three.</summary>
    public static bool ToggleCoreMode(Structure s) => SetCoreMode(s, (CoreMode)(((int)s.Mode + 1) % 3));

    /// <summary>Opens or closes a door, switches a light, or flips the core's gravity. Returns false if the block does nothing.</summary>
    public static bool Toggle(Structure s, Int3 p)
    {
        if (!s.TryGetBlock(p, out p, out Block b)) return false;
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
