using System;
using System.Collections.Generic;
using System.Linq;
using GalHack.Items;
using GalHack.Items.Bridge;
using GalHack.Items.Production;
using Godot;
using Manufacturing.Game;

namespace Manufacturing.Screens;

/// <summary>
/// The deconstructor's screen. Left: what's in storage that can be broken down, with the wreck
/// on the salvage pad at the top; click one to break it down. Right: what the entry under the
/// cursor (or the selected one) breaks into at the return rate, the load in progress, and the
/// fractions the machine is carrying towards the next whole part.
/// </summary>
public partial class DeconstructorPage : MachinePage
{
    const string WreckEntry = "\u0001wreck";
    const int Rows = 9;
    readonly Workshop _w;
    int _pick, _top;

    public DeconstructorPage(Workshop w) => _w = w;

    Deconstructor D => _w.Decon;

    List<string> Entries()
    {
        var list = new List<string>();
        if (_w.Wreck != null && _w.Wreck.Count > 0) list.Add(WreckEntry);
        list.AddRange(_w.Storage.Stacks.Where(s => D.WhyNot(s.Item) == null).Select(s => s.Item.Id));
        return list;
    }

    string Selected
    {
        get
        {
            List<string> e = Entries();
            return e.Count == 0 ? null : e[Wrap(_pick, e.Count)];
        }
    }

    public override string Prompt => Entries().Count == 0 ? "NOTHING IN STORAGE TO BREAK DOWN"
        : "CLICK: BREAK DOWN ONE (OR SALVAGE THE WRECK)   SHIFT-CLICK: FIVE   WHEEL: SCROLL";

    protected override int Signature() => HashCode.Combine(D.Version, _w.Storage.Version, _pick, _top, _w.Wreck?.Version ?? 0, _w.SalvageLeft, _w.Rules.Mode);

    protected override string Wheel(int step)
    {
        _top = Math.Clamp(_top + step, 0, Math.Max(0, Entries().Count - Rows));
        return null;
    }

    public override string Handle(ScreenKey key)
    {
        switch (key)
        {
            case ScreenKey.Up or ScreenKey.Down:
                int count = Entries().Count;
                _pick = Wrap(_pick + (key == ScreenKey.Down ? 1 : -1), count);
                _top = ScrollTo(_top, _pick, Rows, count);
                return null;
            case ScreenKey.Act or ScreenKey.ActMany:
                return BreakDown(Selected, key == ScreenKey.ActMany);
        }
        return null;
    }

    string BreakDown(string entry, bool five)
    {
        if (entry == null) return "NOTHING TO BREAK DOWN";
        if (entry == WreckEntry) return _w.StartSalvage() ? null : "ALREADY SALVAGING";
        int n = five ? Math.Min(5, Math.Max(1, _w.Storage.Count(entry))) : 1;
        DeconResult r = D.BreakItems(entry, n);
        return r.Ok ? $"BREAKING DOWN {r.Job.Label.ToUpperInvariant()}" : r.Message.ToUpperInvariant();
    }

