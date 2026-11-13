using System;
using System.Collections.Generic;

namespace GalHack.Items;

/// <summary>
/// What one block is made of, and the one block item made from it (Empyrion's way: blocks are
/// craftable items). A fabricator makes a block item from its bill; placing a block in survival
/// uses one item, and taking it down gives the item back. Every shape of an armour block is placed
/// from the same item, so every shape costs what its cube does (design section 11).
/// </summary>
public sealed class BlockBill
{
    public string BlockId { get; init; }
    public Bill Bill { get; init; } = new();
    /// <summary>Needs a unique find (salvage only), so it has no path from ore.</summary>
    public bool UniqueOnly { get; init; }
    /// <summary>Weapons: the ammo item it fires from shared storage.</summary>
    public string Ammo { get; init; }
    /// <summary>Its tech-tree node (data: <c>unlock</c>), or null where it is always available.</summary>
    public Unlock Unlock { get; init; }
    public BlockFamily Family { get; init; } = BlockFamily.Device;
    /// <summary>The block item.</summary>
    public ItemDef Item { get; internal set; }

    /// <summary>Cells the block fills (building's size, set when the two catalogues are linked; one until then).</summary>
    public int Cells { get; set; } = 1;
    /// <summary>What the data says about fitting and placing one, or null for the defaults below.</summary>
    public double? Assembly { get; init; }
    public int? PlaceXp { get; init; }

    /// <summary>Seconds the blueprint factory takes to fit one: 0.4 s for a one-cell block, rising to 1.6 s at eight cells.</summary>
    public double AssemblySeconds => Assembly ?? 0.4 + 1.2 * (Math.Min(Cells, 8) - 1) / 7.0;
    /// <summary>Experience for placing one: 1 for an armour block, else 1 a cell up to 8.</summary>
    public int Xp => PlaceXp ?? (Family == BlockFamily.Armour ? 1 : Math.Min(Cells, 8));
}

/// <summary>Every block's bill, keyed by building's block id.</summary>
public sealed class BlockBills
{
    readonly Dictionary<string, BlockBill> _bills = new();

    public IEnumerable<BlockBill> All => _bills.Values;
    public int Count => _bills.Count;

    public bool TryGet(string blockId, out BlockBill bill) => _bills.TryGetValue(blockId, out bill);
    public BlockBill Get(string blockId) => _bills.TryGetValue(blockId, out BlockBill b) ? b : throw new KeyNotFoundException($"no bill for block '{blockId}'");

    /// <summary>The bill for one block (a copy, safe to change).</summary>
    public Bill For(string blockId) => Get(blockId).Bill.Clone();

    /// <summary>The ammo a weapon block fires, or null if it isn't a weapon.</summary>
    public string AmmoFor(string blockId) => _bills.TryGetValue(blockId, out BlockBill b) ? b.Ammo : null;

    /// <summary>The block item for a block, or null if there is none.</summary>
    public ItemDef ItemFor(string blockId) => _bills.TryGetValue(blockId, out BlockBill b) ? b.Item : null;

    /// <summary>A build list as block items: one item per block (what survival placement uses).</summary>
    public Bill ItemsFor(IEnumerable<BlockCount> blocks)
    {
        var total = new Bill();
        foreach (BlockCount b in blocks) total.Add(Get(b.BlockId).Item?.Id ?? throw new KeyNotFoundException($"no item for block '{b.BlockId}'"), b.Count);
        return total;
    }

    /// <summary>The summed bill of a build list.</summary>
    public Bill Total(IEnumerable<BlockCount> blocks)
    {
        var total = new Bill();
        foreach (BlockCount b in blocks) total.Add(Get(b.BlockId).Bill, b.Count);
        return total;
    }

    internal void Add(BlockBill bill) => _bills[bill.BlockId] = bill;
}
