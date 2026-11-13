using System;
using System.Collections.Generic;
using System.Linq;
using DiegeticUi.Theme;
using GalHack.Items;
using Godot;
using Slice.Game;
using Slice.Items;
using Palette = DiegeticUi.Theme.Palette;

namespace Slice.Ui;

/// <summary>
/// What is selected in the menu, in one grid: Ctrl+click adds or removes a stack, Shift+click selects
/// the range from the last click, Ctrl+A selects everything the grid shows (so a text filter limits it).
/// Dragging one selected stack drags them all.
/// </summary>
public static class Selection
{
    public static ItemGrid Grid { get; private set; }
    public static readonly HashSet<int> Slots = new();
    public static int Anchor = -1;

    public static bool Has(ItemGrid grid, int index) => Grid == grid && Slots.Contains(index);

    public static void Clear()
    {
        Grid = null;
        Slots.Clear();
        Anchor = -1;
    }

    static void Own(ItemGrid grid)
    {
        if (Grid == grid) return;
        Slots.Clear();
        Grid = grid;
    }

    public static void Set(ItemGrid grid, int index)
    {
        Own(grid);
        Slots.Clear();
        Slots.Add(index);
        Anchor = index;
    }

    public static void Toggle(ItemGrid grid, int index)
    {
        Own(grid);
        if (!Slots.Remove(index)) Slots.Add(index);
        Anchor = index;
    }

    /// <summary>From the anchor to a slot, in the order the grid shows its stacks (empty slots don't count).</summary>
    public static void Range(ItemGrid grid, int index, IReadOnlyList<int> shown)
    {
        Own(grid);
        var order = shown.ToList();
        int a = Anchor >= 0 ? order.IndexOf(Anchor) : -1, b = order.IndexOf(index);
        if (a < 0 || b < 0) { Slots.Add(index); Anchor = index; return; }
        for (int i = Math.Min(a, b); i <= Math.Max(a, b); i++)
            if (!grid[order[i]].IsEmpty) Slots.Add(order[i]);
    }

    /// <summary>Every stack the grid shows.</summary>
    public static void All(ItemGrid grid, IReadOnlyList<int> shown)
    {
        Own(grid);
        Slots.Clear();
        foreach (int i in shown)
            if (i < grid.Count && !grid[i].IsEmpty) Slots.Add(i);
        Anchor = Slots.Count > 0 ? Slots.Min() : -1;
    }

    /// <summary>The selected slots that still hold something, in slot order.</summary>
    public static List<int> Live(ItemGrid grid) =>
        Grid != grid ? new List<int>() : Slots.Where(i => i < grid.Count && !grid[i].IsEmpty).OrderBy(i => i).ToList();
}

/// <summary>
/// Drawing and handling inventory slots, shared by every grid on the menu (the bag and toolbar,
/// a core's storage, an opened container, a body, a backpack). Each stack is drawn as a small 3D icon
/// (rendered once) with its short name, and its full details in the tooltip.
///
/// Moving: drag a stack to move it (onto a slot, a storage tab, or out of the window to set it
/// down); right-drag takes half, Ctrl + right-drag one, and the wheel changes the amount while
/// holding it. Ctrl+click adds a stack to the selection, Shift+click selects a range, Ctrl+A selects
/// all (all respecting the text filter) and dragging one selected stack drags the lot. Double-click
/// sends a stack (or the selection) across to the other panel; a plain right click splits a stack.
/// With an <see cref="ItemView"/> a grid shows only what its filter lets through, in its chosen order.
/// </summary>
public static class Slots
{
    static Palette C => UiTheme.Colors;

    // The grid being laid out and what it shows (for range selection), and the grid the pointer is over (for Ctrl+A).
    static ItemGrid _shownGrid;
    static IReadOnlyList<int> _shown;
    public static ItemGrid HoverGrid { get; private set; }
    public static IReadOnlyList<int> HoverShown { get; private set; }

