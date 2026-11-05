using System;
using System.Collections.Generic;
using System.Linq;
using DiegeticUi.Screens;
using GalHack.Items;
using GalHack.Items.Production;
using Godot;
using Manufacturing.Game;

namespace Manufacturing.Screens;

/// <summary>
/// The constructor's own screen. Left: what it can make (items above its tier are shown locked);
/// click one to order it. Right: the player's orders (each with a cancel key), the sub-parts it
/// added by itself (each saying which order it is for), and the ammo restock panel: the toggle,
/// and each gun's ammo against its threshold with keys to lower or raise it. Missing raw
/// materials show in red at the bottom.
/// </summary>
public partial class ConstructorPage : MachinePage
{
    static readonly ItemCategory[] Menu = { ItemCategory.Part, ItemCategory.Component, ItemCategory.Ammo, ItemCategory.Consumable, ItemCategory.Refined, ItemCategory.Tool };
    const int MenuRows = 8;

    readonly Workshop _w;
    readonly List<ItemDef> _menu;
    int _pick, _top, _ammo;
    bool _restockFocus;

    public ConstructorPage(Workshop w)
    {
        _w = w;
        _menu = w.Items.Items.Where(i => i.Recipe != null && Menu.Contains(i.Category))
            .OrderBy(i => Array.IndexOf(Menu, i.Category)).ThenBy(i => i.Recipe.Tier).ThenBy(i => i.Rarity).ThenBy(i => i.Name).ToList();
        _pick = Math.Max(0, _menu.FindIndex(i => i.Id == "servo"));
        _top = ScrollTo(0, _pick, MenuRows, _menu.Count);
    }

    Constructor Fab => _w.Fab;
    public ItemDef Selected => _menu[_pick];

    /// <summary>The hot spot key of a recipe row, for tests and screenshots that aim at one.</summary>
    public static string RecipeKey(string itemId) => "make:" + itemId;

    /// <summary>Scrolls the recipe list so an item's row is on the glass.</summary>
    public void ShowRecipe(string itemId)
    {
        int i = _menu.FindIndex(m => m.Id == itemId);
        if (i < 0) return;
        _top = ScrollTo(_top, i, MenuRows, _menu.Count);
        Changed();
    }

    public override string Prompt => _restockFocus
        ? "UP / DOWN: AMMO   [ ]: THRESHOLD   R: RESTOCK ON/OFF   TAB: RECIPES"
        : "CLICK A RECIPE: ORDER ONE   SHIFT-CLICK: FIVE   WHEEL: SCROLL   X: CANCEL AN ORDER";

    protected override int Signature() => HashCode.Combine(Fab.Version, _w.Storage.Version, _w.Rules.Mode, _pick, _top, _ammo, _restockFocus, Fab.Restock.Version);

    protected override string Wheel(int step)
    {
        _top = Math.Clamp(_top + step, 0, Math.Max(0, _menu.Count - MenuRows));
        return null;
    }

    protected override string ClickedNothing(SpotClick click) => click.Right ? CancelLast() : null;

    public override string Handle(ScreenKey key)
    {
        IReadOnlyList<string> ammo = Fab.Restock.InUse;
        switch (key)
        {
            case ScreenKey.Tab:
                _restockFocus = !_restockFocus && ammo.Count > 0;
                return null;
            case ScreenKey.Up or ScreenKey.Down:
                int step = key == ScreenKey.Down ? 1 : -1;
                if (_restockFocus) _ammo = Wrap(_ammo + step, ammo.Count);
                else
                {
                    _pick = Wrap(_pick + step, _menu.Count);
                    _top = ScrollTo(_top, _pick, MenuRows, _menu.Count);
                }
                return null;
            case ScreenKey.Toggle:
                return ToggleRestock();
            case ScreenKey.Less or ScreenKey.More when ammo.Count > 0:
                return Threshold(ammo[Wrap(_ammo, ammo.Count)], key == ScreenKey.More ? 1 : -1);
            case ScreenKey.Act or ScreenKey.ActMany when !_restockFocus:
                return Order(Selected, key == ScreenKey.ActMany ? 5 : 1);
            case ScreenKey.Cancel:
                return CancelLast();
        }
        return null;
    }

    string Order(ItemDef item, int count)
    {
        OrderResult r = Fab.Order(item.Id, count);
        return r.Ok ? $"ORDERED {r.Job.Count} x {item.Name.ToUpperInvariant()}" : r.Message.ToUpperInvariant();
    }

    string Cancel(Job job) => Fab.Cancel(job) ? $"CANCELLED {job.Item.Name.ToUpperInvariant()}" : null;

