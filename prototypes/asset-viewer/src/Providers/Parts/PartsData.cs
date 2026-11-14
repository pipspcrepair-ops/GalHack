using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using GalHack.Items;
using GalHack.Structures;
using Godot;
using Slice.Game;
using Slice.Items;
using Slice.Places;

namespace AssetViewer.Providers.Parts;

/// <summary>
/// The game's data as the slice loads it (block catalogue with the vessel parts and precursor armour,
/// the item catalogue with the slice's overlay, the deco library), read where it lives. Loaded once.
/// Nothing here is a list of content: providers walk these catalogues.
/// </summary>
public static class PartsData
{
    static Catalogue _blocks;
    static ItemCatalogue _items;
    static Dictionary<string, ConsumableEffect> _consumables;

    static string Protos => Path.GetFullPath(Path.Combine(ProjectSettings.GlobalizePath("res://"), ".."));
    public static string Proto(params string[] parts) => Path.Combine(Protos, Path.Combine(parts));
    public static string Shared(params string[] parts) => GameData.SharedPath(parts);
    public static string Repo => Path.GetFullPath(Path.Combine(Protos, ".."));

    /// <summary>Building's catalogue with the extra parts and materials the slice adds.</summary>
    public static Catalogue Blocks => _blocks ??= GalHack.Items.Bridge.BuildingBridge.LoadCatalogue(Proto("building", "data"),
        new[] { Proto("physics-sandbox", "data", "vessel-parts.json"), Shared("Items", "data", "building-parts.json") },
        new[] { Shared("Items", "data", "building-materials.json") });

    /// <summary>The item catalogue as the slice plays it, with deco bills and block items named.</summary>
    public static ItemCatalogue Items
    {
        get
        {
            if (_items != null) return _items;
            _items = SliceItems.Load(Shared("Items", "data"), Proto("slice", "data", "items.json"), out _consumables);
            var deco = DecoCatalogue.LoadDirectory(Shared("Items", "data"));
            deco.AddBillsTo(_items);
            deco.AddUniversalBlockTo(_items);
            GalHack.Items.Bridge.BuildingBridge.NameBlockItems(_items, Blocks);
            return _items;
        }
    }

    /// <summary>What using each consumable does (the slice's overlay).</summary>
    public static Dictionary<string, ConsumableEffect> Consumables { get { _ = Items; return _consumables; } }

    public static Decor Deco => Decor.Shared;

    /// <summary>A name made of an id: "steel-wall" becomes "Steel wall".</summary>
    public static string Nice(string id)
    {
        if (string.IsNullOrEmpty(id)) return id ?? "";
        string s = id.Replace('-', ' ').Replace('_', ' ');
        return char.ToUpperInvariant(s[0]) + s[1..];
    }

    /// <summary>A short id-safe form of a name for option values and ids.</summary>
    public static string Slug(string s) => new string(s.ToLowerInvariant().Select(c => char.IsLetterOrDigit(c) ? c : '-').ToArray()).Trim('-');
}