    // The last click, for double clicks.
    static ItemGrid _clickGrid;
    static int _clickIndex = -1;
    static double _clickAt = -10;

    /// <summary>Ctrl+A: selects every stack the grid under the pointer shows (a text filter limits it).</summary>
    public static bool SelectAll()
    {
        if (HoverGrid == null) return false;
        Selection.All(HoverGrid, HoverShown);
        return Selection.Slots.Count > 0;
    }

    /// <summary>
    /// One slot: drag it to move its stack (onto another slot, another grid, a storage tab, or out of
    /// the window to set it down), click to select (Ctrl adds, Shift ranges), double-click to send it
    /// across to <paramref name="other"/>, right-click to split it. Drops onto it move stacks in (and,
    /// on the toolbar, Creative block shortcuts).
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
        bool picked = Selection.Has(grid, index);
        if (g.Drawing)
        {
            g.Fill(r, g.Back(new Color(C.Glass, 0.9f)));
            if (picked) g.Fill(r, new Color(C.Holo, 0.22f));
            g.Outline(r, selected ? C.Accent : picked ? C.Holo : new Color(C.Rule, 0.9f), selected || picked ? 3 : 1.5f);
            if (!s.IsEmpty) DrawStack(g, r, shown, tone, label, count);
        }
        if (shown != null)
        {
            int n = s.Block != null ? 0 : s.Count;
            g.SetTip(r, () => Tooltips.For(game, shown, n));
        }
        List<int> live = picked ? Selection.Live(grid) : null;
        object payload = s.IsEmpty ? null : new SlotRef(grid, index, Math.Max(1, s.Count)) { Group = live is { Count: > 1 } ? live : null };
        if (g.Draggable(id, r, payload, label, tone)) Click(g, game, grid, index, other);
        if (g.RightClick(id, r) && grid.Split(index)) g.Sound = "drop";
        if (g.DropTarget(id, r, out object dropped))
        {
            if (dropped is SlotRef from) Drop(game, from, grid, index);
            else if (dropped is BlockRef block && game.Mode == GameMode.Creative && grid == game.Pack.Grid && index < grid.Toolbar) grid.SetBlock(index, block.BlockId);
        }
    }

    /// <summary>A stack in a cell: its icon (or, with none yet, a tone bar and the name), short name and count.</summary>
    public static void DrawStack(Gui g, Rect2 r, ItemDef item, Color tone, string label, string count, float alpha = 1f)
    {
        Texture2D icon = item == null ? null : ItemIcons.Shared?.Get(item);
        g.Fill(new Rect2(r.Position + new Vector2(3, 3), new Vector2(r.Size.X - 6, 4)), new Color(tone, alpha));
        if (icon != null)
        {
            float side = r.Size.X - 14;
            g.Canvas.DrawTextureRect(icon, new Rect2(r.Position + new Vector2((r.Size.X - side) / 2, 6), new Vector2(side, side)), false, new Color(1, 1, 1, alpha));
            g.TextIn(label, new Rect2(r.Position + new Vector2(1, r.Size.Y - 15), new Vector2(r.Size.X - 2, 14)), new Color(C.Text, alpha), 10, HorizontalAlignment.Center);
            if (count.Length > 0)
            {
                g.Fill(new Rect2(r.Position + new Vector2(r.Size.X - 6 - count.Length * 8.5f, 8), new Vector2(count.Length * 8.5f + 4, 17)), new Color(C.Glass, 0.8f));
                g.TextIn(count, new Rect2(r.Position + new Vector2(2, 8), new Vector2(r.Size.X - 6, 18)), new Color(C.Accent2, alpha), 13, HorizontalAlignment.Right, UiTheme.Mono);
            }
            return;
        }
        g.TextIn(label, new Rect2(r.Position + new Vector2(2, 14), new Vector2(r.Size.X - 4, 22)), new Color(C.Text, alpha), 12, HorizontalAlignment.Center);
        g.TextIn(count, new Rect2(r.Position + new Vector2(2, r.Size.Y - 22), new Vector2(r.Size.X - 6, 20)), new Color(C.Accent2, alpha), 14, HorizontalAlignment.Right, UiTheme.Mono);
    }

    // ---- Clicks -----------------------------------------------------------------------------------

    static void Click(Gui g, Game.Game game, ItemGrid grid, int index, ItemGrid other)
    {
        double now = g.Clock();
        bool twice = _clickGrid == grid && _clickIndex == index && now - _clickAt < 0.45 && !g.Ctrl && !g.Shift;
        _clickGrid = grid;
        _clickIndex = index;
        _clickAt = twice ? -10 : now;
        if (twice)
        {
            SendAcross(game, grid, index, other ?? grid);
            g.Sound = "drop";
            return;
        }
        if (g.Ctrl) Selection.Toggle(grid, index);
        else if (g.Shift) Selection.Range(grid, index, _shownGrid == grid && _shown != null ? _shown : Enumerable.Range(0, grid.Count).ToList());
        else Selection.Set(grid, index);
    }

    /// <summary>Double-click: the stack (or every selected stack, if it is one of them) goes across to the other grid.</summary>
    public static void SendAcross(Game.Game game, ItemGrid grid, int index, ItemGrid to)
    {
        List<int> live = Selection.Has(grid, index) ? Selection.Live(grid) : null;
        var from = live is { Count: > 1 } ? live : new List<int> { index };
        SendStacks(game, grid, from, to, int.MaxValue);
        Selection.Clear();
    }

    /// <summary>
    /// Sends stacks to another grid: what fits moves, the rest stays, and a message says so when it
    /// didn't all go. Returns how many items moved.
    /// </summary>
    public static int SendStacks(Game.Game game, ItemGrid from, IReadOnlyList<int> slots, ItemGrid to, int amount)
    {
        from.Reconcile();
        int wanted = 0, moved = 0;
        foreach (int i in slots)
        {
            if (i >= from.Count || from[i].Item == null) continue;
            wanted += Math.Min(from[i].Count, amount);
            moved += ItemGrid.QuickMove(from, i, to, amount);
        }
        if (moved < wanted)
            game.Say($"{to.Name.ToUpperInvariant()} FULL · {moved}/{wanted} MOVED", Tones.Caution);
        return moved;
    }

    /// <summary>
    /// Something dragged was let go on a slot (or, with <paramref name="index"/> -1, on a storage tab):
    /// the whole stack, the chosen amount, or the whole selection.
    /// </summary>
    public static void Drop(Game.Game game, SlotRef from, ItemGrid grid, int index)
    {
        if (from.Group is { Count: > 1 } group && (index < 0 || from.Grid != grid))
        {
            SendStacks(game, from.Grid, group, grid, int.MaxValue);
            Selection.Clear();
            return;
        }
        if (index < 0)
        {
            if (from.Grid != grid) SendStacks(game, from.Grid, new[] { from.Index }, grid, from.Amount);
            Selection.Clear();
            return;
        }
        int want = Math.Min(from.Amount, from.Grid[from.Index].Count);
        int moved = ItemGrid.Move(from.Grid, from.Index, grid, index, from.Amount);
        if (from.Grid != grid && moved < want)
            game.Say($"{grid.Name.ToUpperInvariant()} FULL · {moved}/{want} MOVED", Tones.Caution);
        Selection.Clear();
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
        shown ??= Enumerable.Range(first, Math.Max(0, Math.Min(count, grid.Count - first))).ToList();
        if (!g.Drawing && g.Over(area.Grow(2)))
        {
            HoverGrid = grid;
            HoverShown = shown;
        }
        _shownGrid = grid;
        _shown = shown;
        int total = shown.Count;
        for (int k = 0; k < columns * rows; k++)
        {
            int at = scroll * columns + k;
            if (at >= total) break;
            int index = shown[at];
            if (index >= grid.Count) break;
            var r = new Rect2(area.Position + new Vector2(k % columns * (cell + 6), k / columns * (cell + 6)), new Vector2(cell, cell));
            Slot(g, game, $"{prefix}:{index}", grid, index, r, other);
        }
        return total;
    }
}

