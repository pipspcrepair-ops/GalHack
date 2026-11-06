using System;
using System.Collections.Generic;
using System.Linq;
using GalHack.Items;

namespace Slice.Items;

/// <summary>The quick category toggles every inventory view and recipe list has.</summary>
[Flags]
public enum ItemGroup
{
    None = 0,
    Ores = 1,
    Materials = 2,
    Parts = 4,
    Blocks = 8,
    Devices = 16,
    Ammo = 32,
    Food = 64,
    Deco = 128,
    Other = 256,
}

/// <summary>How a view orders what it shows. <see cref="Slot"/> keeps the player's own arrangement.</summary>
public enum ItemSort { Slot, Name, Category, Quantity, Mass, Rarity, Recent }

/// <summary>
/// One view's filter and sort (the bag, a core's storage, a container, a recipe list): a live,
/// case-insensitive text filter on item names and category names, category toggles, and an order.
/// Filtering and sorting only choose what is shown and where; they never change what is stored.
/// The visible list is cached on the grid's version, so a big storage redraws without work.
/// </summary>
public sealed class ItemView
{
    public static readonly ItemGroup[] Groups =
        { ItemGroup.Ores, ItemGroup.Materials, ItemGroup.Parts, ItemGroup.Blocks, ItemGroup.Devices, ItemGroup.Ammo, ItemGroup.Food, ItemGroup.Deco, ItemGroup.Other };

    public static readonly ItemSort[] Sorts = Enum.GetValues<ItemSort>();

    string _text = "";
    ItemGroup _groups;
    ItemSort _sort;
    int _changes;

    /// <summary>The text filter, as typed.</summary>
    public string Text { get => _text; set { _text = value ?? ""; _changes++; } }
    /// <summary>Categories shown (None: all).</summary>
    public ItemGroup Show { get => _groups; set { _groups = value; _changes++; } }
    public ItemSort Sort { get => _sort; set { _sort = value; _changes++; } }

    /// <summary>A filter is narrowing what is shown.</summary>
    public bool Filtering => _text.Trim().Length > 0 || _groups != ItemGroup.None;

    public void Toggle(ItemGroup g) => Show ^= g;
    public void NextSort() => Sort = Sorts[(Array.IndexOf(Sorts, _sort) + 1) % Sorts.Length];
    public void Clear() { _text = ""; _groups = ItemGroup.None; _changes++; }

    public static string GroupName(ItemGroup g) => g switch
    {
        ItemGroup.Ores => "ORES",
        ItemGroup.Materials => "MATERIALS",
        ItemGroup.Parts => "PARTS",
        ItemGroup.Blocks => "BLOCKS",
        ItemGroup.Devices => "DEVICES",
        ItemGroup.Ammo => "AMMO",
        ItemGroup.Food => "FOOD & MED",
        ItemGroup.Deco => "DECO",
        _ => "OTHER",
    };

    public static string SortName(ItemSort s) => s switch
    {
        ItemSort.Slot => "AS ARRANGED",
        ItemSort.Name => "NAME",
        ItemSort.Category => "CATEGORY",
        ItemSort.Quantity => "QUANTITY",
        ItemSort.Mass => "MASS",
        ItemSort.Rarity => "RARITY",
        _ => "RECENT",
    };

    /// <summary>Which quick toggle an item falls under.</summary>
    public static ItemGroup GroupOf(ItemDef item) => item.Category switch
    {
        ItemCategory.Raw => ItemGroup.Ores,
        ItemCategory.Refined => ItemGroup.Materials,
        ItemCategory.Part or ItemCategory.Component when !IsCore(item) => ItemGroup.Parts,
        ItemCategory.Ammo => ItemGroup.Ammo,
        ItemCategory.Consumable => ItemGroup.Food,
        ItemCategory.Tool => ItemGroup.Devices,
        ItemCategory.Block => item.Family switch
        {
            BlockFamily.Armour => ItemGroup.Blocks,
            BlockFamily.Deco => ItemGroup.Deco,
            _ => ItemGroup.Devices,
        },
        ItemCategory.Component => ItemGroup.Devices, // the hover and capital cores
        _ => ItemGroup.Other,
    };

    static bool IsCore(ItemDef item) => item.Id is "base-core" or "vessel-core" or "hover-core" or "capital-core";

    /// <summary>The category words the text filter also matches: "raw ore", "block armour", "deco office".</summary>
    public static string CategoryWords(ItemDef item) =>
        $"{item.Category} {GroupName(GroupOf(item))} {(item.IsBlock ? item.Family.ToString() : "")} {item.Group ?? ""} {Rarities.Name(item.Rarity)}".ToLowerInvariant();

    /// <summary>Does an item pass the text filter and the category toggles?</summary>
    public bool Matches(ItemDef item)
    {
        if (_groups != ItemGroup.None && (_groups & GroupOf(item)) == 0) return false;
        string t = _text.Trim();
        if (t.Length == 0) return true;
        return item.Name.Contains(t, StringComparison.OrdinalIgnoreCase)
               || (item.Short?.Contains(t, StringComparison.OrdinalIgnoreCase) ?? false)
               || CategoryWords(item).Contains(t, StringComparison.OrdinalIgnoreCase);
    }

