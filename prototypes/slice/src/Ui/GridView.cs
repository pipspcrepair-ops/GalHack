using System;
using System.Collections.Generic;
using System.Linq;
using DiegeticUi.Theme;
using GalHack.Items;
using Godot;
using Slice.Items;
using Palette = DiegeticUi.Theme.Palette;

namespace Slice.Ui;

/// <summary>
/// Drawing and handling inventory slots, shared by every grid on the menu (the bag and toolbar,
/// a core's storage, an opened container, a body, a backpack): drag to move, Shift-click to send
/// the stack across, right-click to split, and a tooltip on every stack. With an
/// <see cref="ItemView"/> a grid shows only what its filter lets through, in its chosen order.
/// </summary>
public static class Slots
{
    static Palette C => UiTheme.Colors;

    /// <summary>
    /// One slot: drag it to move its stack (onto another slot, another grid, or out of the window
    /// to set it down), Shift-click to send the stack to <paramref name="other"/>, right-click to
    /// split it. Drops onto it move stacks in (and, on the toolbar, Creative block shortcuts).
    /// </summary>
    public static void Slot(Gui g, Game.Game game, string id, ItemGrid grid, int index, Rect2 r, ItemGrid other, bool selected = false)
    {
        grid.Reconcile();
        Slot s = grid[index];
        ItemCatalogue items = game.Zone.Data.Items;
        Color tone = C.Rule;
        string label = "", count = "";
        ItemDef shown = null;
        if (s.Block == PlayerInventory.BlueprintBlock) { tone = C.Holo; label = "SKIFF BP"; }
        else if (s.Block != null && items.TryGet(s.Block, out ItemDef shortcut)) { tone = C.Accent2; label = shortcut.ShortName; count = "FREE"; shown = shortcut; }
        else if (s.Item != null)
        {
            shown = items.Item(s.Item);
            tone = new Color(Rarities.Color(shown.Rarity) << 8 | 0xff);
            label = shown.ShortName;
            count = s.Count > 1 ? s.Count.ToString() : "";
        }
        if (g.Drawing)
        {
            g.Fill(r, new Color(C.Glass, 0.9f));
            g.Outline(r, selected ? C.Accent : new Color(C.Rule, 0.9f), selected ? 3 : 1.5f);
            if (!s.IsEmpty)
            {
                g.Fill(new Rect2(r.Position + new Vector2(3, 3), new Vector2(r.Size.X - 6, 6)), tone);
                g.TextIn(label, new Rect2(r.Position + new Vector2(2, 14), new Vector2(r.Size.X - 4, 22)), C.Text, 12, HorizontalAlignment.Center);
                g.TextIn(count, new Rect2(r.Position + new Vector2(2, r.Size.Y - 22), new Vector2(r.Size.X - 6, 20)), C.Accent2, 14, HorizontalAlignment.Right, UiTheme.Mono);
                if (shown?.IsBlock == true) g.TextIn(shown.SmallGrid ? "S" : "L", new Rect2(r.Position + new Vector2(4, r.Size.Y - 22), new Vector2(20, 20)), C.Dim, 11);
            }
        }
        if (shown != null)
        {
            int n = s.Block != null ? 0 : s.Count;
            g.SetTip(r, () => Tooltips.For(game, shown, n));
        }
        object payload = s.IsEmpty ? null : new SlotRef(grid, index);
        if (g.Draggable(id, r, payload, label, tone) && g.Shift)
        {
            int moved = ItemGrid.QuickMove(grid, index, other ?? grid);
            if (moved > 0) g.Sound = "drop";
        }
        if (g.RightClicked(r) && grid.Split(index)) g.Sound = "drop";
        if (g.DropTarget(id, r, out object dropped))
        {
            if (dropped is SlotRef from) ItemGrid.Move(from.Grid, from.Index, grid, index);
            else if (dropped is BlockRef block && game.Mode == GameMode.Creative && grid == game.Pack.Grid && index < grid.Toolbar) grid.SetBlock(index, block.BlockId);
        }
    }