/// <summary>
/// A recipe's inputs along a row: each its item's icon (the one the bag shows) with the count one run takes
/// beside it, the name and the stock on hover. Every slot is one width, so the inputs line up in columns down
/// a list. A count that can't be covered now is amber, red when nothing there makes it either; with more
/// inputs than the room holds, the last slot shows how many more.
/// </summary>
public static class Ingredients
{
    static Palette C => UiTheme.Colors;

    /// <summary>The counts' type size, and the gap after each slot.</summary>
    public const int CountSize = 13;
    public const float Gap = 8;

    /// <summary>An icon's side in a row of this height.</summary>
    public static float Side(float rowH) => rowH - 4;

    /// <summary>One slot's width in rows of this height: room for the longest count any of these recipes takes.</summary>
    public static float Slot(float rowH, IEnumerable<ItemDef> made)
    {
        int digits = 1;
        foreach (ItemDef i in made)
            if (i.Recipe != null)
                foreach (var (_, n) in i.Recipe.Inputs) digits = Math.Max(digits, n.ToString().Length);
        return Side(rowH) + 3 + CountW(digits) + Gap;
    }

    static float CountW(int digits) => Paint.TextWidth(UiTheme.Mono, CountSize, new string('8', digits)) + 1;

    /// <summary>The room a row of the most inputs any of these recipes takes needs.</summary>
    public static float Room(float slot, IEnumerable<ItemDef> made) =>
        Math.Max(1, made.Select(i => i.Recipe?.Inputs.Count ?? 0).DefaultIfEmpty(0).Max()) * slot - Gap;

