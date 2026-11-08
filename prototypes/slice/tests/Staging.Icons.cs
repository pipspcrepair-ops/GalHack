using System.Collections.Generic;
using System.IO;
using System.Linq;
using GalHack.Items;
using Godot;
using Slice.Items;
using Slice.Ui;
using WeatherKind = Weather.Model.WeatherKind;

namespace Slice.Tests;

/// <summary>
/// Screenshot views for the inventory icons: the bag and toolbar with every tool in them
/// (--view=icons-bag), and a sheet of every item that has a model of its own, each icon beside its
/// source and the raw icons saved a PNG apiece next to it (--view=icons-sheet --out=DIR/sheet.png).
/// </summary>
public sealed partial class Staging
{
    /// <summary>Every tool and what else has a model of its own, two of each: one on the toolbar by the real drag, one left in the bag.</summary>
    IEnumerable<int> IconBagView()
    {
        _game.Zone.Sky.TimeOfDay = Num("time", 0.33);
        _game.Zone.Weather.Sim.Snap(WeatherKind.Clear);
        string[] own = { "scanner", "mining-drill", "plasma-drill", "hover-bike", "survival-constructor", "climate-node" };
        foreach (string id in own) Hand((id, 2));
        Hand(("survival-tool", 1), ("iron-ore", 160), ("copper-ore", 80), ("carbon-ore", 70), ("silicon-ore", 30), ("biomass", 14), ("stone", 22), ("ice", 12), ("meat", 6), ("hide", 2),
            ("trail-ration", 4), ("patch-kit", 3), ("air-cell", 2), ("iron-bar", 30), ("copper-wire", 20), ("circuit-board", 4));
        for (int i = 0; i < own.Length; i++)
            foreach (int x in Play.ToToolbar(own[i], i + 1)) yield return x;
        foreach (int x in Play.OpenMenu(MenuTab.You)) yield return x;
        // Icons render a few a frame: wait for every one the screen shows.
        foreach (int x in _drive.Wait(150)) yield return x;
        _game.Pack.Selected = 0;
        _game.Controls.Mouse = new Vector2(1300f, 780f); // off every slot, so no tooltip
        foreach (int x in _drive.Wait(30)) yield return x;
    }

    /// <summary>
    /// The glove holding each held item in turn (--out=DIR/hands.png is the last; the others are
    /// DIR/hands-ID.png): the model in the hand is the one the icon is drawn from.
    /// </summary>
    IEnumerable<int> IconHandsView()
    {
        _game.Zone.Sky.TimeOfDay = Num("time", 0.36);
        _game.Zone.Weather.Sim.Snap(WeatherKind.Clear);
        string[] own = { "scanner", "mining-drill", "plasma-drill" };
        foreach (string id in own) Hand((id, 1));
        for (int i = 0; i < own.Length; i++)
            foreach (int x in Play.ToToolbar(own[i], i + 1)) yield return x;
        foreach (int x in Play.CloseMenu()) yield return x;
        string dir = Path.GetDirectoryName(Path.GetFullPath(_args.GetValueOrDefault("out", "hands.png"))) ?? ".";
        var order = new[] { ("survival-tool", 0), ("scanner", 1), ("mining-drill", 2), ("plasma-drill", 3) };
        foreach (var (id, slot) in order)
        {
            foreach (int x in Play.Select(slot)) yield return x;
            foreach (int x in _drive.Wait(20)) yield return x;
            if (id != order[^1].Item1)
                foreach (int x in Shot(Path.Combine(dir, $"hands-{id}.png"))) yield return x;
        }
    }

    /// <summary>A sheet of every item with a model of its own, at twice the icon's size, and the raw icons beside it.</summary>
    IEnumerable<int> IconSheetView()
    {
        _game.Zone.Weather.Sim.Snap(WeatherKind.Clear);
        ItemCatalogue items = _game.Zone.Data.Items;
        ItemIcons icons = _game.Icons;
        List<ItemDef> list = items.Items.Where(i => ItemModels.Has(i, out _)).OrderBy(i => icons.SourceOf(i)).ThenBy(i => i.Rarity).ThenBy(i => i.Id).ToList();
        foreach (ItemDef item in list) icons.Get(item);
        for (int i = 0; i < 900 && list.Any(item => icons.Rendered(item.Id) == null && !icons.Failed(item.Id)); i++)
        {
            foreach (ItemDef item in list) icons.Get(item);
            yield return 0;
        }
        string dir = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(_args.GetValueOrDefault("out", "sheet.png"))) ?? ".", "raw");
        Directory.CreateDirectory(dir);
        var layer = new CanvasLayer { Layer = 200 };
        var back = new ColorRect { Color = new Color("1b2230") };
        back.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        layer.AddChild(back);
        var grid = new GridContainer { Columns = 4 };
        grid.AddThemeConstantOverride("h_separation", 28);
        grid.AddThemeConstantOverride("v_separation", 22);
        grid.Position = new Vector2(40, 30);
        layer.AddChild(grid);
        foreach (ItemDef item in list)
        {
            Texture2D tex = icons.Rendered(item.Id);
            var cell = new VBoxContainer();
            var frame = new PanelContainer();
            var tile = new ColorRect { Color = new Color("2b3548"), CustomMinimumSize = new Vector2(ItemIcons.Size * 2 + 12, ItemIcons.Size * 2 + 12) };
            frame.AddChild(tile);
            if (tex != null)
            {
                tile.AddChild(new TextureRect { Texture = tex, Position = new Vector2(6, 6), Size = new Vector2(ItemIcons.Size * 2, ItemIcons.Size * 2), StretchMode = TextureRect.StretchModeEnum.Scale, TextureFilter = CanvasItem.TextureFilterEnum.Linear });
                tex.GetImage().SavePng(Path.Combine(dir, item.Id + ".png"));
            }
            cell.AddChild(frame);
            cell.AddChild(new Label { Text = $"{item.Name}  ({icons.SourceOf(item)}{(tex == null ? ", NO ICON" : "")})", CustomMinimumSize = new Vector2(ItemIcons.Size * 2 + 12, 0) });
            grid.AddChild(cell);
        }
        _game.AddChild(layer);
        GD.Print($"icon sheet: {list.Count} items with a model of their own; raw icons in {dir}");
        foreach (int x in _drive.Wait(20)) yield return x;
    }
}
