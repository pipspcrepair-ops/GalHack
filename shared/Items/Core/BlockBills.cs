using System.Collections.Generic;

namespace GalHack.Items;

/// <summary>
/// What one block is made of, per grid, and the block family items made from it (Empyrion's way:
/// blocks are craftable items). A constructor makes a block item from its bill; placing a block in
/// survival uses one item, and taking it down gives the item back. Every shape of an armour
/// material is placed from the same family item, so every shape costs what its cube does (design
/// section 11). The small and large grids are separate families, with separate items.
/// </summary>
public sealed class BlockBill
{
    public string BlockId { get; init; }
    public Bill Small { get; init; } = new();
    public Bill Large { get; init; } = new();
    /// <summary>Needs a unique find (salvage only), so it has no path from ore.</summary>
    public bool UniqueOnly { get; init; }
    /// <summary>Weapons: the ammo item it fires from shared storage.</summary>
    public string Ammo { get; init; }

    /// <summary>Its tech-tree nodes on each grid (data: <c>unlock.small</c>, <c>unlock.large</c>), or null where it is always available.</summary>
    public Unlock UnlockSmall { get; init; }
    public Unlock UnlockLarge { get; init; }

    /// <summary>The grids it can be placed on (every catalogue block is on both; deco pieces may be on one).</summary>
    public bool OnSmall { get; init; } = true;
    public bool OnLarge { get; init; } = true;
    public BlockFamily Family { get; init; } = BlockFamily.Device;

    /// <summary>The block family items, per grid (null where it isn't on that grid).</summary>
    public ItemDef ItemSmall { get; internal set; }
    public ItemDef ItemLarge { get; internal set; }
    public ItemDef ItemFor(bool smallGrid) => smallGrid ? ItemSmall : ItemLarge;
    public bool On(bool smallGrid) => smallGrid ? OnSmall : OnLarge;

    public Bill For(bool smallGrid) => smallGrid ? Small : Large;
    public Unlock UnlockFor(bool smallGrid) => smallGrid ? UnlockSmall : UnlockLarge;

    /// <summary>A block's tech-tree node id on a grid: <c>thruster@small</c>.</summary>
    public static string NodeId(string blockId, bool smallGrid) => $"{blockId}@{(smallGrid ? "small" : "large")}";
}

/// <summary>Every block's bill, keyed by building's block id.</summary>
public sealed class BlockBills
{
    readonly Dictionary<string, BlockBill> _bills = new();

    public IEnumerable<BlockBill> All => _bills.Values;
    public int Count => _bills.Count;

    /// <summary>Seconds the blueprint factory takes to fit one block, per grid.</summary>
    public double AssemblySmall { get; internal set; } = 0.4;
    public double AssemblyLarge { get; internal set; } = 1.6;
    public double AssemblySeconds(bool smallGrid) => smallGrid ? AssemblySmall : AssemblyLarge;

    public bool TryGet(string blockId, out BlockBill bill) => _bills.TryGetValue(blockId, out bill);
    public BlockBill Get(string blockId) => _bills.TryGetValue(blockId, out BlockBill b) ? b : throw new KeyNotFoundException($"no bill for block '{blockId}'");

    /// <summary>The bill for one block on a grid (a copy, safe to change).</summary>
    public Bill For(string blockId, bool smallGrid) => Get(blockId).For(smallGrid).Clone();

    /// <summary>The ammo a weapon block fires, or null if it isn't a weapon.</summary>
    public string AmmoFor(string blockId) => _bills.TryGetValue(blockId, out BlockBill b) ? b.Ammo : null;

    /// <summary>The block family item for a block on a grid, or null if there is none.</summary>
    public ItemDef ItemFor(string blockId, bool smallGrid) => _bills.TryGetValue(blockId, out BlockBill b) ? b.ItemFor(smallGrid) : null;

    /// <summary>A build list as block items: one item per block (what survival placement uses).</summary>
    public Bill ItemsFor(IEnumerable<BlockCount> blocks)
    {
        var total = new Bill();
        foreach (BlockCount b in blocks) total.Add(Get(b.BlockId).ItemFor(b.Small)?.Id ?? throw new KeyNotFoundException($"no item for block '{b.BlockId}'"), b.Count);
        return total;
    }

    /// <summary>The summed bill of a build list.</summary>
    public Bill Total(IEnumerable<BlockCount> blocks)
    {
        var total = new Bill();
        foreach (BlockCount b in blocks) total.Add(Get(b.BlockId).For(b.Small), b.Count);
        return total;
    }

    internal void Add(BlockBill bill) => _bills[bill.BlockId] = bill;
}
