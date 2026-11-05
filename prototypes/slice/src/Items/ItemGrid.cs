using System;
using System.Collections.Generic;
using System.Linq;
using GalHack.Items;

namespace Slice.Items;

/// <summary>
/// One inventory slot: a stack of one item (up to its stack size), or, on the toolbar only, a
/// block from the build palette (blocks are never items: placing one pays its bill).
/// </summary>
public sealed class Slot
{
    public string Item;
    public int Count;
    /// <summary>Toolbar only: a block shortcut (building's block id), paid for when placed.</summary>
    public string Block;

    public bool IsEmpty => Item == null && Block == null;
    public void Clear() { Item = null; Count = 0; Block = null; }
    public override string ToString() => Block != null ? $"[{Block}]" : Item == null ? "-" : $"{Count} {Item}";
}

/// <summary>
/// A grid of slots over a <see cref="SharedStorage"/> (Empyrion's bag, shaped by the design's
/// one-storage rule): the storage keeps the counts, mass and volume every machine and placement
/// reads; the grid is how a player sees and arranges them, stacks merging up to each item's stack
/// size. Anything that changes the storage directly (a constructor taking its inputs, a block
/// placed, a dig) is laid into the slots on the next <see cref="Reconcile"/>; anything moved
/// through the grid changes both at once. Nothing is ever lost: what has no slot is reported as
/// <see cref="Overflow"/> for the owner to set down in the world.
/// </summary>
public sealed class ItemGrid
{
    public readonly SharedStorage Storage;
    public readonly int Columns;
    public readonly List<Slot> Slots = new();

    /// <summary>The first slots are a toolbar (the player's 1 to 9): new items only go there when the rest is full.</summary>
    public readonly int Toolbar;

    /// <summary>A structure's storage view grows a row at a time as stacks need slots; a bag doesn't.</summary>
    public readonly bool Growable;

    public string Name => Storage.Name;
    ItemCatalogue Items => Storage.Catalogue;
    int _seen = -1;

    /// <summary>Counts in storage with no slot to show them in (a full bag): the owner spills them.</summary>
    public Bill Overflow { get; private set; } = new();

    public ItemGrid(SharedStorage storage, int columns, int rows, int toolbar = 0, bool growable = false)
    {
        Storage = storage;
        Columns = columns;
        Toolbar = toolbar;
        Growable = growable;
        for (int i = 0; i < toolbar + columns * rows; i++) Slots.Add(new Slot());
    }

    public int Count => Slots.Count;
    public Slot this[int i] => Slots[i];
    public int BagSlots => Slots.Count - Toolbar;
    public double MassKg => Storage.MassKg;
    public double UsedSu => Storage.UsedSu;

    /// <summary>Lays the storage's counts into the slots if anything changed them. Cheap when nothing did.</summary>
    public void Reconcile()
    {
        if (Storage.Version == _seen) return;
        var overflow = new Bill();
        var slotted = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (Slot s in Slots)
        {
            if (s.Item == null) continue;
            if (s.Count <= 0 || !Items.Has(s.Item)) { s.Clear(); continue; }
            slotted[s.Item] = slotted.GetValueOrDefault(s.Item) + s.Count;
        }
        foreach (string id in slotted.Keys.ToList())
        {
            int extra = slotted[id] - Storage.Count(id);
            // Fewer than the slots show: take them off the last bag stacks first, the toolbar (the first slots) last.
            for (int i = Slots.Count - 1; i >= 0 && extra > 0; i--)
            {
                Slot s = Slots[i];
                if (s.Item != id) continue;
                int take = Math.Min(extra, s.Count);
                s.Count -= take;
                extra -= take;
                if (s.Count == 0) s.Clear();
            }
        }
        foreach (var (item, count) in Storage.Stacks)
        {
            int missing = count - Slots.Where(s => s.Item == item.Id).Sum(s => s.Count);
            if (missing > 0)
            {
                int left = Place(item, missing);
                if (left > 0) overflow.Add(item.Id, left);
            }
        }
        Overflow = overflow;
        _seen = Storage.Version;
    }

    /// <summary>Lays units into slots: topping up stacks of the item, then empty bag slots, then the toolbar. Returns what didn't fit.</summary>
    int Place(ItemDef item, int n)
    {
        int stack = item.StackSize;
        foreach (Slot s in BagFirst())
        {
            if (n == 0) break;
            if (s.Item != item.Id || s.Count >= stack) continue;
            int put = Math.Min(n, stack - s.Count);
            s.Count += put;
            n -= put;
        }
        while (n > 0)
        {
            Slot empty = BagFirst().FirstOrDefault(s => s.IsEmpty);
            if (empty == null && Growable)
            {
                for (int i = 0; i < Columns; i++) Slots.Add(new Slot());
                continue;
            }
            if (empty == null) break;
            empty.Item = item.Id;
            empty.Count = Math.Min(n, stack);
            n -= empty.Count;
        }
        return n;
    }

    IEnumerable<Slot> BagFirst()
    {
        for (int i = Toolbar; i < Slots.Count; i++) yield return Slots[i];
        for (int i = 0; i < Toolbar; i++) yield return Slots[i];
    }

    /// <summary>Marks the grid as matching its storage (after a move that changed both together).</summary>
    void Seen() => _seen = Storage.Version;

    // ---- Moving -------------------------------------------------------------------------------

