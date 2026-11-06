using System;
using System.Collections.Generic;
using System.Linq;
using GalHack.Items;

namespace Slice.Items;

/// <summary>What the selected toolbar slot puts in your hands.</summary>
public enum HandKind { Empty, Tool, Block, Core, Device, Consumable, Blueprint, Other }

/// <summary>Which toolbar the number keys use: your own, or the linked structure's (Empyrion's virtual toolbar).</summary>
public enum BarSource { Personal, Structure }

/// <summary>
/// The player's inventory, Empyrion's shape on shared/Items: a toolbar of nine slots (1 to 9,
/// the selected one in your hands) and a bag of 8 x 6 slots, all over one storage that counts
/// mass and volume. It is the player's own and goes everywhere with them; a structure's shared
/// storage is a different inventory and the two never merge.
///
/// Beside your own toolbar there is a structure toolbar (Empyrion's virtual toolbar): nine
/// shortcuts into the shared storage of the structure you are linked to (the one whose area you
/// are in). You choose which toolbar is in use (double-tap a number, or Y): blocks placed from
/// your toolbar come from your own stacks, blocks placed from the structure toolbar come from that
/// structure's storage. Nothing is ever taken from one when you chose the other.
/// </summary>
public sealed class PlayerInventory
{
    public const int ToolbarSlots = 9, Columns = 8, Rows = 6;

    /// <summary>The bag's room, storage units (a big suit pack: the grid's slots are usually the limit).</summary>
    public const double Capacity = 1500;

    /// <summary>Toolbar shortcut for the starter vessel's blueprint (the blueprint projector).</summary>
    public const string BlueprintBlock = "@blueprint";

    /// <summary>Items that found a new structure when set down.</summary>
    public static readonly string[] CoreItems = { "base-core", "vessel-core", "hover-core", "capital-core" };

    public readonly SharedStorage Storage;
    public readonly ItemGrid Grid;
    readonly ItemCatalogue _items;

    /// <summary>The toolbar slot in your hands (0 to 8).</summary>
    public int Selected;

    /// <summary>Which toolbar is in use.</summary>
    public BarSource Bar = BarSource.Personal;

    /// <summary>The structure storage you are linked to (you are in its area), or null; set by the game every tick.</summary>
    public SharedStorage Linked { get; private set; }
    /// <summary>The linked structure's name, for the toolbar's label.</summary>
    public string LinkedName { get; private set; } = "";

    readonly Dictionary<SharedStorage, string[]> _structureBars = new();
    readonly Slot _virtual = new();

    public PlayerInventory(ItemCatalogue items)
    {
        _items = items;
        Storage = new SharedStorage(items, "Pack", Capacity);
        Grid = new ItemGrid(Storage, Columns, Rows, ToolbarSlots);
    }

    public int this[string id] => Storage.Count(id);

    /// <summary>Links the structure toolbar to a structure's storage (null: none in range; the structure toolbar is then empty).</summary>
    public void Link(SharedStorage storage, string name)
    {
        Linked = storage;
        LinkedName = name ?? "";
    }

    /// <summary>The linked structure's toolbar: nine item ids (null for an empty slot), kept per structure.</summary>
    public string[] StructureBar
    {
        get
        {
            if (Linked == null) return new string[ToolbarSlots];
            if (!_structureBars.TryGetValue(Linked, out string[] bar)) _structureBars[Linked] = bar = new string[ToolbarSlots];
            return bar;
        }
    }

    /// <summary>The structure toolbar of a particular storage (tests, the CORE tab).</summary>
    public string[] BarFor(SharedStorage storage)
    {
        if (!_structureBars.TryGetValue(storage, out string[] bar)) _structureBars[storage] = bar = new string[ToolbarSlots];
        return bar;
    }

    /// <summary>Puts an item on a slot of the linked structure's toolbar (dragged from its storage). False with no structure linked.</summary>
    public bool SetStructureSlot(int slot, string itemId)
    {
        if (Linked == null || slot < 0 || slot >= ToolbarSlots) return false;
        StructureBar[slot] = itemId;
        return true;
    }

    /// <summary>True when the structure toolbar is in use and a structure is linked.</summary>
    public bool OnStructureBar => Bar == BarSource.Structure && Linked != null;

    /// <summary>Switches toolbar (your own, or the linked structure's).</summary>
    public void ToggleBar() => Bar = Bar == BarSource.Personal ? BarSource.Structure : BarSource.Personal;

    /// <summary>
    /// The slot in your hands: on your own toolbar a slot of your grid; on the structure toolbar a
    /// view of the linked storage (its item, and how many the storage holds).
    /// </summary>
    public Slot InHand
    {
        get
        {
            if (Bar == BarSource.Personal) return Grid[Selected];
            string id = Linked == null ? null : StructureBar[Selected];
            _virtual.Block = null;
            _virtual.Item = id;
            _virtual.Count = id == null ? 0 : Linked.Count(id);
            return _virtual;
        }
    }

    /// <summary>The item in hand (a stack's, or a shortcut's item id), or null.</summary>
    public string HandItem
    {
        get
        {
            Slot s = InHand;
            return s.Block != null && s.Block != BlueprintBlock ? s.Block : s.Item;
        }
    }

    /// <summary>The source a placement from the hand takes from: your bag, or the linked structure's storage.</summary>
    public SharedStorage HandSource => OnStructureBar ? Linked : Storage;

    public HandKind Hand
    {
        get
        {
            Grid.Reconcile();
            Slot s = InHand;
            if (s.Block == BlueprintBlock) return HandKind.Blueprint;
            string id = HandItem;
            if (id == null || !_items.TryGet(id, out ItemDef item)) return HandKind.Empty;
            if (id == "survival-tool") return HandKind.Tool;
            if (CoreItems.Contains(id)) return HandKind.Core;
            if (item.IsBlock) return HandKind.Block;
            if (_items.Machines.All.Any(m => m.ItemId == id)) return HandKind.Device;
            if (item.Category == ItemCategory.Consumable || id == "meat") return HandKind.Consumable;
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

    /// <summary>Removes items from the bag (a script or a trade); returns how many came out.</summary>
    public int Remove(string id, int n)
    {
        int took = Storage.Remove(id, n);
        Grid.Reconcile();
        return took;
    }

    /// <summary>Does the bag hold at least this many?</summary>
    public bool Has(string id, int n = 1) => Storage.Count(id) >= n;
}