    public static string Short(string name) => name.Length <= 9 ? name.ToUpperInvariant() : name[..9].ToUpperInvariant();

    /// <summary>
    /// A grid of slots in rows, from a slot index on, scrolled by whole rows. With a view, only
    /// what its filter shows, in its order (the slots keep their own ids, so drags still land).
    /// Returns how many slots the view shows in all (for paging).
    /// </summary>
    public static int Grid(Gui g, Game.Game game, string prefix, ItemGrid grid, int first, int count, int columns, Rect2 area, float cell, ItemGrid other, int scroll = 0, ItemView view = null)
    {
        int rows = Math.Max(1, (int)((area.Size.Y + 6) / (cell + 6)));
        IReadOnlyList<int> shown = view?.Visible(grid, first, count);
        int total = shown?.Count ?? Math.Min(count, grid.Count - first);
        for (int k = 0; k < columns * rows; k++)
        {
            int at = scroll * columns + k;
            if (at >= total) break;
            int index = shown != null ? shown[at] : first + at;
            if (index >= grid.Count) break;
            var r = new Rect2(area.Position + new Vector2(k % columns * (cell + 6), k / columns * (cell + 6)), new Vector2(cell, cell));
            Slot(g, game, $"{prefix}:{index}", grid, index, r, other);
        }
        return total;
    }
}

/// <summary>
/// The filter bar every grid and recipe list has: a live text box (click it, or press / on the
/// menu; Escape clears it, then closes the menu), quick category toggles and a sort button. On an
/// in-world screen, which has no keyboard, the text shows read-only.
/// </summary>
public static class FilterBar
{
    static Palette C => UiTheme.Colors;

    public const float Height = 62;

    static string Toggle(ItemGroup g) => g switch
    {
        ItemGroup.Ores => "ORE",
        ItemGroup.Materials => "MAT",
        ItemGroup.Parts => "PART",
        ItemGroup.Blocks => "BLK",
        ItemGroup.Devices => "DEV",
        ItemGroup.Ammo => "AMO",
        ItemGroup.Food => "FOOD",
        ItemGroup.Deco => "DECO",
        _ => "OTH",
    };

    /// <summary>Draws (and handles) one view's filter bar in a <see cref="Height"/>-tall strip. Returns the view.</summary>
    public static ItemView Build(Gui g, string key, Rect2 r, bool readOnly = false, bool sort = true)
    {
        ItemView view = ItemViews.Get(key);
        float sortW = sort ? 170 : 0;
        var box = new Rect2(r.Position, new Vector2(r.Size.X - sortW - (sort ? 8 : 0), 28));
        bool focused = ItemViews.Focused == key;
        if (g.TextBox($"{key}:filter", box, view.Text, readOnly ? "filter (Tab menu)" : "filter: type a name or category", focused, readOnly))
            ItemViews.Focused = key;
        if (sort && g.Button($"{key}:sort", new Rect2(box.End.X + 8, r.Position.Y, sortW, 28), $"SORT: {ItemView.SortName(view.Sort)}", C.Holo, size: 13))
            view.NextSort();
        var groups = ItemView.Groups;
        float w = (r.Size.X - 4 * groups.Length) / (groups.Length + 1);
        float y = r.Position.Y + 34;
        if (g.Button($"{key}:cat:all", new Rect2(r.Position.X, y, w, 24), "ALL", C.Accent2, lit: view.Show == ItemGroup.None, size: 11)) view.Show = ItemGroup.None;
        for (int i = 0; i < groups.Length; i++)
        {
            ItemGroup grp = groups[i];
            var b = new Rect2(r.Position.X + (i + 1) * (w + 4), y, w, 24);
            if (g.Button($"{key}:cat:{grp.ToString().ToLowerInvariant()}", b, Toggle(grp), C.Accent2, lit: (view.Show & grp) != 0, size: 11)) view.Toggle(grp);
        }
        return view;
    }

