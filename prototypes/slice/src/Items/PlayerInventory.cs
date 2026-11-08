using System;
using System.Collections.Generic;
using System.Linq;
using GalHack.Items;

namespace Slice.Items;

/// <summary>What the selected toolbar slot puts in your hands.</summary>
public enum HandKind { Empty, Tool, Block, Core, Device, Consumable, Blueprint, Vehicle, Scanner, Other, Weapon }

/// <summary>Which toolbar the number keys use: your own, or the remote one (items left in cores' storage).</summary>
public enum BarSource { Personal, Remote }

/// <summary>
/// A remote toolbar slot: a reference to one item in one core's storage. It shows the storage's live
/// count (falling as anyone uses it, refilling when the storage does); nothing leaves the storage
/// until the item is used.
/// </summary>
public sealed class RemoteSlot
{
    public SharedStorage Source;
    /// <summary>The origin core's name, for the pip's hover and the selected slot.</summary>
    public string Origin = "";
    public string ItemId;
    public int Count => Source.Count(ItemId);
}

/// <summary>
/// The player's inventory, Empyrion's shape on shared/Items: a toolbar of nine slots (1 to 9,
/// the selected one in your hands) and a bag of 8 x 6 slots, all over one storage that counts
/// mass and volume. It is the player's own and goes everywhere with them; a structure's shared
/// storage is a different inventory and the two never merge.
///
/// Beside your own toolbar there is the remote toolbar: nine references to items left in the
/// storage of cores you may use. Y switches between the two. A remote slot is used from its origin's
/// storage, only while you are inside that core's area; outside it the slot greys out and can't be
/// used (a remote gun in hand stays there, unable to fire). Remote items never leave their container
/// until they are used, so a death never touches them. The same item from two cores takes two slots.
/// </summary>
public sealed class PlayerInventory
{
    public const int ToolbarSlots = 9, Columns = 10, Rows = 7;

    /// <summary>The bag's room, storage units (a big suit pack: the grid's slots are usually the limit).</summary>
    public const double Capacity = 1500;

    /// <summary>Toolbar shortcut for the starter vessel's blueprint (the blueprint projector).</summary>
    public const string BlueprintBlock = "@blueprint";

    /// <summary>The hand tools, plainest first: each is the survival tool's gather, shoot and deconstruct, faster.</summary>
    public static readonly string[] ToolItems = { "survival-tool", "mining-drill", "plasma-drill" };

    /// <summary>The two cores' items: the Big Core runs a base or a capital vessel, the Small Core a small or hover vessel.</summary>
    public const string BigCore = "big-core", SmallCore = "small-core";
    public static readonly string[] CoreItems = { BigCore, SmallCore };

    public readonly SharedStorage Storage;
    public readonly ItemGrid Grid;
    readonly ItemCatalogue _items;

    /// <summary>The toolbar slot in your hands (0 to 8).</summary>
    public int Selected;

    /// <summary>Which toolbar is in use.</summary>
    public BarSource Bar = BarSource.Personal;

    /// <summary>The structure storage you are linked to (you are in its area), or null; set by the game every tick.</summary>
    public SharedStorage Linked { get; private set; }
    /// <summary>The linked structure's name.</summary>
    public string LinkedName { get; private set; } = "";

    /// <summary>The remote toolbar's nine slots (null: empty).</summary>
    public readonly RemoteSlot[] Remote = new RemoteSlot[ToolbarSlots];

    /// <summary>Is a core's storage within reach (you are inside its area)? Set by the game every tick; reachable by default (tests).</summary>
    public Func<SharedStorage, bool> InRange = _ => true;

    /// <summary>An item is being dragged out of a storage: the toolbar shows the remote bar so it can be dropped there.</summary>
    public bool Dragging;

    readonly Slot _virtual = new();
    readonly Dictionary<SharedStorage, int> _colours = new();

    public PlayerInventory(ItemCatalogue items)
    {
        _items = items;
        Storage = new SharedStorage(items, "Pack", Capacity);
        Grid = new ItemGrid(Storage, Columns, Rows, ToolbarSlots);
    }

    /// <summary>The survival tool, which is never dropped, wherever it sits.</summary>
    public const string SurvivalTool = "survival-tool";

    /// <summary>
    /// What a death leaves on the body: the bag goes, the toolbar and the survival tool stay with you
    /// (design: the survival tool is never dropped; the owner: nor is anything on the toolbar).
    /// Takes the rest out of the pack and returns it, for the backpack.
    /// </summary>
    public Bill TakeBagForDeath()
    {
        Grid.Reconcile();
        var keep = new Bill();
        for (int i = 0; i < Grid.Toolbar; i++)
            if (Grid[i].Item != null) keep.Add(Grid[i].Item, Grid[i].Count);
        var drop = new Bill();
        foreach (var (id, count) in Storage.Contents)
        {
            int kept = id == SurvivalTool ? count : Math.Min(count, keep[id]);
            if (count > kept) drop.Add(id, count - kept);
        }
        Storage.TryTake(drop);
        Grid.Reconcile(); // takes the bag's stacks first, so a stack on the toolbar stays whole
        return drop;
    }

    public int this[string id] => Storage.Count(id);

    /// <summary>Links to a structure's storage (null: none in range). Building and the menu read it.</summary>
    public void Link(SharedStorage storage, string name)
    {
        Linked = storage;
        LinkedName = name ?? "";
    }

    // ---- The remote toolbar ----------------------------------------------------------------------

    /// <summary>The remote toolbar is in use.</summary>
    public bool OnRemoteBar => Bar == BarSource.Remote;

