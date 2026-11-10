using System;
using System.Collections.Generic;
using System.Linq;
using GalHack.Arms;
using GalHack.Deco;
using GalHack.Deco.Engine;
using GalHack.Items;
using GalHack.Items.Deposits;
using Godot;
using Slice.Items;
using Slice.Places;

namespace AssetViewer.Providers.Parts;

/// <summary>
/// Every item in the game's catalogue (what sits in a bag or a storage): tools, weapons with their
/// generated gun models and muzzle flash, recoil and charge animations, ammo, ores and resources,
/// parts, consumables, and the block items. Each is shown as the model the game gives it (the hands'
/// model, the vehicle, the placed device, the deco piece, the block) or, where the game has only an
/// inventory icon, as the stand-in model its icon is rendered from, with the icon itself and the recipe's
/// icons as views. Ores also get their surface tells and underground bodies.
/// </summary>
public sealed partial class ItemsProvider : IAssetProvider
{
    public string Category => "Items";

    public const string ViewModel = "Model", ViewIcon = "Icon", ViewBoth = "Model and icon", ViewRecipe = "Recipe icons";

    public IEnumerable<AssetEntry> Entries()
    {
        ItemCatalogue items = PartsData.Items;
        foreach (ItemDef item in items.Items)
            yield return Entry(items, item);
        foreach (AssetEntry e in Deposits.Entries(items)) yield return e;
    }

    static string GroupOf(ItemDef i) => i.Category switch
    {
        ItemCategory.Raw => "Resources/Raw (mined, dug, harvested)",
        ItemCategory.Unique => "Resources/Unique finds",
        ItemCategory.Refined => "Materials",
        ItemCategory.Part => "Parts",
        ItemCategory.Component => "Components",
        ItemCategory.Consumable => "Consumables",
        ItemCategory.Ammo => "Ammo",
        ItemCategory.Tool => "Tools",
        ItemCategory.Weapon => "Weapons",
        _ => i.Family switch
        {
            BlockFamily.Armour => "Block items/Armour",
            BlockFamily.Deco => "Block items/Deco" + (string.IsNullOrEmpty(i.Group) ? "" : "/" + PartsData.Nice(i.Group)),
            _ => "Block items/Devices",
        },
    };

    static AssetEntry Entry(ItemCatalogue items, ItemDef item)
    {
        var views = new List<string> { ViewModel, ViewIcon, ViewBoth };
        if (item.Recipe != null) views.Add(ViewRecipe);
        var options = new List<AssetOption> { new AssetOption("View", views.ToArray()) };
        WeaponDef weapon = item.Category == ItemCategory.Weapon ? Slice.Arms.GunModel.Rows.OfItem(item.Id) : null;
        if (weapon != null && weapon.Modes.Count > 1)
            options.Add(new AssetOption("Mode", weapon.Modes.Select(m => m.Mode.Length > 0 ? m.Mode : m.Id).ToArray()) { Rollable = true });
        return new AssetEntry
        {
            Id = "item." + item.Id, Group = GroupOf(item), Name = item.Name,
            Options = options.ToArray(), Build = r => ItemAssets.Build(items, item, r),
        };
    }
}
