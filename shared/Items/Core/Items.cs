using System;
using System.Collections.Generic;

namespace GalHack.Items;

/// <summary>
/// The design's five-step rarity ladder. Each step lives further out, in more dangerous places;
/// unique finds can't be mined at all and come only from exploring.
/// </summary>
public enum Rarity { Common, Uncommon, Rare, Exotic, Unique }

public static class Rarities
{
    /// <summary>One colour per step in every inventory, map and loot pop-up: grey, green, blue, purple, gold (0xRRGGBB).</summary>
    public static uint Color(Rarity r) => r switch
    {
        Rarity.Common => 0x9aa3ad,
        Rarity.Uncommon => 0x3fcf5e,
        Rarity.Rare => 0x3d8bff,
        Rarity.Exotic => 0xa64dff,
        _ => 0xffbf2e,
    };

    public static string Name(Rarity r) => r.ToString().ToLowerInvariant();

    public static Rarity Parse(string s) =>
        Enum.TryParse(s, ignoreCase: true, out Rarity r) ? r : throw new FormatException($"unknown rarity '{s}'");

    public static Rarity Max(Rarity a, Rarity b) => a > b ? a : b;
}

/// <summary>What kind of thing an item is. Raw and unique items have no recipe; everything else has exactly one.</summary>
public enum ItemCategory { Raw, Unique, Refined, Part, Component, Consumable, Ammo, Tool }

/// <summary>Where a raw or unique item comes from. Crafted items are <see cref="Crafted"/>.</summary>
public enum ItemSource
{
    Crafted,
    /// <summary>An ore deposit.</summary>
    Mined,
    /// <summary>Any rock: digging terrain.</summary>
    Dug,
    /// <summary>Plants and creatures.</summary>
    Harvested,
    /// <summary>Points of interest, wrecks and derelicts only. Never mined.</summary>
    Found,
}

/// <summary>One kind of item. Mass and volume are per unit, the two numbers a player juggles when packing.</summary>
public sealed class ItemDef
{
    public string Id { get; init; }
    public string Name { get; init; }
    /// <summary>Seven letters or so, for tiles and tight rows.</summary>
    public string Short { get; init; }
    public ItemCategory Category { get; init; }
    public Rarity Rarity { get; init; }
    public ItemSource Source { get; init; }
    public double MassKg { get; init; }
    /// <summary>Storage units (litres) per unit.</summary>
    public double VolumeSu { get; init; }
    public string Where { get; init; } = "";
    public string Uses { get; init; } = "";
    /// <summary>The planet generator's deposit kind for this ore, if it places it today.</summary>
    public string DepositKind { get; init; }
    public int FuelUnits { get; init; }
    /// <summary>Ammo: rounds in one box, belt or rack.</summary>
    public int Rounds { get; init; }
    /// <summary>Ammo: the default auto-restock threshold, in boxes.</summary>
    public int RestockDefault { get; init; }
    /// <summary>The most units one inventory slot holds (data: <c>stack</c>, else a default per category).</summary>
    public int StackSize { get; init; } = 1;
    /// <summary>Its tech-tree node (data: <c>unlock</c>), or null if it is always available (materials and parts).</summary>
    public Unlock Unlock { get; init; }
    public int Index { get; internal set; }

    /// <summary>How it is made, or null for raw and unique items.</summary>
    public Recipe Recipe { get; internal set; }

    public bool IsRaw => Category == ItemCategory.Raw;
    public bool IsUnique => Category == ItemCategory.Unique;
    /// <summary>Gathered from the world (mined, dug or harvested), as opposed to found or crafted.</summary>
    public bool IsGathered => Source is ItemSource.Mined or ItemSource.Dug or ItemSource.Harvested;
    public bool IsAmmo => Category == ItemCategory.Ammo;
    public string ShortName => Short ?? Name;

    public override string ToString() => Id;
}

/// <summary>
/// A tech-tree node, carried by the item or block it unlocks (there is no separate tree file):
/// unlockable from character level <see cref="Level"/> for <see cref="Cost"/> points, once its
/// <see cref="Parent"/> node is unlocked, shown on tab <see cref="Tab"/>. A parent is another
/// node's id: an item id, or a block's <see cref="BlockBill.NodeId"/>.
/// </summary>
public sealed record Unlock(int Level, int Cost, string Parent, string Tab);

/// <summary>One recipe: a run turns <see cref="Inputs"/> into <see cref="Count"/> of the output.</summary>
public sealed class Recipe
{
    public ItemDef Output { get; init; }
    public int Count { get; init; } = 1;
    public IReadOnlyList<(ItemDef Item, int Count)> Inputs { get; init; }
    /// <summary>Seconds for one run on a speed-1 machine.</summary>
    public double Seconds { get; init; }
    /// <summary>The lowest constructor tier that knows it (0 survival .. 3 advanced).</summary>
    public int Tier { get; init; }

    /// <summary>Runs needed for at least <paramref name="units"/> of the output.</summary>
    public int RunsFor(int units) => units <= 0 ? 0 : (units + Count - 1) / Count;

    /// <summary>The inputs of <paramref name="runs"/> runs, as a bill.</summary>
    public Bill InputsFor(int runs)
    {
        var bill = new Bill();
        foreach (var (item, n) in Inputs) bill.Add(item.Id, n * runs);
        return bill;
    }

    public override string ToString() => $"{Count} {Output.Id} <- {string.Join(", ", Inputs.ConvertAll(i => $"{i.Count} {i.Item.Id}"))}";
}

static class ListExtensions
{
    public static List<TOut> ConvertAll<TIn, TOut>(this IReadOnlyList<TIn> list, Func<TIn, TOut> f)
    {
        var result = new List<TOut>(list.Count);
        foreach (TIn x in list) result.Add(f(x));
        return result;
    }
}