    // ---- Grids ---------------------------------------------------------------------------------

    (ItemGrid grid, int version, int changes, int first, int count) _key;
    List<int> _visible = new();

    /// <summary>
    /// The slot indices to show, in order, from slot <paramref name="first"/> on (<paramref name="count"/>
    /// of them, or all). Unfiltered and as arranged, every slot shows in place, empty ones too;
    /// otherwise only the matching stacks, in the chosen order.
    /// </summary>
    public IReadOnlyList<int> Visible(ItemGrid grid, int first = 0, int count = int.MaxValue)
    {
        grid.Reconcile();
        var key = (grid, grid.Version, _changes, first, count);
        if (key == _key) return _visible;
        int end = (int)Math.Min(grid.Count, (long)first + count);
        var list = new List<int>(Math.Max(0, end - first));
        if (!Filtering && _sort == ItemSort.Slot)
        {
            for (int i = first; i < end; i++) list.Add(i);
        }
        else
        {
            ItemCatalogue items = grid.Storage.Catalogue;
            for (int i = first; i < end; i++)
            {
                Slot s = grid[i];
                if (s.Item == null) continue;
                if (Matches(items.Item(s.Item))) list.Add(i);
            }
            if (_sort != ItemSort.Slot)
            {
                var keyed = list.Select(i => (i, item: items.Item(grid[i].Item), n: grid[i].Count)).ToList();
                list = Ordered(keyed, k => k.item, k => k.n, k => grid.AddedAt(k.item.Id)).Select(k => k.i).ToList();
            }
        }
        _visible = list;
        _key = key;
        return _visible;
    }

    /// <summary>Items (a recipe list), filtered and ordered; <paramref name="count"/> gives a quantity for the quantity sort.</summary>
    public List<ItemDef> Apply(IEnumerable<ItemDef> items, Func<ItemDef, int> count = null)
    {
        var list = items.Where(Matches).ToList();
        if (_sort == ItemSort.Slot) return list;
        return Ordered(list, i => i, i => count?.Invoke(i) ?? 0, _ => 0).ToList();
    }

    IEnumerable<T> Ordered<T>(IEnumerable<T> list, Func<T, ItemDef> item, Func<T, int> n, Func<T, long> added) => _sort switch
    {
        ItemSort.Name => list.OrderBy(x => item(x).Name, StringComparer.OrdinalIgnoreCase),
        ItemSort.Category => list.OrderBy(x => item(x).Category).ThenBy(x => item(x).Family).ThenBy(x => item(x).Name, StringComparer.OrdinalIgnoreCase),
        ItemSort.Quantity => list.OrderByDescending(n).ThenBy(x => item(x).Name, StringComparer.OrdinalIgnoreCase),
        ItemSort.Mass => list.OrderByDescending(x => item(x).MassKg * Math.Max(1, n(x))).ThenBy(x => item(x).Name, StringComparer.OrdinalIgnoreCase),
        ItemSort.Rarity => list.OrderByDescending(x => item(x).Rarity).ThenBy(x => item(x).Name, StringComparer.OrdinalIgnoreCase),
        ItemSort.Recent => list.OrderByDescending(added).ThenBy(x => item(x).Name, StringComparer.OrdinalIgnoreCase),
        _ => list,
    };
}

/// <summary>Every view's filter and sort, by the view's key, so each keeps its own choice for the session.</summary>
public static class ItemViews
{
    static readonly Dictionary<string, ItemView> _views = new(StringComparer.Ordinal);

    public static ItemView Get(string key)
    {
        if (!_views.TryGetValue(key, out ItemView v)) _views[key] = v = new ItemView();
        return v;
    }

    /// <summary>The view whose text box has the keyboard, or null.</summary>
    public static string Focused;

    /// <summary>Typing into the focused box: printable text, and backspaces.</summary>
    public static bool Type(string text, int backspaces)
    {
        if (Focused == null) return false;
        ItemView v = Get(Focused);
        string t = v.Text;
        for (int i = 0; i < backspaces && t.Length > 0; i++) t = t[..^1];
        foreach (char c in text ?? "") if (!char.IsControl(c) && t.Length < 32) t += c;
        if (t != v.Text) v.Text = t;
        return true;
    }

    /// <summary>Escape on a focused box: clears its text first, then lets go of the keyboard. False if no box had it (the menu closes).</summary>
    public static bool Escape()
    {
        if (Focused == null) return false;
        ItemView v = Get(Focused);
        if (v.Text.Length > 0) v.Text = "";
        else Focused = null;
        return true;
    }

    /// <summary>Forgets every view (a new game).</summary>
    public static void Reset()
    {
        _views.Clear();
        Focused = null;
    }
}
