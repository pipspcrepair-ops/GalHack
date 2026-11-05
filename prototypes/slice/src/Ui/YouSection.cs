using System;
using System.Collections.Generic;
using System.Linq;
using DiegeticUi.Theme;
using GalHack.Items;
using GalHack.Structures;
using Godot;
using Slice.Game;
using Slice.Items;
using SurvivalVitals.Model;
using Progress = Slice.Items.Progress;
using Palette = DiegeticUi.Theme.Palette;

namespace Slice.Ui;

/// <summary>Drawing and handling inventory slots, shared by every grid on the menu.</summary>
public static class Slots
{
    static Palette C => UiTheme.Colors;

    /// <summary>
    /// One slot: drag it to move its stack (onto another slot, another grid, or out of the window
    /// to set it down), Shift-click to send the stack to <paramref name="other"/>, right-click to
    /// split it. Drops onto it move stacks in (and, on the toolbar, block shortcuts).
    /// </summary>
    public static void Slot(Gui g, Game.Game game, string id, ItemGrid grid, int index, Rect2 r, ItemGrid other, bool selected = false)
    {
        grid.Reconcile();
        Slot s = grid[index];
        ItemCatalogue items = game.Zone.Data.Items;
        Color tone = C.Rule;
        string label = "", count = "";
        if (s.Block == PlayerInventory.BlueprintBlock) { tone = C.Holo; label = "SKIFF BP"; }
        else if (s.Block != null && game.Zone.Data.Catalogue.TryGetType(s.Block, out BlockType bt)) { tone = C.Accent2; label = Short(bt.Name); }
        else if (s.Item != null)
        {
            ItemDef item = items.Item(s.Item);
            tone = new Color(Rarities.Color(item.Rarity) << 8 | 0xff);
            label = item.ShortName;
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
                if (s.Block != null) g.TextIn("BLOCK", new Rect2(r.Position + new Vector2(2, r.Size.Y - 22), new Vector2(r.Size.X - 4, 20)), C.Dim, 11);
            }
            if (g.Over(r) && g.DragId == null && s.Item != null)
                g.TextIn(items.Item(s.Item).Name, new Rect2(r.Position + new Vector2(-40, -22), new Vector2(r.Size.X + 80, 20)), C.Text, 13, HorizontalAlignment.Center);
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
            else if (dropped is BlockRef block && grid == game.Pack.Grid && index < grid.Toolbar) grid.SetBlock(index, block.BlockId);
        }
    }

    public static string Short(string name) => name.Length <= 9 ? name.ToUpperInvariant() : name[..9].ToUpperInvariant();

    /// <summary>A grid of slots in rows, from a slot index on.</summary>
    public static void Grid(Gui g, Game.Game game, string prefix, ItemGrid grid, int first, int count, int columns, Rect2 area, float cell, ItemGrid other, int scroll = 0)
    {
        int rows = Math.Max(1, (int)((area.Size.Y + 6) / (cell + 6)));
        for (int k = 0; k < count && k / columns < rows; k++)
        {
            int index = first + scroll * columns + k;
            if (index >= grid.Count) break;
            var r = new Rect2(area.Position + new Vector2(k % columns * (cell + 6), k / columns * (cell + 6)), new Vector2(cell, cell));
            Slot(g, game, $"{prefix}:{index}", grid, index, r, other);
        }
    }
}

/// <summary>
/// YOU: your bag (8 x 6) and toolbar (1 to 9), equipment round a paperdoll of you with your
/// vitals, armour and level beside it, and on the right either the container you opened (E) with
/// take-all and store-all, or the suit fabricator and the build palette.
/// </summary>
public sealed class YouSection : IMenuSection
{
    static Palette C => UiTheme.Colors;
    readonly Game.Game _game;
    readonly ConstructorSection _fabricator;
    public bool ShowBlocks;
    int _containerScroll, _paletteScroll;
    Paperdoll _doll;

    public YouSection(Game.Game game)
    {
        _game = game;
        _fabricator = new ConstructorSection(game, () => game.Fabricator, "SUIT FABRICATOR", "fab");
    }

    public Vector2 PreferredSize => new(1560, 790);