    /// <summary>
    /// Moves up to <paramref name="count"/> of the stack in one slot onto another slot, in this
    /// grid or another (drag and drop). Onto the same item it merges up to the stack size; onto
    /// an empty slot it lands there; onto a different item in the same grid the two swap. Into
    /// another grid only what its storage has room for goes. Returns how many moved.
    /// </summary>
    public static int Move(ItemGrid from, int fromSlot, ItemGrid to, int toSlot, int count = int.MaxValue)
    {
        from.Reconcile();
        to.Reconcile();
        Slot a = from.Slots[fromSlot], b = to.Slots[toSlot];
        if (a == b || a.IsEmpty) return 0;
        if (a.Block != null)
        {
            // A block shortcut moves only along the toolbar.
            if (to != from || toSlot >= to.Toolbar) return 0;
            (a.Block, b.Block, a.Item, a.Count, b.Item, b.Count) = (b.Block, a.Block, b.Item, b.Count, a.Item, a.Count);
            return 1;
        }
        ItemDef item = from.Items.Item(a.Item);
        count = Math.Min(count, a.Count);
        if (b.Block != null) return 0;
        if (b.Item != null && b.Item != a.Item)
        {
            if (from != to || count != a.Count) return 0;
            (a.Item, a.Count, b.Item, b.Count) = (b.Item, b.Count, a.Item, a.Count);
            return count;
        }
        int room = item.StackSize - (b.Item == a.Item ? b.Count : 0);
        int n = Math.Min(count, room);
        if (from != to)
        {
            n = to.Storage.Add(item.Id, n);
            from.Storage.Remove(item.Id, n);
        }
        if (n <= 0) return 0;
        a.Count -= n;
        if (a.Count == 0) a.Clear();
        b.Item = item.Id;
        b.Count += n;
        from.Seen();
        to.Seen();
        return n;
    }

    /// <summary>Shift-click: the whole stack in a slot to the other grid (or, within one grid, between toolbar and bag). Returns how many moved.</summary>
    public static int QuickMove(ItemGrid from, int fromSlot, ItemGrid to)
    {
        from.Reconcile();
        Slot a = from.Slots[fromSlot];
        if (a.Item == null) return 0;
        string id = a.Item;
        int n = a.Count;
        if (to == from)
        {
            // Toolbar to bag, or bag to toolbar.
            bool toToolbar = fromSlot >= from.Toolbar;
            int start = toToolbar ? 0 : from.Toolbar, end = toToolbar ? from.Toolbar : from.Slots.Count;
            int moved = 0;
            for (int i = start; i < end && a.Count > 0; i++)
                if (from.Slots[i].Item == id || from.Slots[i].IsEmpty) moved += Move(from, fromSlot, from, i, a.Count);
            return moved;
        }
        int put = to.Storage.Add(id, n);
        if (put <= 0) return 0;
        from.Storage.Remove(id, put);
        a.Count -= put;
        if (a.Count == 0) a.Clear();
        from.Seen();
        to.Reconcile();
        return put;
    }

    /// <summary>Right-click: half the stack into the first empty bag slot of the same grid. False if there is none or nothing to split.</summary>
    public bool Split(int slot)
    {
        Reconcile();
        Slot a = Slots[slot];
        if (a.Item == null || a.Count < 2) return false;
        Slot empty = BagFirst().FirstOrDefault(s => s.IsEmpty);
        if (empty == null) return false;
        int half = a.Count / 2;
        a.Count -= half;
        empty.Item = a.Item;
        empty.Count = half;
        return true;
    }

    /// <summary>Everything in one grid that fits into another, stack by stack. Returns what moved.</summary>
    public static Bill MoveAll(ItemGrid from, ItemGrid to, bool includeToolbar = true)
    {
        from.Reconcile();
        var moved = new Bill();
        for (int i = 0; i < from.Slots.Count; i++)
        {
            if (!includeToolbar && i < from.Toolbar) continue;
            Slot s = from.Slots[i];
            if (s.Item == null) continue;
            string id = s.Item;
            int n = QuickMove(from, i, to);
            if (n > 0) moved.Add(id, n);
        }
        return moved;
    }

    /// <summary>Takes one unit out of a slot's stack (eating, using, placing a core). False if it was empty.</summary>
    public bool TakeOne(int slot)
    {
        Reconcile();
        Slot s = Slots[slot];
        if (s.Item == null) return false;
        if (Storage.Remove(s.Item, 1) == 0) return false;
        s.Count--;
        if (s.Count == 0) s.Clear();
        Seen();
        return true;
    }

    /// <summary>Puts a block shortcut on a toolbar slot (dragged from the build palette). Any items there go back to the bag.</summary>
    public bool SetBlock(int toolbarSlot, string blockId)
    {
        if (toolbarSlot < 0 || toolbarSlot >= Toolbar) return false;
        Slot s = Slots[toolbarSlot];
        if (s.Item != null)
        {
            string id = s.Item;
            int n = s.Count;
            s.Clear();
            int left = Place(Items.Item(id), n);
            if (left > 0)
            {
                // No room in the bag: the stack stays where it was.
                s.Item = id;
                s.Count = n;
                Reconcile();
                return false;
            }
        }
        s.Item = null;
        s.Count = 0;
        s.Block = blockId;
        return true;
    }

    /// <summary>The first slot holding an item, or -1.</summary>
    public int IndexOf(string itemId)
    {
        Reconcile();
        return Slots.FindIndex(s => s.Item == itemId);
    }

    /// <summary>Slots in use.</summary>
    public int Used => Slots.Count(s => !s.IsEmpty);

    /// <summary>A short text that changes whenever the grid would look different (screens redraw on it).</summary>
    public string Signature => $"{Storage.Version}|{string.Join(",", Slots.Select(s => s.ToString()))}";
}
