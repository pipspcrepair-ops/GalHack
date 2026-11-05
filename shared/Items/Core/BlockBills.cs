using System.Collections.Generic;

namespace GalHack.Items;

/// <summary>
/// What one block costs to place, per grid. Blocks are never items: placing one in survival takes
/// its bill from shared storage and removing it gives the whole bill back. Every shape of an
/// armour material costs the same as its cube (design section 11).
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

    public Bill For(bool smallGrid) => smallGrid ? Small : Large;
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

    /// <summary>The summed bill of a build list.</summary>
    public Bill Total(IEnumerable<BlockCount> blocks)
    {
        var total = new Bill();
        foreach (BlockCount b in blocks) total.Add(Get(b.BlockId).For(b.Small), b.Count);
        return total;
    }

    internal void Add(BlockBill bill) => _bills[bill.BlockId] = bill;
}
