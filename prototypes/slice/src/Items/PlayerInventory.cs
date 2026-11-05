using GalHack.Items;

namespace Slice.Items;

/// <summary>What the selected toolbar slot puts in your hands.</summary>
public enum HandKind { Empty, Tool, Block, Core, Device, Consumable, Blueprint, Other }

/// <summary>
/// The player's inventory, Empyrion's shape on shared/Items: a toolbar of nine slots (1 to 9,
/// the selected one in your hands) and a bag of 8 x 6 slots, all over one storage that counts
/// mass and volume. Tools, placeables and consumables go on the toolbar; so do blocks from the
/// build palette (shortcuts: a block is paid for from storage when it is placed).
/// </summary>
public sealed class PlayerInventory
{
    public const int ToolbarSlots = 9, Columns = 8, Rows = 6;

    /// <summary>The bag's room, storage units (a big suit pack: the grid's slots are usually the limit).</summary>
    public const double Capacity = 1500;

    /// <summary>Toolbar shortcut for the starter vessel's blueprint (the blueprint projector).</summary>
    public const string BlueprintBlock = "@blueprint";

    public readonly SharedStorage Storage;
    public readonly ItemGrid Grid;
    readonly ItemCatalogue _items;

    /// <summary>The toolbar slot in your hands (0 to 8).</summary>
    public int Selected;

    public PlayerInventory(ItemCatalogue items)
    {
        _items = items;
        Storage = new SharedStorage(items, "Pack", Capacity);
        Grid = new ItemGrid(Storage, Columns, Rows, ToolbarSlots);
    }

    public int this[string id] => Storage.Count(id);

    public Slot InHand => Grid[Selected];

    public HandKind Hand
    {
        get
        {
            Grid.Reconcile();
            Slot s = InHand;
            if (s.Block == BlueprintBlock) return HandKind.Blueprint;
            if (s.Block != null) return HandKind.Block;
            if (s.Item == null) return HandKind.Empty;
            ItemDef item = _items.Item(s.Item);
            if (s.Item == "survival-tool") return HandKind.Tool;
            if (s.Item is "base-core" or "vessel-core" or "hover-core") return HandKind.Core;
            if (_items.Machines.All is var all && System.Linq.Enumerable.Any(all, m => m.ItemId == s.Item)) return HandKind.Device;
            if (item.Category == ItemCategory.Consumable || s.Item == "meat") return HandKind.Consumable;
            return HandKind.Other;
        }
    }

    /// <summary>Adds items (gathered, crafted, looted); returns what had no room (the caller sets it down in the world).</summary>
    public Bill Add(string id, int n)
    {
        int put = Storage.Add(id, n);
        Grid.Reconcile();
        var left = new Bill();
        if (put < n) left.Add(id, n - put);
        return left;
    }
}
