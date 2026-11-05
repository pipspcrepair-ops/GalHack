using System;
using System.Collections.Generic;
using System.Linq;
using GalHack.Items;
using GalHack.Items.Production;
using Godot;
using Manufacturing.Game;

namespace Manufacturing.Screens;

/// <summary>
/// The constructor's own screen. Left: what it can make (items above its tier are shown locked).
/// Right: the player's orders, the sub-parts it added by itself (each saying which order it is
/// for), and the ammo restock panel: the toggle, and each gun's ammo against its threshold.
/// Missing raw materials show in red at the bottom.
/// </summary>
public partial class ConstructorPage : MachinePage
{
    static readonly ItemCategory[] Menu = { ItemCategory.Part, ItemCategory.Component, ItemCategory.Ammo, ItemCategory.Consumable, ItemCategory.Refined, ItemCategory.Tool };

    readonly Workshop _w;
    readonly List<ItemDef> _menu;
    int _pick, _ammo;
    bool _restockFocus;

    public ConstructorPage(Workshop w)
    {
        _w = w;
        _menu = w.Items.Items.Where(i => i.Recipe != null && Menu.Contains(i.Category))
            .OrderBy(i => Array.IndexOf(Menu, i.Category)).ThenBy(i => i.Recipe.Tier).ThenBy(i => i.Rarity).ThenBy(i => i.Name).ToList();
        _pick = Math.Max(0, _menu.FindIndex(i => i.Id == "servo"));
    }

    Constructor Fab => _w.Fab;
    public ItemDef Selected => _menu[_pick];

    public override string Prompt => _restockFocus
        ? "WHEEL: AMMO   [ ]: THRESHOLD   R: RESTOCK ON/OFF   TAB: ORDER LIST"
        : $"CLICK: ORDER {Selected.Name.ToUpperInvariant()}   SHIFT: x5   RMB: CANCEL LAST   TAB: AMMO RESTOCK";

    protected override int Signature() => HashCode.Combine(Fab.Version, _w.Storage.Version, _w.Rules.Mode, _pick, _ammo, _restockFocus, Fab.Restock.Version);

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
                else _pick = Wrap(_pick + step, _menu.Count);
                return null;
            case ScreenKey.Toggle:
                Fab.Restock.Enabled = !Fab.Restock.Enabled;
                return Fab.Restock.Enabled ? "AMMO RESTOCK ON" : "AMMO RESTOCK OFF";
            case ScreenKey.Less or ScreenKey.More when ammo.Count > 0:
                string id = ammo[Wrap(_ammo, ammo.Count)];
                int t = Fab.Restock.Threshold(id, _w.Items) + (key == ScreenKey.More ? 1 : -1);
                Fab.Restock.SetThreshold(id, t);
                return $"{_w.Items.Item(id).Name.ToUpperInvariant()}: KEEP {Fab.Restock.Threshold(id, _w.Items)} IN STOCK";
            case ScreenKey.Act or ScreenKey.ActMany when !_restockFocus:
                OrderResult r = Fab.Order(Selected.Id, key == ScreenKey.ActMany ? 5 : 1);
                return r.Ok ? $"ORDERED {r.Job.Count} x {Selected.Name.ToUpperInvariant()}" : r.Message.ToUpperInvariant();
            case ScreenKey.Cancel:
                Job last = Fab.Orders.LastOrDefault();
                return last != null && Fab.Cancel(last) ? $"CANCELLED {last.Item.Name.ToUpperInvariant()}" : null;
        }
        return null;
    }

    public override void _Draw()
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
        if (Focused) this.Hints(W, H, Prompt);
    }

    void DrawMenu(Rect2 area)
    {
        this.Caption(area.Position, $"MAKE  ({_menu.Count(i => Fab.Knows(i))} OF {_menu.Count} KNOWN HERE)", _restockFocus ? Kit.Dim : Kit.Accent);
        const int rows = 8;
        int first = Math.Clamp(_pick - rows / 2, 0, Math.Max(0, _menu.Count - rows));
        float y = area.Position.Y + 40;
        for (int i = first; i < Math.Min(_menu.Count, first + rows); i++)
        {
            ItemDef item = _menu[i];
            bool known = Fab.Knows(item);
            var r = new Rect2(area.Position.X, y, area.Size.X, 48);
            this.Row(r, Kit.Of(item.Rarity), i == _pick && !_restockFocus, !known);
            this.TextIn(Kit.Label, Kit.SizeSmall, new Rect2(r.Position.X + 20, r.Position.Y, 250, r.Size.Y), Kit.Fit(Kit.Label, Kit.SizeSmall, item.Name.ToUpperInvariant(), 240), known ? Kit.Cream : Kit.Dim);
            string right = known ? $"{Fab.RunSeconds(item.Recipe):0.#} S" : _w.Items.Machines.TierName(item.Recipe.Tier).ToUpperInvariant();
            this.TextIn(Kit.Mono, Kit.SizeTiny, r, right, known ? Kit.Dim : Kit.Warn, HorizontalAlignment.Right, 14);
            y += 52;
        }
        // What the selected item is made of.
        ItemDef s = Selected;
        var info = new Rect2(area.Position.X, area.End.Y - 42, area.Size.X, 42);
        this.Block(info, Kit.Panel);
        this.TextIn(Kit.Label, Kit.SizeTiny, info, Kit.Fit(Kit.Label, Kit.SizeTiny, $"{s.Recipe.Count} FROM " + _w.Items.Describe(s.Recipe.InputsFor(1)).ToUpperInvariant(), info.Size.X - 24), Kit.Cream, padX: 12);
    }

    void DrawQueues(Rect2 area)
    {
        this.Caption(area.Position, "YOUR ORDERS", Kit.Accent);
        float y = area.Position.Y + 38;
        if (Fab.Orders.Count == 0) this.Text(Kit.Label, Kit.SizeSmall, new Vector2(area.Position.X, y + 6), "NOTHING ORDERED", Kit.Dim);
        foreach (Job job in Fab.Orders.Take(2))
        {
            JobRow(new Rect2(area.Position.X, y, area.Size.X, 48), job, Kit.Accent);
            y += 54;
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

    void JobRow(Rect2 r, Job job, Color colour)
    {
        this.Block(r, Kit.Panel);
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
            this.Text(Kit.Label, Kit.SizeTiny, cell.Position + new Vector2(12, 6), Kit.Fit(Kit.Label, Kit.SizeTiny, item.Name.ToUpperInvariant(), cell.Size.X - 24), Kit.Cream);
            Color fill = stock >= threshold ? Kit.Good : rs.Enabled ? Kit.Accent2 : Kit.Warn;
            this.Segments(new Rect2(cell.Position.X + 12, cell.Position.Y + 36, cell.Size.X - 44, 16), Math.Min(stock, Math.Max(1, threshold)), Math.Max(1, threshold), fill, 10, 3);
            this.Text(Kit.Mono, Kit.SizeTiny, cell.Position + new Vector2(12, 60), $"{stock} / {threshold}", Kit.Cream);
            this.Text(Kit.Label, Kit.SizeTiny - 4, cell.Position + new Vector2(110, 63), $"BOXES OF {item.Rounds}", Kit.Dim);
        }
    }
}