    public void Build(Gui g, Rect2 area)
    {
        Vector2 o = area.Position;
        float sx = area.Size.X / PreferredSize.X;
        PlayerInventory pack = _game.Pack;
        OpenContainer open = _game.Opened;
        ItemGrid other = open?.Grid;

        // ---- Paperdoll, equipment, vitals and level -------------------------------------------
        var doll = new Rect2(o + new Vector2(70, 26), new Vector2(240, 380));
        g.Caption("YOU", o, 420);
        g.Fill(doll, new Color(C.Panel, 0.6f));
        if (g.Drawing)
        {
            _doll ??= _game.GetNodeOrNull<Paperdoll>("Paperdoll");
            Texture2D tex = _doll?.Texture;
            if (tex != null) g.Canvas.DrawTextureRect(tex, doll, false);
        }
        Loadout gear = _game.Suit.Sim.Player.Gear;
        var equipment = new (string label, string value)[]
        {
            ("HELMET", gear.HelmetOn ? "SEALED" : "OPEN"),
            ("ARMOUR", gear.Suit?.Kind?.ToUpperInvariant() ?? "NONE"),
            ("BOOST 1", gear.Boosters.Count > 0 ? gear.Boosters[0].ToUpperInvariant() : ""),
            ("BOOST 2", gear.Boosters.Count > 1 ? gear.Boosters[1].ToUpperInvariant() : ""),
            ("IN HAND", pack.InHand.Item != null ? _game.Zone.Data.Items.Item(pack.InHand.Item).ShortName : pack.InHand.Block != null ? "BLOCK" : ""),
        };
        for (int i = 0; i < equipment.Length; i++)
        {
            float x = i < 2 ? 0 : 320, y = 52 + (i < 2 ? i : i - 2) * 92;
            var r = new Rect2(o + new Vector2(x, y), new Vector2(64, 64));
            g.Fill(r, new Color(C.Glass, 0.9f));
            g.Outline(r, string.IsNullOrEmpty(equipment[i].value) ? new Color(C.Rule, 0.6f) : C.Accent2, 1.5f);
            g.TextIn(equipment[i].label, new Rect2(r.Position + new Vector2(0, -18), new Vector2(64, 16)), C.Dim, 11, HorizontalAlignment.Center);
            g.TextIn(equipment[i].value, r.Grow(-3), C.Text, 11, HorizontalAlignment.Center);
        }
        VitalsBlock(g, new Rect2(o + new Vector2(0, 420), new Vector2(420, 360)));

        // ---- Bag and toolbar ------------------------------------------------------------------
        Vector2 b = o + new Vector2(450 * sx, 0);
        g.Caption("BAG", b, 560);
        Slots.Grid(g, _game, "bag", pack.Grid, PlayerInventory.ToolbarSlots, PlayerInventory.Columns * PlayerInventory.Rows, PlayerInventory.Columns,
            new Rect2(b + new Vector2(0, 36), new Vector2(560, 420)), 64, other ?? pack.Grid);
        double used = pack.Grid.UsedSu, cap = pack.Storage.CapacitySu;
        g.Text($"{pack.Grid.MassKg:F1} kg   {used:F1} / {cap:F0} SU   {pack.Grid.Used - pack.Grid.Slots.Take(9).Count(s => !s.IsEmpty)}/{pack.Grid.BagSlots} slots",
            b + new Vector2(0, 462), C.Text, 16, UiTheme.Mono);
        g.Bar(new Rect2(b + new Vector2(0, 486), new Vector2(560, 12)), used, cap, used > cap * 0.9 ? C.Warn : C.Good, 20);
        g.Caption("TOOLBAR  (1 TO 9)", b + new Vector2(0, 512), 560);
        for (int i = 0; i < PlayerInventory.ToolbarSlots; i++)
        {
            var r = new Rect2(b + new Vector2(i * 62, 548), new Vector2(56, 56));
            Slots.Slot(g, _game, $"bar:{i}", pack.Grid, i, r, other ?? pack.Grid, selected: i == pack.Selected);
            g.TextIn($"{i + 1}", new Rect2(r.Position + new Vector2(0, 58), new Vector2(56, 16)), C.Dim, 12, HorizontalAlignment.Center);
        }
        g.Text("Drag to move · Shift-click to send · Right-click to split · drag out of the panels to set down", b + new Vector2(0, 630), C.Dim, 13);
        var dropZone = new Rect2(b + new Vector2(0, 660), new Vector2(560, 50));
        g.Fill(dropZone, new Color(C.Panel, 0.5f));
        g.TextIn("SET DOWN HERE: drop a stack on the ground", dropZone, C.Dim, 15, HorizontalAlignment.Center);
        if (g.DropTarget("drop-ground", dropZone, out object d) && d is SlotRef sr) _game.Drop(sr.Grid, sr.Index);

        // ---- The open container, or the fabricator and the palette -----------------------------
        Vector2 c = o + new Vector2(1030 * sx, 0);
        var right = new Rect2(c, new Vector2(area.End.X - c.X, area.Size.Y));
        if (open != null) Container(g, right, open);
        else
        {
            if (g.Tab("you:fab", new Rect2(c, new Vector2(250, 36)), "FABRICATOR", !ShowBlocks, C.Accent)) ShowBlocks = false;
            if (g.Tab("you:blocks", new Rect2(c + new Vector2(260, 0), new Vector2(250, 36)), "BUILD PALETTE", ShowBlocks, C.Accent2)) ShowBlocks = true;
            var inner = new Rect2(c + new Vector2(0, 50), new Vector2(right.Size.X, right.Size.Y - 50));
            if (ShowBlocks) Palette(g, inner);
            else _fabricator.Build(g, inner);
        }
        // Released outside every panel: set down too.
        var panels = new Rect2(b, new Vector2(560, 720)).Merge(right);
        if (g.DroppedOutside(panels, out object outside) && outside is SlotRef s2) _game.Drop(s2.Grid, s2.Index);
    }

