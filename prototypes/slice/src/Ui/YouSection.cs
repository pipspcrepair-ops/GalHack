using System;
using System.Collections.Generic;
using System.Linq;
using DiegeticUi.Theme;
using GalHack.Items;
using Godot;
using Slice.Game;
using Slice.Items;
using SurvivalVitals.Model;
using Progress = Slice.Items.Progress;
using Palette = DiegeticUi.Theme.Palette;

namespace Slice.Ui;

/// <summary>
/// YOU: your bag (8 x 6) and toolbar (1 to 9), with a filter bar (text, categories, sort); the
/// linked structure's toolbar under yours when you are in a structure's area; equipment round a
/// paperdoll of you with your vitals, armour and level beside it; and on the right either the
/// container you opened (E) with take-all and store-all, or the suit fabricator and the blocks list.
/// </summary>
public sealed class YouSection : IMenuSection
{
    static Palette C => UiTheme.Colors;
    readonly Game.Game _game;
    readonly ConstructorSection _fabricator;
    public bool ShowBlocks;
    int _bagScroll, _containerScroll, _paletteScroll;
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
        string inHand = pack.HandItem is string hid && _game.Zone.Data.Items.TryGet(hid, out ItemDef hi) ? hi.ShortName : "";
        var equipment = new (string label, string value)[]
        {
            ("HELMET", gear.HelmetOn ? "SEALED" : "OPEN"),
            ("ARMOUR", gear.Suit?.Kind?.ToUpperInvariant() ?? "NONE"),
            ("BOOST 1", gear.Boosters.Count > 0 ? gear.Boosters[0].ToUpperInvariant() : ""),
            ("BOOST 2", gear.Boosters.Count > 1 ? gear.Boosters[1].ToUpperInvariant() : ""),
            ("IN HAND", inHand),
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

        // ---- Bag (with its filter) and toolbars ------------------------------------------------
        Vector2 b = o + new Vector2(450 * sx, 0);
        g.Caption("BAG", b, 560);
        ItemView bagView = FilterBar.Build(g, "bag", new Rect2(b + new Vector2(0, 30), new Vector2(560, FilterBar.Height)));
        var bagArea = new Rect2(b + new Vector2(0, 96), new Vector2(560, 6 * 60 - 6));
        int shownStacks = Slots.Grid(g, _game, "bag", pack.Grid, PlayerInventory.ToolbarSlots, PlayerInventory.Columns * PlayerInventory.Rows, PlayerInventory.Columns,
            bagArea, 54, other ?? pack.Grid, _bagScroll, bagView);
        if (bagView.Filtering || bagView.Sort != ItemSort.Slot)
            g.Text($"{shownStacks} shown{(bagView.Filtering ? " · filtered" : "")}", b + new Vector2(480, 4), C.Dim, 13);
        double used = pack.Grid.UsedSu, cap = pack.Storage.CapacitySu;
        g.Text($"{pack.Grid.MassKg:F1} kg   {used:F1} / {cap:F0} SU   {pack.Grid.Used - pack.Grid.Slots.Take(9).Count(s => !s.IsEmpty)}/{pack.Grid.BagSlots} slots",
            b + new Vector2(0, 454), C.Text, 15, UiTheme.Mono);
        g.Bar(new Rect2(b + new Vector2(0, 476), new Vector2(560, 10)), used, cap, used > cap * 0.9 ? C.Warn : C.Good, 20);
        bool mine = pack.Bar == BarSource.Personal;
        g.Caption($"YOUR TOOLBAR  (1 TO 9){(mine ? "  · IN USE" : "")}", b + new Vector2(0, 494), 560, mine ? C.Accent : C.Dim);
        for (int i = 0; i < PlayerInventory.ToolbarSlots; i++)
        {
            var r = new Rect2(b + new Vector2(i * 62, 526), new Vector2(56, 56));
            Slots.Slot(g, _game, $"bar:{i}", pack.Grid, i, r, other ?? pack.Grid, selected: mine && i == pack.Selected);
        }
        StructureBar(g, new Rect2(b + new Vector2(0, 590), new Vector2(560, 90)));
        var dropZone = new Rect2(b + new Vector2(0, 690), new Vector2(560, 40));
        g.Fill(dropZone, new Color(C.Panel, 0.5f));
        g.TextIn("SET DOWN HERE (or drag out of the panels) · Shift-click sends · right-click splits", dropZone, C.Dim, 13, HorizontalAlignment.Center);
        if (g.DropTarget("drop-ground", dropZone, out object d) && d is SlotRef sr) _game.Drop(sr.Grid, sr.Index);
        if (g.Button("bag:up", new Rect2(b.X + 400, b.Y + 450, 70, 22), "UP", C.Accent2, _bagScroll > 0, size: 12)) _bagScroll--;
        if (g.Button("bag:down", new Rect2(b.X + 480, b.Y + 450, 80, 22), "DOWN", C.Accent2, (_bagScroll + 6) * PlayerInventory.Columns < shownStacks, size: 12)) _bagScroll++;
        _bagScroll = Math.Clamp(_bagScroll, 0, Math.Max(0, (shownStacks - 1) / PlayerInventory.Columns - 5));

        // ---- The open container, or the fabricator and the blocks list --------------------------
        Vector2 c = o + new Vector2(1030 * sx, 0);
        var right = new Rect2(c, new Vector2(area.End.X - c.X, area.Size.Y));
        if (open != null) Container(g, right, open, bagView);
        else
        {
            if (g.Tab("you:fab", new Rect2(c, new Vector2(250, 36)), "FABRICATOR", !ShowBlocks, C.Accent)) ShowBlocks = false;
            if (g.Tab("you:blocks", new Rect2(c + new Vector2(260, 0), new Vector2(250, 36)), _game.Mode == GameMode.Creative ? "BUILD PALETTE" : "BLOCKS", ShowBlocks, C.Accent2)) ShowBlocks = true;
            var inner = new Rect2(c + new Vector2(0, 50), new Vector2(right.Size.X, right.Size.Y - 50));
            if (ShowBlocks) Palette(g, inner);
            else _fabricator.Build(g, inner);
        }
        // Released outside every panel: set down too.
        var panels = new Rect2(b, new Vector2(560, 740)).Merge(right);
        if (g.DroppedOutside(panels, out object outside) && outside is SlotRef s2) _game.Drop(s2.Grid, s2.Index);
    }

