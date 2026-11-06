using GalHack.Structures;

namespace GalHack.Items.Bridge;

public enum PlaceStatus
{
    Placed,
    /// <summary>Building's own rules said no (occupied, not attached...): see <see cref="PlaceResult.Rule"/>.</summary>
    Blocked,
    /// <summary>Survival: the source holds none of the block's item (see <see cref="PlaceResult.Missing"/>).</summary>
    NotEnoughMaterials,
    /// <summary>Survival: the block has no item, so it can't be placed.</summary>
    NoBill,
    /// <summary>The block's family doesn't go on this class of structure (small-grid blocks on a base, say).</summary>
    WrongClass,
}

public readonly record struct PlaceResult(PlaceStatus Status, PlaceError Rule, Bill Paid, Bill Missing)
{
    public bool Ok => Status == PlaceStatus.Placed;
}

public readonly record struct TakeDownResult(RemoveResult Removal, Bill Refunded, Bill Spilled, SpillCrate Crate)
{
    public bool Ok => Removal.Status == RemoveStatus.Removed;
}

/// <summary>
/// Placing and taking down blocks in either game mode, through building's own rules. Blocks are
/// craftable items (Empyrion's way): a block family item places as any of its shapes.
///
/// Survival: placing a block uses one of its family item from the source the caller names (a
/// player's stack, or a structure's one shared storage), and taking it down gives that one item
/// back, nothing lost; whatever the storage can't take goes into a spill crate that never
/// despawns. A container taken away shrinks storage (<see cref="Sync"/>), and what no longer fits
/// spills the same way. Creative: placing is free and unlimited, removing gives nothing, and every
/// block can be placed. In both, a block only goes on the classes of structure its family allows
/// (base, capital, small, hover), as Empyrion's blocks say where they may be placed.
/// </summary>
public sealed class BlockPlacer
{
    public ItemCatalogue Items { get; }
    public WorldRules Rules { get; }
    public CrateYard Crates { get; }

    public BlockPlacer(ItemCatalogue items, WorldRules rules, CrateYard crates)
    {
        Items = items;
        Rules = rules;
        Crates = crates;
    }

    /// <summary>What placing this block would use here, now: one of its item (nothing in creative).</summary>
    public Bill CostOf(Structure s, in Block b) =>
        Rules.Creative || BuildingBridge.ItemOf(Items, s, b) is not ItemDef item ? new Bill() : Bill.Of((item.Id, 1));

    /// <summary>Why a block can't go on this structure's class, or null if it can (blocks without an item go anywhere their grid allows).</summary>
    public string ClassProblem(Structure s, in Block b, StructureClass? cls = null)
    {
        ItemDef item = BuildingBridge.ItemOf(Items, s, b);
        StructureClass c = cls ?? BuildingBridge.ClassOf(s);
        if (item == null || item.PlacesOn(c)) return null;
        return $"{item.Name}: {StructureClasses.Only(item.Places)}";
    }

    /// <summary>
    /// Places a block, using one of its item from <paramref name="source"/> in survival. The
    /// structure's class is worked out from it unless given (building's model can't tell a hover
    /// vessel from a small one). Call <see cref="Sync"/> after placing a container.
    /// </summary>
    public PlaceResult Place(Structure s, Int3 p, Block b, SharedStorage source, StructureClass? cls = null)
    {
        PlaceError rule = Building.Check(s, p, b);
        if (rule != PlaceError.None) return new(PlaceStatus.Blocked, rule, new Bill(), new Bill());
        if (ClassProblem(s, b, cls) != null) return new(PlaceStatus.WrongClass, PlaceError.None, new Bill(), new Bill());

        Bill paid = new();
        if (Rules.Survival)
        {
            ItemDef item = BuildingBridge.ItemOf(Items, s, b);
            if (item == null) return new(PlaceStatus.NoBill, PlaceError.None, new Bill(), new Bill());
            paid = Bill.Of((item.Id, 1));
            if (!source.TryTake(paid)) return new(PlaceStatus.NotEnoughMaterials, PlaceError.NotEnoughMaterials, new Bill(), paid);
        }
        Building.Place(s, p, b); // already checked, and building's own inventory is not used
        return new(PlaceStatus.Placed, PlaceError.None, paid, new Bill());
    }

    /// <summary>
    /// Takes a block down with the tool's deconstruct mode. Survival gives its item back into
    /// <paramref name="into"/> (whole, damaged or not, as Empyrion's pick-up does), and spills what
    /// doesn't fit into a crate at (x, y, z). Sections it cuts loose become wrecks, blocks and all.
    /// Call <see cref="Sync"/> after taking a container away.
    /// </summary>
    public TakeDownResult Remove(Structure s, Int3 p, SharedStorage into, double x = 0, double y = 0, double z = 0)
    {
        if (!s.TryGet(p, out Block b)) return new(Building.Remove(s, p, null), new Bill(), new Bill(), null);
        Bill refund = Rules.Survival && BuildingBridge.ItemOf(Items, s, b) is ItemDef item ? Bill.Of((item.Id, 1)) : new Bill();
        RemoveResult removal = Building.Remove(s, p, null);
        if (removal.Status != RemoveStatus.Removed) return new(removal, new Bill(), new Bill(), null);

        Bill spilled = into.AddAll(refund);
        SpillCrate crate = Crates.Spill(spilled, x, y, z);
        return new(removal, refund, spilled, crate);
    }

    /// <summary>
    /// Steel to hardened to combat, in place (Empyrion's upgrade): survival uses one of the better
    /// block's item from the source and gives the old block's item back, as one step.
    /// </summary>
    public PlaceResult Upgrade(Structure s, Int3 p, SharedStorage source, double x = 0, double y = 0, double z = 0)
    {
        if (!s.TryGet(p, out Block b)) return new(PlaceStatus.Blocked, PlaceError.None, new Bill(), new Bill());
        BlockType t = s.TypeOf(b);
        if (t.Upgrade == null) return new(PlaceStatus.Blocked, PlaceError.None, new Bill(), new Bill());
        Bill paid = new();
        if (Rules.Survival)
        {
            ItemDef better = Items.BlockItem(t.Upgrade.Id, BuildingBridge.IsSmall(s));
            if (better == null) return new(PlaceStatus.NoBill, PlaceError.None, new Bill(), new Bill());
            paid = Bill.Of((better.Id, 1));
            if (!source.TryTake(paid)) return new(PlaceStatus.NotEnoughMaterials, PlaceError.NotEnoughMaterials, new Bill(), paid);
            if (BuildingBridge.ItemOf(Items, s, b) is ItemDef old) Crates.Spill(source.AddAll(Bill.Of((old.Id, 1))), x, y, z);
        }
        Building.Upgrade(s, p);
        return new(PlaceStatus.Placed, PlaceError.None, paid, new Bill());
    }

    /// <summary>
    /// Matches storage to the structure: capacity from its containers, overflow spilled to a
    /// crate, and the cargo's volume written back for the mass readout. Returns what overflowed.
    /// </summary>
    public Bill Sync(Structure s, SharedStorage storage, double x, double y, double z, bool spill = true)
    {
        storage.CapacitySu = BuildingBridge.StorageCapacity(s);
        Bill overflow = storage.TakeOverflow();
        if (spill) Crates.Spill(overflow, x, y, z);
        s.CargoSu = storage.UsedSu;
        return overflow;
    }
}