    /// <summary>The toolbar to draw: the remote one while it is in use or an item is being dragged toward it.</summary>
    public bool ShowRemote => OnRemoteBar || Dragging;

    public bool RemoteEmpty => Remote.All(s => s == null);
    public int RemoteCount() => Remote.Count(s => s != null);

    /// <summary>The colour index of a core (stable for the game: the order cores were first used), for the slot's pip.</summary>
    public int ColourOf(SharedStorage source)
    {
        if (!_colours.TryGetValue(source, out int c)) _colours[source] = c = _colours.Count;
        return c;
    }

    /// <summary>Puts a reference on a slot (a drag from a core's storage). The same item of the same core never takes two slots.</summary>
    public bool SetRemote(int slot, SharedStorage source, string origin, string itemId)
    {
        if (slot < 0 || slot >= ToolbarSlots || source == null || itemId == null) return false;
        for (int i = 0; i < ToolbarSlots; i++)
            if (i != slot && Remote[i] is RemoteSlot r && r.Source == source && r.ItemId == itemId) Remote[i] = null;
        Remote[slot] = new RemoteSlot { Source = source, Origin = origin ?? "", ItemId = itemId };
        ColourOf(source);
        return true;
    }

    /// <summary>Puts a reference on the first free slot (or the one already holding it): the slot used, or -1 if all nine are taken.</summary>
    public int AddRemote(SharedStorage source, string origin, string itemId)
    {
        for (int i = 0; i < ToolbarSlots; i++)
            if (Remote[i] is RemoteSlot r && r.Source == source && r.ItemId == itemId) return i;
        int free = Array.FindIndex(Remote, s => s == null);
        return free >= 0 && SetRemote(free, source, origin, itemId) ? free : -1;
    }

    public void ClearRemote(int slot)
    {
        if (slot >= 0 && slot < ToolbarSlots) Remote[slot] = null;
    }

    /// <summary>Is a remote slot usable now: set, and its origin in range?</summary>
    public bool RemoteReady(int slot) => Remote[slot] is RemoteSlot r && InRange(r.Source);

    /// <summary>The remote slot in your hands, or null.</summary>
    public RemoteSlot HandRemote => OnRemoteBar ? Remote[Selected] : null;

    /// <summary>The hand can be used: your own toolbar always; a remote slot only in range of its origin.</summary>
    public bool HandUsable => !OnRemoteBar || (HandRemote != null && InRange(HandRemote.Source));

    /// <summary>
    /// Switches toolbar. Returns a short message when it can't: the remote bar is empty (and can't be
    /// opened), or it just closed because its last item went.
    /// </summary>
    public string ToggleBar()
    {
        if (Bar == BarSource.Remote)
        {
            Bar = BarSource.Personal;
            return null;
        }
        if (RemoteEmpty) return "The remote toolbar is empty: drag an item from a core's storage tab onto it";
        Bar = BarSource.Remote;
        return null;
    }

    /// <summary>Each tick: the remote bar closes itself when its last item goes. Returns true when it just did.</summary>
    public bool Tick()
    {
        if (Bar != BarSource.Remote || !RemoteEmpty || Dragging) return false;
        Bar = BarSource.Personal;
        return true;
    }

    /// <summary>
    /// The slot in your hands: on your own toolbar a slot of your grid; on the remote toolbar a view
    /// of the origin's storage (its item, and how many it holds right now).
    /// </summary>
    public Slot InHand
    {
        get
        {
            if (Bar == BarSource.Personal) return Grid[Selected];
            RemoteSlot r = Remote[Selected];
            _virtual.Block = null;
            _virtual.Item = r?.ItemId;
            _virtual.Count = r == null ? 0 : r.Count;
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

    /// <summary>The source a use of the hand takes from: your bag, or the remote slot's origin storage.</summary>
    public SharedStorage HandSource => OnRemoteBar && HandRemote != null ? HandRemote.Source : Storage;

    /// <summary>
    /// Ammo for a gun: from the player first, then the gun's origin, then the nearest container in
    /// range (<paramref name="nearby"/>, nearest first). Takes up to <paramref name="n"/>; returns how many it got.
    /// </summary>
    public int TakeAmmo(string ammoId, int n, IEnumerable<SharedStorage> nearby)
    {
        int got = Storage.Remove(ammoId, n);
        if (got > 0) Grid.Reconcile();
        if (got < n && OnRemoteBar && HandRemote is RemoteSlot r && InRange(r.Source)) got += r.Source.Remove(ammoId, n - got);
        if (got < n && nearby != null)
            foreach (SharedStorage s in nearby)
            {
                if (got >= n) break;
                if (s == Storage || !InRange(s)) continue;
                got += s.Remove(ammoId, n - got);
            }
        return got;
    }

    public HandKind Hand
    {
        get
        {
            Grid.Reconcile();
            Slot s = InHand;
            if (s.Block == BlueprintBlock) return HandKind.Blueprint;
            string id = HandItem;
            if (id == null || !_items.TryGet(id, out ItemDef item)) return HandKind.Empty;
            if (ToolItems.Contains(id)) return HandKind.Tool;
            if (item.Category == ItemCategory.Weapon) return HandKind.Weapon;
            if (id == "hover-bike") return HandKind.Vehicle;
            if (id == "scanner") return HandKind.Scanner;
            if (CoreItems.Contains(id)) return HandKind.Core;
            if (item.IsBlock) return HandKind.Block;
            if (id == Slice.Vessels.Device.ClimateItem || _items.Machines.All.Any(m => m.ItemId == id)) return HandKind.Device;
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
