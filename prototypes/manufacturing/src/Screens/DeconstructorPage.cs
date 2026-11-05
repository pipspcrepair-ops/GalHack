using System;
using System.Collections.Generic;
using System.Linq;
using GalHack.Items;
using GalHack.Items.Production;
using Godot;
using Manufacturing.Game;

namespace Manufacturing.Screens;

/// <summary>
/// The deconstructor's screen. Left: what's in storage that can be broken down, with the wreck
/// on the salvage pad at the top. Right: what the selected item breaks into at the return rate,
/// the load in progress, and the fractions the machine is carrying towards the next whole part.
/// </summary>
public partial class DeconstructorPage : MachinePage
{
    const string WreckEntry = "\u0001wreck";
    readonly Workshop _w;
    int _pick;

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

    public override string Prompt => Selected == WreckEntry ? "CLICK: SALVAGE THE WRECK ON THE PAD   WHEEL: SELECT"
        : Selected != null ? $"CLICK: BREAK DOWN ONE {_w.Items.Item(Selected).Name.ToUpperInvariant()}   SHIFT: x5   WHEEL: SELECT" : "NOTHING IN STORAGE TO BREAK DOWN";

    protected override int Signature() => HashCode.Combine(D.Version, _w.Storage.Version, _pick, _w.Wreck?.Version ?? 0, _w.SalvageLeft, _w.Rules.Mode);

    public override string Handle(ScreenKey key)
    {
        switch (key)
        {
            case ScreenKey.Up: _pick--; return null;
            case ScreenKey.Down: _pick++; return null;
            case ScreenKey.Act or ScreenKey.ActMany:
                string s = Selected;
                if (s == null) return "NOTHING TO BREAK DOWN";
                if (s == WreckEntry) return _w.StartSalvage() ? null : "ALREADY SALVAGING";
                int n = key == ScreenKey.ActMany ? Math.Min(5, Math.Max(1, _w.Storage.Count(s))) : 1;
                DeconResult r = D.BreakItems(s, n);
                return r.Ok ? $"BREAKING DOWN {r.Job.Label.ToUpperInvariant()}" : r.Message.ToUpperInvariant();
        }
        return null;
    }

    public override void _Draw()
    {
        this.Block(new Rect2(0, 0, W, H), Kit.Glass);
        this.Header(W, "DECONSTRUCTOR", D.Def.Tag, Kit.Good);
        this.Strip(W, D.Status, D.Current != null ? Kit.Accent2 : Kit.Good, $"RETURNS {D.ReturnRate:P0}  -  {_w.Rules.Mode.ToString().ToUpperInvariant()}");

        // Left: what can go in.
        List<string> entries = Entries();
        this.Caption(new Vector2(24, 152), "BREAK DOWN", Kit.Good);
        int pick = Wrap(_pick, entries.Count);
        const int rows = 9;
        int first = Math.Clamp(pick - rows / 2, 0, Math.Max(0, entries.Count - rows));
        float y = 192;
        if (entries.Count == 0) this.Text(Kit.Label, Kit.SizeSmall, new Vector2(24, y + 8), "STORAGE HOLDS NOTHING TO BREAK DOWN", Kit.Dim);
        for (int i = first; i < Math.Min(entries.Count, first + rows); i++)
        {
            var r = new Rect2(24, y, 400, 48);
            if (entries[i] == WreckEntry)
            {
                this.Row(r, Kit.Danger, i == pick);
                this.TextIn(Kit.Label, Kit.SizeSmall, r, _w.SalvageLeft > 0 ? $"SALVAGING WRECK ({_w.SalvageLeft} LEFT)" : $"WRECK ON THE PAD ({_w.Wreck.Count} BLOCKS)", Kit.Cream, padX: 20);
            }
            else
            {
                ItemDef item = _w.Items.Item(entries[i]);
                this.Row(r, Kit.Of(item.Rarity), i == pick);
                this.TextIn(Kit.Label, Kit.SizeSmall, new Rect2(r.Position.X + 20, r.Position.Y, 300, r.Size.Y), Kit.Fit(Kit.Label, Kit.SizeSmall, item.Name.ToUpperInvariant(), 290), Kit.Cream);
                this.TextIn(Kit.Mono, Kit.SizeSmall, r, $"{_w.Storage.Count(item.Id)}", Kit.Dim, HorizontalAlignment.Right, 14);
            }
            y += 52;
        }
        this.Block(new Rect2(440, 152, 4, 516), Kit.Rule);

        // Right top: what the selection breaks into.
        string sel = entries.Count > 0 ? entries[pick] : null;
        this.Caption(new Vector2(460, 152), "BREAKS INTO", Kit.Accent2);
        IReadOnlyDictionary<string, double> returns = sel == null ? new Dictionary<string, double>()
            : sel == WreckEntry ? D.ReturnsOfBlocks(WreckBill()) : D.ReturnsOf(_w.Items.Item(sel), 1);
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
        if (sel != null && sel != WreckEntry)
            this.Text(Kit.Label, Kit.SizeTiny, new Vector2(460, y + 2), $"ONE {_w.Items.Item(sel).Name.ToUpperInvariant()} WAS MADE FROM " + _w.Items.Describe(_w.Items.Item(sel).Recipe.InputsFor(1)).ToUpperInvariant(), Kit.Dim);

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
        if (Focused) this.Hints(W, H, Prompt);
    }

    Bill WreckBill()
    {
        var bill = new Bill();
        if (_w.Wreck == null) return bill;
        foreach (var (_, b) in _w.Wreck.Blocks())
            if (_w.Items.Blocks.TryGet(_w.Wreck.TypeOf(b).Id, out BlockBill bb)) bill.Add(bb.For(_w.Wreck.Grid.Size == GalHack.Structures.GridSize.Small));
        return bill;
    }
}
