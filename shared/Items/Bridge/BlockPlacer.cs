using GalHack.Structures;

namespace GalHack.Items.Bridge;

public enum PlaceStatus
{
    Placed,
    /// <summary>Building's own rules said no (occupied, not attached...): see <see cref="PlaceResult.Rule"/>.</summary>
    Blocked,
    /// <summary>Survival: storage lacks part of the bill (see <see cref="PlaceResult.Missing"/>).</summary>
    NotEnoughMaterials,
    /// <summary>Survival: the block has no bill, so it can't be paid for.</summary>
    NoBill,
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
/// Placing and taking down blocks in either game mode, through building's own rules.
///
/// Survival: placing a block takes its bill from the structure's shared storage, and removing it
/// gives the whole bill back, nothing lost; whatever storage can't take goes into a spill crate
/// that never despawns. A container taken away shrinks storage, and what no longer fits spills
/// the same way. Creative: placing is free and unlimited, removing refunds nothing, and every
/// block can be placed, priced or not.
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

    /// <summary>What placing this block would cost here, now (nothing in creative).</summary>
    public Bill CostOf(Structure s, in Block b) =>
        Rules.Creative || !BuildingBridge.Priced(Items, s, b) ? new Bill() : BuildingBridge.BillOf(Items, s, b);

    public PlaceResult Place(Structure s, Int3 p, Block b, SharedStorage storage)
    {
        PlaceError rule = Building.Check(s, p, b);
        if (rule != PlaceError.None) return new(PlaceStatus.Blocked, rule, new Bill(), new Bill());

        Bill paid = new();
        if (Rules.Survival)
        {
            if (!BuildingBridge.Priced(Items, s, b)) return new(PlaceStatus.NoBill, PlaceError.None, new Bill(), new Bill());
            paid = BuildingBridge.BillOf(Items, s, b);
            if (!storage.TryTake(paid)) return new(PlaceStatus.NotEnoughMaterials, PlaceError.NotEnoughMaterials, new Bill(), storage.Missing(paid));
        }
        Building.Place(s, p, b); // already checked, and building's own inventory is not used
        storage.CapacitySu = BuildingBridge.StorageCapacity(s); // placing only ever adds room
        s.CargoSu = storage.UsedSu;
        return new(PlaceStatus.Placed, PlaceError.None, paid, new Bill());
    }

    /// <summary>
    /// Takes a block down with the multi-tool. Survival refunds its whole bill into storage, and
    /// spills what doesn't fit into a crate at (x, y, z). Sections it cuts loose become wrecks,
    /// blocks and all.
    /// </summary>
    public TakeDownResult Remove(Structure s, Int3 p, SharedStorage storage, double x = 0, double y = 0, double z = 0)
    {
        if (!s.TryGet(p, out Block b)) return new(Building.Remove(s, p, null), new Bill(), new Bill(), null);
        Bill refund = Rules.Survival && BuildingBridge.Priced(Items, s, b) ? BuildingBridge.BillOf(Items, s, b) : new Bill();
        RemoveResult removal = Building.Remove(s, p, null);
        if (removal.Status != RemoveStatus.Removed) return new(removal, new Bill(), new Bill(), null);

        Bill spilled = storage.AddAll(refund);
        // A container going (or a section severed) shrinks storage: what no longer fits spills too.
        spilled.Add(Sync(s, storage, x, y, z, spill: false));
        SpillCrate crate = Crates.Spill(spilled, x, y, z);
        return new(removal, refund, spilled, crate);
    }

    /// <summary>Steel to hardened to combat, in place. Survival refunds the old bill and takes the new one, as one step.</summary>
    public PlaceResult Upgrade(Structure s, Int3 p, SharedStorage storage, double x = 0, double y = 0, double z = 0)
    {
        if (!s.TryGet(p, out Block b)) return new(PlaceStatus.Blocked, PlaceError.None, new Bill(), new Bill());
        BlockType t = s.TypeOf(b);
        if (t.Upgrade == null) return new(PlaceStatus.Blocked, PlaceError.None, new Bill(), new Bill());
        Bill paid = new();
        if (Rules.Survival)
        {
            if (!Items.Blocks.TryGet(t.Upgrade.Id, out _)) return new(PlaceStatus.NoBill, PlaceError.None, new Bill(), new Bill());
            Bill oldBill = BuildingBridge.Priced(Items, s, b) ? BuildingBridge.BillOf(Items, s, b) : new Bill();
            Bill newBill = Items.Blocks.For(t.Upgrade.Id, BuildingBridge.IsSmall(s));
            paid = newBill.Minus(oldBill);
            if (!storage.TryTake(paid)) return new(PlaceStatus.NotEnoughMaterials, PlaceError.NotEnoughMaterials, new Bill(), storage.Missing(paid));
            Crates.Spill(storage.AddAll(oldBill.Minus(newBill)), x, y, z);
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