    string CancelLast() => Fab.Orders.LastOrDefault() is { } last ? Cancel(last) : null;

    string ToggleRestock()
    {
        Fab.Restock.Enabled = !Fab.Restock.Enabled;
        return Fab.Restock.Enabled ? "AMMO RESTOCK ON" : "AMMO RESTOCK OFF";
    }

    string Threshold(string ammoId, int step)
    {
        Fab.Restock.SetThreshold(ammoId, Fab.Restock.Threshold(ammoId, _w.Items) + step);
        return $"{_w.Items.Item(ammoId).Name.ToUpperInvariant()}: KEEP {Fab.Restock.Threshold(ammoId, _w.Items)} IN STOCK";
    }

    protected override void Paint()
    {
        this.Block(new Rect2(0, 0, W, H), Kit.Glass);
        this.Header(W, "CONSTRUCTOR", Fab.Def.Tag);
        Bill missing = Fab.Missing;
        string status = Fab.Status;
        Color sc = !missing.IsEmpty ? Kit.Danger : Fab.Busy ? Kit.Accent2 : Kit.Good;
        this.Strip(W, status, sc, $"{Fab.Def.Name.ToUpperInvariant()}  -  {_w.Rules.Mode.ToString().ToUpperInvariant()}", _w.Rules.Creative ? Kit.Good : Kit.Dim);

        DrawMenu(new Rect2(24, 152, 400, 516));
        this.Block(new Rect2(440, 152, 4, 516), Kit.Rule);
        DrawQueues(new Rect2(460, 152, 796, 300));
        DrawRestock(new Rect2(460, 462, 796, 150));

        var foot = new Rect2(460, 622, 796, 46);
        if (!missing.IsEmpty)
        {
            this.Block(foot, Kit.Danger);
            this.TextIn(Kit.Label, Kit.SizeSmall, foot, Kit.Fit(Kit.Label, Kit.SizeSmall, "MISSING  " + _w.Items.Describe(missing, 4).ToUpperInvariant(), foot.Size.X - 40), Kit.Glass, padX: 20);
        }
        else
        {
            this.Block(foot, Kit.Panel);
            this.TextIn(Kit.Label, Kit.SizeSmall, foot, $"PARTS FROM: {_w.Storage.Name}", Kit.Dim, padX: 20);
            this.TextIn(Kit.Mono, Kit.SizeSmall, foot, $"{_w.Storage.UsedSu:0} / {_w.Storage.CapacitySu:0} SU", Kit.Cream, HorizontalAlignment.Right, 20);
        }
    }

    void DrawMenu(Rect2 area)
    {
        this.Caption(area.Position, $"MAKE  ({_menu.Count(i => Fab.Knows(i))} OF {_menu.Count} KNOWN HERE)", _restockFocus ? Kit.Dim : Kit.Accent);
        int maxTop = Math.Max(0, _menu.Count - MenuRows);
        var up = new Rect2(area.End.X - 82, area.Position.Y - 4, 38, 30);
        var down = new Rect2(area.End.X - 38, area.Position.Y - 4, 38, 30);
        this.Arrow(up, true, Spot(up, "scroll-up", _ => Wheel(-1)), _top > 0);
        this.Arrow(down, false, Spot(down, "scroll-down", _ => Wheel(1)), _top < maxTop);

        ItemDef shown = Selected; // the recipe line at the bottom follows the cursor
        float y = area.Position.Y + 40;
        for (int i = _top; i < Math.Min(_menu.Count, _top + MenuRows); i++)
        {
            ItemDef item = _menu[i];
            int index = i;
            bool known = Fab.Knows(item);
            var r = new Rect2(area.Position.X, y, area.Size.X, 48);
            bool hot = Spot(r, RecipeKey(item.Id), click =>
            {
                if (click.Right) return null;
                _pick = index;
                return Order(item, click.Shift ? 5 : 1);
            });
            if (hot) shown = item;
            this.Row(r, Kit.Of(item.Rarity), i == _pick && !_restockFocus, !known, hot);
            this.TextIn(Kit.Label, Kit.SizeSmall, new Rect2(r.Position.X + 20, r.Position.Y, 250, r.Size.Y), Kit.Fit(Kit.Label, Kit.SizeSmall, item.Name.ToUpperInvariant(), 240), known ? Kit.Cream : Kit.Dim);
            string right = known ? $"{Fab.RunSeconds(item.Recipe):0.#} S" : _w.Items.Machines.TierName(item.Recipe.Tier).ToUpperInvariant();
            this.TextIn(Kit.Mono, Kit.SizeTiny, r, right, known ? Kit.Dim : Kit.Warn, HorizontalAlignment.Right, 14);
            y += 52;
        }
        // What the selected (or pointed-at) item is made of.
        var info = new Rect2(area.Position.X, area.End.Y - 42, area.Size.X, 42);
        this.Block(info, Kit.Panel);
        this.TextIn(Kit.Label, Kit.SizeTiny, info, Kit.Fit(Kit.Label, Kit.SizeTiny, $"{shown.Recipe.Count} FROM " + _w.Items.Describe(shown.Recipe.InputsFor(1)).ToUpperInvariant(), info.Size.X - 24), Kit.Cream, padX: 12);
    }