    /// <summary>
    /// The linked structure's toolbar (Empyrion's virtual toolbar): nine shortcuts into its shared
    /// storage, each showing how many it holds. Drag a stack from its storage, or a block from the
    /// list, onto a slot to set it; nothing moves out of the storage until you place a block.
    /// </summary>
    void StructureBar(Gui g, Rect2 r)
    {
        PlayerInventory pack = _game.Pack;
        bool linked = pack.Linked != null, inUse = pack.OnStructureBar;
        g.Caption(linked ? $"{pack.LinkedName.ToUpperInvariant()} TOOLBAR  (Y, OR A NUMBER TWICE){(inUse ? "  · IN USE" : "")}" : "STRUCTURE TOOLBAR: in a structure's area only",
            r.Position, r.Size.X, inUse ? C.Accent : C.Dim);
        if (!linked) return;
        string[] bar = pack.StructureBar;
        ItemCatalogue items = _game.Zone.Data.Items;
        for (int i = 0; i < PlayerInventory.ToolbarSlots; i++)
        {
            var s = new Rect2(r.Position + new Vector2(i * 62, 32), new Vector2(56, 56));
            string id = bar[i];
            ItemDef item = id != null && items.TryGet(id, out ItemDef it) ? it : null;
            int n = item == null ? 0 : pack.Linked.Count(id);
            if (g.Drawing)
            {
                bool sel = inUse && i == pack.Selected;
                g.Fill(s, new Color(C.Glass, 0.9f));
                g.Outline(s, sel ? C.Accent : new Color(C.Holo, 0.8f), sel ? 3 : 1.5f);
                if (item != null)
                {
                    g.Fill(new Rect2(s.Position + new Vector2(3, 3), new Vector2(s.Size.X - 6, 6)), C.Holo);
                    g.TextIn(item.ShortName, new Rect2(s.Position + new Vector2(2, 14), new Vector2(s.Size.X - 4, 22)), n > 0 ? C.Text : C.Dim, 12, HorizontalAlignment.Center);
                    g.TextIn($"{n}", new Rect2(s.Position + new Vector2(2, s.Size.Y - 22), new Vector2(s.Size.X - 6, 20)), n > 0 ? C.Holo : C.Danger, 14, HorizontalAlignment.Right, UiTheme.Mono);
                }
            }
            if (item != null) g.SetTip(s, () => Tooltips.For(_game, item, n));
            if (g.Draggable($"sbar:{i}", s, null, "", C.Holo) && g.Shift) bar[i] = null;
            if (g.RightClicked(s)) bar[i] = null;
            if (g.DropTarget($"sbar:{i}", s, out object dropped))
            {
                string set = dropped switch
                {
                    SlotRef sr when sr.Grid[sr.Index].Item is string sid => sid,
                    BlockRef br => br.BlockId,
                    _ => null,
                };
                if (set != null) pack.SetStructureSlot(i, set);
            }
        }
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

    void Container(Gui g, Rect2 r, OpenContainer open, ItemView bagView)
    {
        g.Caption(open.Title.ToUpperInvariant(), r.Position, r.Size.X);
        ItemGrid grid = open.Grid;
        grid.Reconcile();
        ItemView view = FilterBar.Build(g, "box", new Rect2(r.Position + new Vector2(0, 30), new Vector2(r.Size.X, FilterBar.Height)));
        int columns = 7;
        float cell = 58;
        int visible = 6;
        IReadOnlyList<int> shown = view.Visible(grid);
        int rows = Math.Max(1, (shown.Count + columns - 1) / columns);
        _containerScroll = Math.Clamp(_containerScroll, 0, Math.Max(0, rows - visible));
        Slots.Grid(g, _game, "box", grid, 0, grid.Count, columns, new Rect2(r.Position + new Vector2(0, 98), new Vector2(columns * (cell + 6), visible * (cell + 6))), cell, _game.Pack.Grid, _containerScroll, view);
        float y = r.Position.Y + 98 + visible * 64 + 8;
        if (rows > visible)
        {
            if (g.Button("box:up", new Rect2(r.Position.X, y, 60, 30), "UP", C.Accent2)) _containerScroll--;
            if (g.Button("box:down", new Rect2(r.Position.X + 70, y, 60, 30), "DOWN", C.Accent2)) _containerScroll++;
        }
        g.Text($"{grid.MassKg:F1} kg   {grid.UsedSu:F1}{(double.IsInfinity(grid.Storage.CapacitySu) ? "" : $" / {grid.Storage.CapacitySu:F0}")} SU", new Vector2(r.Position.X + 150, y + 4), C.Text, 15, UiTheme.Mono);
        y += 44;
        if (g.Button("box:take-all", new Rect2(r.Position.X, y, 220, 44), view.Filtering ? "TAKE SHOWN" : "TAKE ALL", C.Good))
        {
            var only = view.Filtering ? new HashSet<int>(shown) : null;
            Bill moved = ItemGrid.MoveAll(grid, _game.Pack.Grid, true, only == null ? null : only.Contains);
            if (!moved.IsEmpty) _game.Say($"Took {_game.Zone.Data.Items.Describe(moved, 4)}{FilterBar.Shown(view, only?.Count ?? 0)}", Tones.Good);
            else _game.Say(grid.Storage.IsEmpty ? "It's empty" : view.Filtering && shown.Count == 0 ? "Nothing shown to take" : "No room in your bag", Tones.Caution);
        }
        if (g.Button("box:store-all", new Rect2(r.Position.X + 240, y, 220, 44), bagView.Filtering ? "STORE SHOWN" : "STORE ALL", C.Accent2))
        {
            IReadOnlyList<int> bagShown = bagView.Visible(_game.Pack.Grid, PlayerInventory.ToolbarSlots);
            var only = bagView.Filtering ? new HashSet<int>(bagShown) : null;
            Bill moved = ItemGrid.MoveAll(_game.Pack.Grid, grid, false, only == null ? null : only.Contains);
            _game.Say(moved.IsEmpty ? "Nothing in your bag to store (the toolbar stays with you)" : $"Stored {_game.Zone.Data.Items.Describe(moved, 4)}{FilterBar.Shown(bagView, only?.Count ?? 0)}", moved.IsEmpty ? Tones.Caution : Tones.Good);
        }
        if (g.Button("box:close", new Rect2(r.Position.X, y + 56, 120, 34), "CLOSE", C.Danger)) _game.CloseContainer();
    }

    /// <summary>
    /// The block families. In Creative, the build palette: drag one onto your toolbar for a free,
    /// unlimited shortcut. In Survival, a list of what each block is made of and where, how many
    /// you carry and how many the linked structure holds; drag one onto the structure toolbar to
    /// build from that structure's storage. Blocks are crafted in constructors, never conjured here.
    /// </summary>
    void Palette(Gui g, Rect2 r)
    {
        bool creative = _game.Mode == GameMode.Creative;
        Progress p = _game.Progress;
        ItemCatalogue items = _game.Zone.Data.Items;
        PlayerInventory pack = _game.Pack;
        ItemView view = FilterBar.Build(g, "palette", new Rect2(r.Position, new Vector2(r.Size.X, FilterBar.Height)));
        // Creative has every block, the cores too; Survival's cores are items you craft and carry.
        IEnumerable<ItemDef> families = creative
            ? items.Items.Where(i => (i.IsBlock && i.Family != BlockFamily.Deco) || PlayerInventory.CoreItems.Contains(i.Id))
            : items.BlockItems.Where(i => i.Family != BlockFamily.Deco && !PlayerInventory.CoreItems.Contains(i.Id));
        var entries = view.Apply(families, i => pack[i.Id])
            .OrderBy(i => view.Sort == ItemSort.Slot ? (p.ItemAllowed(i) ? 0 : 1) : 0).ToList();
        int perPage = 13;
        _paletteScroll = Math.Clamp(_paletteScroll, 0, Math.Max(0, (entries.Count - 1) / perPage));
        g.Text(creative ? "Drag a block onto your toolbar: Creative places it free and unlimited."
                        : "Blocks are items: craft them in a constructor. Drag one onto the structure toolbar to build from its storage.",
            r.Position + new Vector2(0, FilterBar.Height + 2), C.Dim, 12);
        float top = r.Position.Y + FilterBar.Height + 24;
        for (int i = 0; i < perPage; i++)
        {
            int k = _paletteScroll * perPage + i;
            if (k >= entries.Count) break;
            ItemDef item = entries[k];
            bool allowed = p.ItemAllowed(item);
            var row = new Rect2(r.Position.X, top + i * 40, r.Size.X, 34);
            g.Fill(row, new Color(allowed ? C.Glass : C.Panel, 0.85f));
            g.Fill(new Rect2(row.Position, new Vector2(5, row.Size.Y)), allowed ? (item.SmallGrid ? C.Accent2 : C.Accent) : C.Dim);
            g.Text(item.Name, row.Position + new Vector2(12, 2), allowed || creative ? C.Text : C.Dim, 14);
            string detail = creative ? "free" : $"made from {items.Describe(item.Recipe?.InputsFor(1), 2)}{(allowed ? "" : $" · {p.LockReason(item.Id)}")}";
            g.Text(detail, row.Position + new Vector2(12, 19), allowed ? C.Dim : C.Warn, 11);
            if (!creative)
                g.TextIn($"{pack[item.Id]}{(pack.Linked != null ? $" + {pack.Linked.Count(item.Id)}" : "")}", new Rect2(row.End.X - 110, row.Position.Y + 6, 104, 22), C.Accent2, 14, HorizontalAlignment.Right, UiTheme.Mono);
            g.SetTip(row, () => Tooltips.For(_game, item, 0));
            if (creative || pack.Linked != null)
                g.Draggable($"palette:{item.Id}", row, new BlockRef(item.Id), item.ShortName, item.SmallGrid ? C.Accent2 : C.Accent);
        }
        float y = top + perPage * 40 + 4;
        if (g.Button("palette:up", new Rect2(r.Position.X, y, 60, 30), "UP", C.Accent2, _paletteScroll > 0)) _paletteScroll--;
        if (g.Button("palette:down", new Rect2(r.Position.X + 70, y, 60, 30), "DOWN", C.Accent2, (_paletteScroll + 1) * perPage < entries.Count)) _paletteScroll++;
        if (!creative) g.Text("YOURS + IN THE LINKED STRUCTURE", new Vector2(r.Position.X + 150, y + 6), C.Dim, 12);
    }
}
