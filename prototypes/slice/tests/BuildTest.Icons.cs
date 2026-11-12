using System;
using System.Collections.Generic;
using System.Linq;
using GalHack.Items;
using Godot;
using Slice.Items;
using Slice.Player;
using Slice.Ui;
using Slice.Vehicles;

namespace Slice.Tests;

/// <summary>
/// Inventory icons come from an item's own model: every tool and weapon (and the hover bike, and the
/// devices set down in the world) is drawn from the model the game shows in the hand, on the road or
/// on the ground, built by the same code, never from the stand-in by kind. A new item with a model
/// gets its icon with no icon code.
/// </summary>
public partial class BuildTest
{
    /// <summary>Tools and weapons are held items. (Matched by name so the check holds whichever branch adds the weapon category.)</summary>
    static bool IsHeldCategory(ItemDef item) => item.Category.ToString() is "Tool" or "Weapon";

    static string Sig(ItemModel m)
    {
        string s = ItemModels.Signature(m.Root);
        m.Root.Free();
        return s;
    }

    /// <summary>The icon's source for every tool and weapon, and the models themselves (no game needed).</summary>
    void IconSourceChecks()
    {
        GD.Print("-- inventory icons are the item's own model --");
        var icons = new ItemIcons();
        icons.Setup(Items, Blocks);
        List<ItemDef> held = Items.Items.Where(IsHeldCategory).ToList();
        Check(held.Count >= 6, $"the catalogue has {held.Count} tools and weapons ({string.Join(", ", held.Select(i => i.Id))})");
        var stand = held.Where(i => icons.SourceOf(i) is not (ItemIcons.Source.Held or ItemIcons.Source.Placed or ItemIcons.Source.Vehicle)).Select(i => i.Id).ToList();
        Check(stand.Count == 0, $"every tool and weapon's icon comes from its own model, none from the stand-in{(stand.Count == 0 ? "" : ": " + string.Join(", ", stand))}");
        foreach (ItemDef item in held)
        {
            ItemModel m = ItemModels.Build(item);
            int meshes = m == null ? 0 : ItemModels.Signature(m.Root).Split('\n', StringSplitOptions.RemoveEmptyEntries).Length;
            Check(m != null && meshes >= 4, $"{item.Id}: a model of {meshes} parts, {(m == null ? "none" : m.Source.ToString().ToLowerInvariant())}");
            m?.Root.Free();
        }
        foreach (string id in PlayerInventory.ToolItems.Append("scanner"))
            Check(icons.SourceOf(Items.Item(id)) == ItemIcons.Source.Held, $"{id}'s icon is its held model");
        ItemDef bike = Items.Item("hover-bike");
        Check(icons.SourceOf(bike) == ItemIcons.Source.Vehicle, "the hover bike's icon is the vehicle's own model");
        string bikeIcon = Sig(ItemModels.Build(bike)), bikeWorld = ItemModels.Signature(BikeModel.Build());
        Check(bikeIcon == bikeWorld && bikeIcon.Length > 0, "and that is the very model the bike in the world is built from");

        // The three hand tools are one model in three liveries, told apart by colour.
        static Color Hull(ItemModel m) => (Color)((m.Root.GetChild<MeshInstance3D>(0).MaterialOverride as ShaderMaterial).GetShaderParameter("base_color"));
        ItemModel plain = ItemModels.Build(Items.Item("survival-tool")), drill = ItemModels.Build(Items.Item("mining-drill")), plasma = ItemModels.Build(Items.Item("plasma-drill"));
        Check(ItemModels.Signature(plain.Root) == ItemModels.Signature(drill.Root) && ItemModels.Signature(drill.Root) == ItemModels.Signature(plasma.Root),
            "the survival tool, the mining drill and the plasma drill are one model (the one in the hands)");
        Check(Hull(plain) != Hull(drill) && Hull(drill) != Hull(plasma) && Hull(plain) != Hull(plasma), "in three liveries, so their icons can be told apart");
        plain.Root.Free();
        drill.Root.Free();
        plasma.Root.Free();

        // A new item with a model (a generated gun, say) is registered once, and its icon follows with no icon code.
        ItemModels.Register(i => i.Id == "check-gun", i => HeldTool.Build(i), ModelSource.Held);
        var gun = new ItemDef { Id = "check-gun", Name = "Check gun", Category = ItemCategory.Tool, Rarity = Rarity.Rare };
        Check(icons.SourceOf(gun) == ItemIcons.Source.Held && ItemModels.IsHeld(gun), "a newly registered item with a held model gets its icon from it with no icon code");

        // What is left on the stand-in, by kind (only things without a model of their own).
        var rest = Items.Items.Where(i => !i.IsBlock && icons.SourceOf(i) == ItemIcons.Source.StandIn).ToList();
        GD.Print($"  (still on the stand-in: {rest.Count} items without a model of their own: {string.Join(", ", rest.GroupBy(i => i.Category).Select(g => $"{g.Key} x{g.Count()}"))})");
        GD.Print($"  (stand-in items: {string.Join(", ", rest.Select(i => i.Id))})");
        icons.Free();
    }

    /// <summary>In the game: the glove holds the same model the icon is drawn from, for each hand tool and the scanner; a block shows the plain tool.</summary>
    IEnumerable<int> HeldModelChecks()
    {
        GD.Print("-- the hands hold the model the icon is made from --");
        Pack.Add("mining-drill", 1);
        Pack.Add("plasma-drill", 1);
        Pack.Add(Item("steel"), 5);
        foreach (int x in _play.ToToolbar("mining-drill", 7)) yield return x;
        foreach (int x in _play.ToToolbar("plasma-drill", 6)) yield return x;
        foreach (int x in _play.BlockToToolbar("steel", 5)) yield return x;
        foreach (int x in _play.CloseMenu()) yield return x;
        foreach (var (id, slot) in new[] { ("survival-tool", 0), ("mining-drill", 7), ("plasma-drill", 6), ("scanner", 8) })
        {
            foreach (int x in _play.Select(slot)) yield return x;
            foreach (int x in _drive.Wait(3)) yield return x;
            ItemModel shown = _game.Hands.Shown;
            ItemModel icon = ItemModels.Build(Items.Item(id));
            Check(Pack.HandItem == id && shown != null && shown.Root.Visible && ItemModels.Signature(shown.Root) == Sig(icon),
                $"{id} in hand: the glove holds the very model its icon is built from ({(shown == null ? "nothing shown" : ItemModels.Signature(shown.Root).Split('\n').Length + " parts")})");
        }
        foreach (int x in _play.Select(5)) yield return x;
        foreach (int x in _drive.Wait(3)) yield return x;
        ItemModel plain = HeldTool.Build(null);
        Check(Pack.Hand == HandKind.Block && _game.Hands.Shown != null && ItemModels.Signature(_game.Hands.Shown.Root) == Sig(plain), "a block in hand shows the plain tool, as it always did");
        foreach (int x in _play.Select(0)) yield return x;
        foreach (int x in _drive.Wait(2)) yield return x;
    }
}