    void DrawQueues(Rect2 area)
    {
        this.Caption(area.Position, "YOUR ORDERS", Kit.Accent);
        float y = area.Position.Y + 38;
        if (Fab.Orders.Count == 0) this.Text(Kit.Label, Kit.SizeSmall, new Vector2(area.Position.X, y + 6), "NOTHING ORDERED", Kit.Dim);
        int n = 0;
        foreach (Job job in Fab.Orders.Take(2))
        {
            var row = new Rect2(area.Position.X, y, area.Size.X, 48);
            // The cancel key first: it sits on top of its row. Right-clicking the row cancels too.
            var x = new Rect2(row.End.X - 46, row.Position.Y + 4, 40, 40);
            this.Key(x, "X", Kit.Danger, Spot(x, $"cancel:{n}", _ => Cancel(job)));
            bool hot = Spot(row, $"order:{n}", click => click.Right ? Cancel(job) : null);
            JobRow(new Rect2(row.Position, row.Size - new Vector2(52, 0)), job, Kit.Accent, hot);
            y += 54;
            n++;
        }
        if (Fab.Orders.Count > 2) this.Text(Kit.Label, Kit.SizeTiny, new Vector2(area.End.X - 120, area.Position.Y + 4), $"+{Fab.Orders.Count - 2} MORE", Kit.Dim);

        y = area.Position.Y + 160;
        this.Caption(new Vector2(area.Position.X, y), "AUTO SUB-PARTS  -  ADDED FOR YOU", Kit.Good);
        y += 38;
        if (Fab.SubParts.Count == 0) this.Text(Kit.Label, Kit.SizeSmall, new Vector2(area.Position.X + 30, y + 6), "NONE NEEDED", Kit.Dim);
        foreach (Job job in Fab.SubParts.Take(2))
        {
            JobRow(new Rect2(area.Position.X + 30, y, area.Size.X - 30, 48), job, Kit.Good);
            this.Block(new Rect2(area.Position.X + 8, y - 6, 4, 30), Kit.Good.Darkened(0.4f));
            this.Block(new Rect2(area.Position.X + 8, y + 22, 22, 4), Kit.Good.Darkened(0.4f));
            y += 54;
        }
        if (Fab.SubParts.Count > 2) this.Text(Kit.Label, Kit.SizeTiny, new Vector2(area.End.X - 120, area.Position.Y + 164), $"+{Fab.SubParts.Count - 2} MORE", Kit.Dim);
    }

    void JobRow(Rect2 r, Job job, Color colour, bool hot = false)
    {
        this.Block(r, hot ? Kit.Panel.Lightened(0.12f) : Kit.Panel);
        this.Block(new Rect2(r.Position, new Vector2(8, r.Size.Y)), colour);
        string name = job.Item.Name.ToUpperInvariant();
        if (job.Feeds != null)
        {
            this.Text(Kit.Label, Kit.SizeSmall, r.Position + new Vector2(20, 2), Kit.Fit(Kit.Label, Kit.SizeSmall, name, 280), Kit.Cream);
            this.Text(Kit.Label, Kit.SizeTiny - 2, r.Position + new Vector2(20, 27), "FOR " + job.Feeds.Item.Name.ToUpperInvariant(), Kit.Dim);
        }
        else this.TextIn(Kit.Label, Kit.SizeSmall, r, Kit.Fit(Kit.Label, Kit.SizeSmall, name, 280), Kit.Cream, padX: 20);
        this.TextIn(Kit.Mono, Kit.SizeSmall, new Rect2(r.Position.X + 300, r.Position.Y, 110, r.Size.Y), $"{job.Done}/{job.Count}", Kit.Cream);
        var right = new Rect2(r.Position.X + 420, r.Position.Y, r.Size.X - 430, r.Size.Y);
        if (job.Running) this.Segments(new Rect2(right.Position.X, right.Position.Y + 14, right.Size.X - 20, 20), job.Progress, 1, colour, 12, 4);
        else
        {
            bool waiting = job.Recipe.Inputs.Any(i => Fab.Missing[i.Item.Id] > 0);
            (string text, Color c) = waiting ? ("WAITING ON ORE", Kit.Danger) : Fab.CanStart(job) ? ("NEXT", Kit.Accent2) : ("QUEUED", Kit.Dim);
            this.TextIn(Kit.Label, Kit.SizeTiny, right, text, c, HorizontalAlignment.Right, 14);
        }
    }