    void VitalsBlock(Gui g, Rect2 r)
    {
        Slice.Player.Suit suit = _game.Suit;
        Vitals v = suit.Vitals;
        VitalsRules rules = suit.Sim.Data.Vitals;
        Modifiers mods = suit.Sim.Mods;
        var rows = new (string, string, double, Color)[]
        {
            ("HEALTH", $"{v.Health:F0}", v.Health / rules.Health.Max, C.Good),
            ("FOOD", $"{suit.FoodShare * 100:F0}%", suit.FoodShare, C.Accent2),
            ("OXYGEN", $"{suit.OxygenShare * 100:F0}%", suit.OxygenShare, C.Holo),
            ("STAMINA", $"{v.Stamina:F0}", v.Stamina / rules.Stamina.Max, C.Accent),
            ("BODY TEMP", $"{v.BodyTemperature:F1}°C", 1 - Math.Abs(v.BodyTemperature - rules.Temperature.Normal) / 4.0, C.Good),
            ("RADIATION", $"{v.Radiation:F1}", 1 - v.Radiation / (rules.Radiation.DamageAbove * 1.5), C.Warn),
        };
        for (int i = 0; i < rows.Length; i++)
        {
            var (label, value, share, colour) = rows[i];
            float y = r.Position.Y + i * 30;
            g.Text(label, new Vector2(r.Position.X, y), C.Dim, 15);
            g.Text(value, new Vector2(r.Position.X + 120, y), C.Text, 16, UiTheme.Mono);
            g.Bar(new Rect2(r.Position.X + 230, y + 4, 180, 12), Math.Clamp(share, 0, 1), 1, colour, 10);
        }
        float yy = r.Position.Y + rows.Length * 30 + 8;
        g.Text($"ARMOUR {mods.Armor:F0}   COLD {mods.ColdProtection:F0}   HEAT {mods.HeatProtection:F0}   RAD {mods.RadiationShielding:F0}", new Vector2(r.Position.X, yy), C.Text, 14, UiTheme.Mono);
        Progress p = _game.Progress;
        var (into, span) = p.ToNext;
        g.Text($"LEVEL {p.Level} · {p.Xp} XP · {p.Points} POINTS", new Vector2(r.Position.X, yy + 30), C.Accent2, 17, UiTheme.Display);
        g.Bar(new Rect2(r.Position.X, yy + 58, 410, 12), into, span, C.Accent2, 20);
        g.Text(p.Level >= p.Rules.Cap ? $"past the cap: a point every {p.Rules.PastCap} XP" : $"{span - into} XP to level {p.Level + 1}", new Vector2(r.Position.X, yy + 76), C.Dim, 14);
    }

    void Container(Gui g, Rect2 r, OpenContainer open)
    {
        g.Caption(open.Title.ToUpperInvariant(), r.Position, r.Size.X);
        ItemGrid grid = open.Grid;
        grid.Reconcile();
        int columns = 7;
        float cell = 64;
        int rows = Math.Max(1, (grid.Count + columns - 1) / columns);
        int visible = 6;
        _containerScroll = Math.Clamp(_containerScroll, 0, Math.Max(0, rows - visible));
        Slots.Grid(g, _game, "box", grid, 0, columns * visible, columns, new Rect2(r.Position + new Vector2(0, 36), new Vector2(columns * (cell + 6), visible * (cell + 6))), cell, _game.Pack.Grid, _containerScroll);
        float y = r.Position.Y + 36 + visible * 70 + 8;
        if (rows > visible)
        {
            if (g.Button("box:up", new Rect2(r.Position.X, y, 60, 30), "UP", C.Accent2)) _containerScroll--;
            if (g.Button("box:down", new Rect2(r.Position.X + 70, y, 60, 30), "DOWN", C.Accent2)) _containerScroll++;
        }
        g.Text($"{grid.MassKg:F1} kg   {grid.UsedSu:F1}{(double.IsInfinity(grid.Storage.CapacitySu) ? "" : $" / {grid.Storage.CapacitySu:F0}")} SU", new Vector2(r.Position.X + 150, y + 4), C.Text, 15, UiTheme.Mono);
        y += 44;
        if (g.Button("box:take-all", new Rect2(r.Position.X, y, 220, 44), "TAKE ALL", C.Good))
        {
            Bill moved = ItemGrid.MoveAll(grid, _game.Pack.Grid);
            if (!moved.IsEmpty) _game.Say($"Took {_game.Zone.Data.Items.Describe(moved, 4)}", Tones.Good);
            else _game.Say(grid.Storage.IsEmpty ? "It's empty" : "No room in your bag", Tones.Caution);
        }
        if (g.Button("box:store-all", new Rect2(r.Position.X + 240, y, 220, 44), "STORE ALL", C.Accent2))
        {
            Bill moved = ItemGrid.MoveAll(_game.Pack.Grid, grid, includeToolbar: false);
            _game.Say(moved.IsEmpty ? "Nothing in your bag to store (the toolbar stays with you)" : $"Stored {_game.Zone.Data.Items.Describe(moved, 4)}", moved.IsEmpty ? Tones.Caution : Tones.Good);
        }
        if (g.Button("box:close", new Rect2(r.Position.X, y + 56, 120, 34), "CLOSE", C.Danger)) _game.CloseContainer();
    }