    /// <summary>"12 shown" when a filter narrows a take-all or store-all, so the player knows it acted on what they see.</summary>
    public static string Shown(ItemView view, int stacks) => view.Filtering ? $" ({stacks} shown)" : "";
}

/// <summary>
/// An item's tooltip, the same everywhere (grids, storages, recipe lists, loot, in-world screens):
/// its name in its rarity's colour and its category; mass and volume each and for the stack; how
/// it is made (inputs, constructor, time) or where it is found; its stack size, and for a block the
/// grid and classes of structure it goes on; whether the tech tree has it locked; what it is for.
/// </summary>
public static class Tooltips
{
    static Palette C => UiTheme.Colors;

    public static List<(string Text, Color Colour)> For(Game.Game game, ItemDef item, int count)
    {
        ItemCatalogue items = game.Zone.Data.Items;
        var lines = new List<(string, Color)>
        {
            (item.Name, new Color(Rarities.Color(item.Rarity) << 8 | 0xff)),
            ($"{Kind(item)} · {Rarities.Name(item.Rarity)}", C.Dim),
            ($"{item.MassKg:0.##} kg · {item.VolumeSu:0.##} SU each{(count > 1 ? $"   stack of {count}: {item.MassKg * count:0.#} kg · {item.VolumeSu * count:0.#} SU" : "")}", C.Text),
        };
        if (item.Recipe != null)
        {
            lines.Add(($"Made from {items.Describe(item.Recipe.InputsFor(1), 4)}{(item.Recipe.Count > 1 ? $" (makes {item.Recipe.Count})" : "")}", C.Text));
            lines.Add(($"{Maker(items, item.Recipe.Tier)} · {item.Recipe.Seconds:0.#} s a run", C.Text));
        }
        else lines.Add((Found(item), C.Text));
        string fits = item.IsBlock ? $" · {(item.SmallGrid ? "small" : "large")} grid, on {string.Join(", ", item.Places.Select(StructureClasses.Name))}" : "";
        lines.Add(($"Stacks to {item.StackSize}{fits}", C.Text));
        Progress p = game.Progress;
        if (item.Unlock != null)
        {
            bool open = p == null || p.ItemAllowed(item);
            lines.Add((open ? $"Unlocked (tech tree, level {item.Unlock.Level})" : $"Locked: level {item.Unlock.Level}, {item.Unlock.Cost} point{(item.Unlock.Cost == 1 ? "" : "s")} in the tech tree",
                open ? C.Good : C.Warn));
        }
        if (!string.IsNullOrEmpty(item.Uses)) lines.Add((item.Uses, C.Dim));
        return lines;
    }

    static string Kind(ItemDef item) => item.Category switch
    {
        ItemCategory.Block => item.Family switch
        {
            BlockFamily.Armour => "Block family · any shape",
            BlockFamily.Deco => $"Deco piece{(item.Group != null ? $" · {item.Group}" : "")}",
            _ => PlayerInventory.CoreItems.Contains(item.Id) ? "Core · founds a structure" : "Device block",
        },
        ItemCategory.Raw => "Raw",
        ItemCategory.Unique => "Unique find",
        ItemCategory.Component when PlayerInventory.CoreItems.Contains(item.Id) => "Core · founds a structure",
        _ => item.Category.ToString(),
    };

    static string Maker(ItemCatalogue items, int tier) => tier < 0 ? "Suit fabricator" : $"{Capital(items.Machines.TierName(tier))} constructor or better";

    static string Capital(string s) => string.IsNullOrEmpty(s) ? s : char.ToUpperInvariant(s[0]) + s[1..];

    static string Found(ItemDef item) =>
        !string.IsNullOrEmpty(item.Where) ? $"Found: {item.Where}" : item.Source switch
        {
            ItemSource.Mined => "Mined from ore deposits",
            ItemSource.Dug => "Dug from any rock",
            ItemSource.Harvested => "Harvested from plants and creatures",
            ItemSource.Found => "Found only in ruins, wrecks and derelicts",
            _ => "Not crafted",
        };
}