    void DrawRestock(Rect2 area)
    {
        AmmoRestock rs = Fab.Restock;
        this.Block(area, Kit.Panel.Darkened(0.25f));
        if (_restockFocus) this.Frame(area, Kit.Accent2, 3);

        // The toggle: lamp and caption, one switch.
        var toggle = new Rect2(area.Position.X + 6, area.Position.Y + 6, 326, 44);
        if (Spot(toggle, "restock", _ => ToggleRestock()))
        {
            this.Block(toggle, Kit.Panel.Lightened(0.12f));
            this.Frame(toggle, Kit.Cream, 2);
        }
        this.Lamp(area.Position + new Vector2(28, 28), 12, Kit.Good, rs.Enabled);
        this.Text(Kit.Label, Kit.SizeSmall, area.Position + new Vector2(52, 14), rs.Enabled ? "AUTO-RESTOCK AMMO: ON" : "AUTO-RESTOCK AMMO: OFF", rs.Enabled ? Kit.Good : Kit.Dim);
        Color sc = rs.State == RestockState.Paused || rs.State == RestockState.NeedsTier ? Kit.Danger : rs.State == RestockState.Restocking ? Kit.Accent2 : Kit.Dim;
        this.TextIn(Kit.Label, Kit.SizeTiny, new Rect2(area.Position.X + 340, area.Position.Y + 8, area.Size.X - 350, 32),
            Kit.Fit(Kit.Label, Kit.SizeTiny, rs.Status.ToUpperInvariant(), area.Size.X - 370), sc, HorizontalAlignment.Right, 10);

        IReadOnlyList<string> ammo = rs.InUse;
        if (ammo.Count == 0)
        {
            this.Text(Kit.Label, Kit.SizeSmall, area.Position + new Vector2(24, 64), "NO GUNS ABOARD", Kit.Dim);
            return;
        }
        float colW = (area.Size.X - 24) / Math.Max(3, ammo.Count);
        for (int i = 0; i < ammo.Count; i++)
        {
            ItemDef item = _w.Items.Item(ammo[i]);
            int stock = _w.Storage.Count(item.Id), threshold = rs.Threshold(item.Id, _w.Items);
            var cell = new Rect2(area.Position.X + 12 + i * colW, area.Position.Y + 50, colW - 12, 90);
            bool sel = _restockFocus && i == Wrap(_ammo, ammo.Count);
            this.Block(cell, sel ? Kit.Panel.Lightened(0.12f) : Kit.Panel);
            if (sel) this.Frame(cell, Kit.Accent2, 3);
            var less = new Rect2(cell.End.X - 76, cell.Position.Y + 4, 32, 28);
            var more = new Rect2(cell.End.X - 38, cell.Position.Y + 4, 32, 28);
            this.Key(less, "-", Kit.Accent2, Spot(less, $"less:{item.Id}", _ => Threshold(item.Id, -1)));
            this.Key(more, "+", Kit.Accent2, Spot(more, $"more:{item.Id}", _ => Threshold(item.Id, 1)));
            this.Text(Kit.Label, Kit.SizeTiny, cell.Position + new Vector2(12, 6), Kit.Fit(Kit.Label, Kit.SizeTiny, item.Name.ToUpperInvariant(), cell.Size.X - 104), Kit.Cream);
            Color fill = stock >= threshold ? Kit.Good : rs.Enabled ? Kit.Accent2 : Kit.Warn;
            this.Segments(new Rect2(cell.Position.X + 12, cell.Position.Y + 38, cell.Size.X - 44, 16), Math.Min(stock, Math.Max(1, threshold)), Math.Max(1, threshold), fill, 10, 3);
            this.Text(Kit.Mono, Kit.SizeTiny, cell.Position + new Vector2(12, 60), $"{stock} / {threshold}", Kit.Cream);
            this.Text(Kit.Label, Kit.SizeTiny - 4, cell.Position + new Vector2(110, 63), $"BOXES OF {item.Rounds}", Kit.Dim);
        }
    }
}