    /// <summary>The build palette: every unlocked block on both grids, dragged onto the toolbar. Locked ones are greyed with what they need.</summary>
    void Palette(Gui g, Rect2 r)
    {
        var entries = new List<(string block, bool small, string name, bool allowed, string why)>();
        Progress p = _game.Progress;
        Catalogue cat = _game.Zone.Data.Catalogue;
        foreach (BlockType t in cat.Types)
        {
            if (t.IsCore || !_game.Zone.Data.Items.Blocks.TryGet(t.Id, out BlockBill bill)) continue;
            foreach (bool small in new[] { false, true })
            {
                bool allowed = p.BlockAllowed(t.Id, small);
                entries.Add((t.Id, small, $"{t.Name}", allowed, allowed ? "" : p.Needs(p.NodeOfBlock(t.Id, small))));
            }
        }
        entries = entries.OrderBy(e => !e.allowed).ThenBy(e => e.small).ThenBy(e => e.name).ToList();
        int perPage = 14;
        _paletteScroll = Math.Clamp(_paletteScroll, 0, Math.Max(0, (entries.Count - 1) / perPage));
        g.Text("Drag a block onto the toolbar. It is paid for from your bag (then the structure's storage) when you place it.", r.Position, C.Dim, 13);
        var bp = new Rect2(r.Position + new Vector2(0, 24), new Vector2(r.Size.X, 34));
        g.Fill(bp, new Color(C.Holo, 0.25f));
        g.TextIn("BLUEPRINT PROJECTOR: the Skiff (optional shortcut)", bp.Grow(-6), C.Text, 15);
        g.Draggable("palette:blueprint", bp, new BlockRef(PlayerInventory.BlueprintBlock), "SKIFF BP", C.Holo);
        for (int i = 0; i < perPage; i++)
        {
            int k = _paletteScroll * perPage + i;
            if (k >= entries.Count) break;
            var (block, small, name, allowed, why) = entries[k];
            var row = new Rect2(r.Position + new Vector2(0, 66 + i * 40), new Vector2(r.Size.X, 34));
            g.Fill(row, new Color(allowed ? C.Glass : C.Panel, 0.85f));
            g.Fill(new Rect2(row.Position, new Vector2(5, row.Size.Y)), allowed ? (small ? C.Accent2 : C.Accent) : C.Dim);
            g.Text($"{name}", row.Position + new Vector2(12, 7), allowed ? C.Text : C.Dim, 15);
            g.Text(small ? "SMALL" : "LARGE", row.Position + new Vector2(240, 8), C.Dim, 13);
            Bill cost = _game.Builder.CostOf(block, small);
            g.Text(allowed ? _game.Zone.Data.Items.Describe(cost, 3) : why, row.Position + new Vector2(310, 9), allowed ? C.Text : C.Warn, 12);
            if (allowed) g.Draggable($"palette:{block}@{(small ? "small" : "large")}", row, new BlockRef(block), Slots.Short(name), small ? C.Accent2 : C.Accent);
        }
        float y = r.Position.Y + 66 + perPage * 40 + 6;
        if (g.Button("palette:up", new Rect2(r.Position.X, y, 60, 30), "UP", C.Accent2, _paletteScroll > 0)) _paletteScroll--;
        if (g.Button("palette:down", new Rect2(r.Position.X + 70, y, 60, 30), "DOWN", C.Accent2, (_paletteScroll + 1) * perPage < entries.Count)) _paletteScroll++;
    }
}