    protected override void Paint()
    {
        this.Block(new Rect2(0, 0, W, H), Kit.Glass);
        this.Header(W, "DECONSTRUCTOR", D.Def.Tag, Kit.Good);
        this.Strip(W, D.Status, D.Current != null ? Kit.Accent2 : Kit.Good, $"RETURNS {D.ReturnRate:P0}  -  {_w.Rules.Mode.ToString().ToUpperInvariant()}");

        // Left: what can go in.
        List<string> entries = Entries();
        this.Caption(new Vector2(24, 152), "BREAK DOWN", Kit.Good);
        int pick = Wrap(_pick, entries.Count);
        int top = Math.Clamp(_top, 0, Math.Max(0, entries.Count - Rows));
        var up = new Rect2(424 - 82, 148, 38, 30);
        var down = new Rect2(424 - 38, 148, 38, 30);
        this.Arrow(up, true, Spot(up, "scroll-up", _ => Wheel(-1)), top > 0);
        this.Arrow(down, false, Spot(down, "scroll-down", _ => Wheel(1)), top < entries.Count - Rows);

        string shown = entries.Count > 0 ? entries[pick] : null; // the right side follows the cursor
        float y = 192;
        if (entries.Count == 0) this.Text(Kit.Label, Kit.SizeSmall, new Vector2(24, y + 8), "STORAGE HOLDS NOTHING TO BREAK DOWN", Kit.Dim);
        for (int i = top; i < Math.Min(entries.Count, top + Rows); i++)
        {
            string entry = entries[i];
            int index = i;
            var r = new Rect2(24, y, 400, 48);
            bool hot = Spot(r, "entry:" + (entry == WreckEntry ? "wreck" : entry), click =>
            {
                if (click.Right) return null;
                _pick = index;
                return BreakDown(entry, click.Shift);
            });
            if (hot) shown = entry;
            if (entry == WreckEntry)
            {
                this.Row(r, Kit.Danger, i == pick, hot: hot);
                this.TextIn(Kit.Label, Kit.SizeSmall, r, _w.SalvageLeft > 0 ? $"SALVAGING WRECK ({_w.SalvageLeft} LEFT)" : $"WRECK ON THE PAD ({_w.Wreck.Count} BLOCKS)", Kit.Cream, padX: 20);
            }
            else
            {
                ItemDef item = _w.Items.Item(entry);
                this.Row(r, Kit.Of(item.Rarity), i == pick, hot: hot);
                this.TextIn(Kit.Label, Kit.SizeSmall, new Rect2(r.Position.X + 20, r.Position.Y, 300, r.Size.Y), Kit.Fit(Kit.Label, Kit.SizeSmall, item.Name.ToUpperInvariant(), 290), Kit.Cream);
                this.TextIn(Kit.Mono, Kit.SizeSmall, r, $"{_w.Storage.Count(item.Id)}", Kit.Dim, HorizontalAlignment.Right, 14);
            }
            y += 52;
        }
        this.Block(new Rect2(440, 152, 4, 516), Kit.Rule);

        // Right top: what the entry breaks into.
        this.Caption(new Vector2(460, 152), "BREAKS INTO", Kit.Accent2);
        IReadOnlyDictionary<string, double> returns = shown == null ? new Dictionary<string, double>()
            : shown == WreckEntry ? D.ReturnsOfBlocks(WreckBill()) : D.ReturnsOf(_w.Items.Item(shown), 1);
        y = 190;
        foreach (var (id, amount) in returns.OrderByDescending(kv => kv.Value).Take(5))
        {
            ItemDef item = _w.Items.Item(id);
            var r = new Rect2(460, y, 796, 40);
            this.Block(r, Kit.Panel);
            this.Block(new Rect2(r.Position, new Vector2(8, r.Size.Y)), Kit.Of(item.Rarity));
            this.TextIn(Kit.Mono, Kit.SizeSmall, new Rect2(r.Position.X + 20, r.Position.Y, 110, r.Size.Y), Kit.Amount(amount), Kit.Cream);
            this.TextIn(Kit.Label, Kit.SizeSmall, new Rect2(r.Position.X + 130, r.Position.Y, 460, r.Size.Y), item.Name.ToUpperInvariant(), Kit.Cream);
            double carried = D.Carry.GetValueOrDefault(id);
            int now = (int)Math.Floor(carried + amount + 1e-9);
            this.TextIn(Kit.Label, Kit.SizeTiny, r, now >= 1 ? $"{now} NOW" : "CARRIED OVER", now >= 1 ? Kit.Good : Kit.Dim, HorizontalAlignment.Right, 16);
            y += 44;
        }
        if (shown != null && shown != WreckEntry)
            this.Text(Kit.Label, Kit.SizeTiny, new Vector2(460, y + 2), $"ONE {_w.Items.Item(shown).Name.ToUpperInvariant()} WAS MADE FROM " + _w.Items.Describe(_w.Items.Item(shown).Recipe.InputsFor(1)).ToUpperInvariant(), Kit.Dim);

        // Right bottom: the load in progress and the carry.
        var job = new Rect2(460, 470, 796, 72);
        this.Block(job, Kit.Panel);
        if (D.Current != null)
        {
            this.Text(Kit.Label, Kit.SizeSmall, job.Position + new Vector2(20, 8), Kit.Fit(Kit.Label, Kit.SizeSmall, D.Current.Label.ToUpperInvariant(), 520), Kit.Cream);
            this.Segments(new Rect2(job.Position.X + 20, job.Position.Y + 42, 560, 18), D.Current.Progress, 1, Kit.Good, 16, 4);
            this.TextIn(Kit.Mono, Kit.SizeSmall, job, $"+{D.Queue.Count() - 1} QUEUED", Kit.Dim, HorizontalAlignment.Right, 20);
        }
        else this.TextIn(Kit.Label, Kit.SizeSmall, job, "HOPPER EMPTY", Kit.Dim, padX: 20);

        var carry = new Rect2(460, 552, 796, 116);
        this.Block(carry, Kit.Panel.Darkened(0.25f));
        this.Text(Kit.Label, Kit.SizeTiny, carry.Position + new Vector2(16, 8), "CARRIED IN THE MACHINE (PAID OUT AS IT REACHES A WHOLE PART)", Kit.Dim);
        string c = D.Carry.Count == 0 ? "NOTHING" : string.Join("   ", D.Carry.OrderByDescending(kv => kv.Value).Take(5).Select(kv => $"{Kit.Amount(kv.Value)} {_w.Items.Item(kv.Key).ShortName.ToUpperInvariant()}"));
        this.Text(Kit.Mono, Kit.SizeSmall, carry.Position + new Vector2(16, 40), Kit.Fit(Kit.Mono, Kit.SizeSmall, c, carry.Size.X - 32), Kit.Cream);
        this.Text(Kit.Label, Kit.SizeTiny, carry.Position + new Vector2(16, 78), $"RETURNED SO FAR: {D.Returned.Total} PARTS", Kit.Dim);
    }

    Bill WreckBill()
    {
        var bill = new Bill();
        if (_w.Wreck == null) return bill;
        foreach (var (_, b) in _w.Wreck.Blocks())
            if (BuildingBridge.Priced(_w.Items, _w.Wreck, b)) bill.Add(BuildingBridge.BillOf(_w.Items, _w.Wreck, b));
        return bill;
    }
}