    /// <summary>
    /// The inputs in <paramref name="r"/>, from its left: <paramref name="have"/> is how many are there to use,
    /// <paramref name="made"/> (if given) whether what is short is made there too.
    /// </summary>
    public static void Draw(Gui g, Game.Game game, Rect2 r, IReadOnlyList<(ItemDef Item, int Count)> inputs, float slot, Func<ItemDef, int> have, Func<ItemDef, bool> made = null, float alpha = 1f)
    {
        if (inputs == null || inputs.Count == 0) return;
        float side = Side(r.Size.Y);
        int fit = Math.Max(0, (int)((r.Size.X + Gap) / slot));
        int shown = inputs.Count <= fit ? inputs.Count : Math.Max(0, fit - 1);
        Color Tone(ItemDef item, int need) => have(item) >= need ? C.Text : made == null || made(item) ? C.Warn : C.Danger;
        for (int k = 0; k < shown; k++)
        {
            var (item, need) = inputs[k];
            float x = r.Position.X + k * slot;
            var box = new Rect2(x, r.Position.Y + (r.Size.Y - side) / 2f, side, side);
            if (ItemIcons.Shared?.Get(item) is Texture2D icon) g.Image(icon, box, new Color(1, 1, 1, alpha));
            else g.Fill(box, new Color(new Color(Rarities.Color(item.Rarity) << 8 | 0xff), 0.3f * alpha)); // not rendered yet, or a headless run
            Color tone = Tone(item, need);
            g.TextIn($"{need}", new Rect2(box.End.X + 3, r.Position.Y, slot - side - 3 - Gap, r.Size.Y), new Color(tone, alpha), CountSize, HorizontalAlignment.Left, UiTheme.Mono);
            g.SetTip(new Rect2(x, r.Position.Y, slot - Gap / 2, r.Size.Y), () =>
            {
                List<(string Text, Color Colour)> tip = Tooltips.For(game, item, 0);
                tip.Insert(1, ($"HAVE {have(item)} · NEED {need}", have(item) >= need ? C.Good : tone));
                return tip;
            });
        }
        if (shown == inputs.Count || fit == 0) return;
        float rx = r.Position.X + shown * slot;
        var rest = new Rect2(rx, r.Position.Y, r.End.X - rx, r.Size.Y);
        bool short_ = inputs.Skip(shown).Any(i => have(i.Item) < i.Count);
        g.TextIn($"+{inputs.Count - shown}", rest, new Color(short_ ? C.Warn : C.Dim, alpha), CountSize, HorizontalAlignment.Left, UiTheme.Mono);
        g.SetTip(rest, () => inputs.Skip(shown).Select(i => ($"{i.Count} {i.Item.Name}  (HAVE {have(i.Item)})", Tone(i.Item, i.Count))).ToList());
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
        if (g.TextBox($"{key}:filter", box, view.Text, readOnly ? "FILTER" : "FILTER [/]", focused, readOnly))
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
/// it is made (inputs, constructor, time) or where it is found; its stack size, and for a block its
/// size in metres; whether the tech tree has it locked; what it is for.
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
            ($"{item.MassKg:0.##} kg · {item.VolumeSu:0.##} SU{(count > 1 ? $"   ×{count} · {item.MassKg * count:0.#} kg · {item.VolumeSu * count:0.#} SU" : "")}", C.Text),
        };
        if (item.Recipe != null)
        {
            lines.Add(($"RECIPE · {items.Describe(item.Recipe.InputsFor(1), 4)}{(item.Recipe.Count > 1 ? $" · MAKES {item.Recipe.Count}" : "")}", C.Text));
            lines.Add(($"{Maker(items, item.Recipe.Tier)} · {item.Recipe.Seconds:0.#} s", C.Text));
        }
        else lines.Add((Found(item), C.Text));
        GalHack.Structures.Catalogue blocks = game.Zone.Data.Catalogue;
        string fits = item.IsBlock && blocks.TryGetType(item.BlockId, out GalHack.Structures.BlockType type) ? $" · {GalHack.Structures.Footprint.Label(type, blocks.Grid.CellSize)}" : "";
        lines.Add(($"STACK {item.StackSize}{fits}", C.Text));
        Progress p = game.Progress;
        if (item.Unlock != null)
        {
            bool open = p == null || p.ItemAllowed(item);
            lines.Add((open ? $"UNLOCKED · LV {item.Unlock.Level}" : $"LOCKED · LV {item.Unlock.Level} · {item.Unlock.Cost} PT",
                open ? C.Good : C.Warn));
        }
        if (!string.IsNullOrEmpty(item.Uses)) lines.Add((item.Uses, C.Dim));
        return lines;
    }

    static string Kind(ItemDef item) => item.Category switch
    {
        ItemCategory.Block => item.Family switch
        {
            BlockFamily.Armour => "Block · any shape",
            BlockFamily.Deco => $"Deco piece{(item.Group != null ? $" · {item.Group}" : "")}",
            _ => item.Id == PlayerInventory.Core ? "Core" : "Device block",
        },
        ItemCategory.Raw => "Raw",
        ItemCategory.Unique => "Unique find",
        ItemCategory.Component when item.Id == PlayerInventory.Core => "Core",
        _ => item.Category.ToString(),
    };

    static string Maker(ItemCatalogue items, int tier) => tier < 0 ? "Suit fabricator" : $"{Capital(items.Machines.TierName(tier))} constructor+";

    static string Capital(string s) => string.IsNullOrEmpty(s) ? s : char.ToUpperInvariant(s[0]) + s[1..];

    static string Found(ItemDef item) =>
        !string.IsNullOrEmpty(item.Where) ? $"SOURCE · {item.Where.ToUpperInvariant()}" : item.Source switch
        {
            ItemSource.Mined => "SOURCE · ORE DEPOSITS",
            ItemSource.Dug => "SOURCE · ANY ROCK",
            ItemSource.Harvested => "SOURCE · PLANTS, CREATURES",
            ItemSource.Found => "SOURCE · RUINS, WRECKS, DERELICTS",
            _ => "NOT CRAFTED",
        };
}
