using System;
using System.Linq;
using GalHack.Items;
using GalHack.Items.Production;
using Godot;
using Manufacturing.Game;

namespace Manufacturing.Screens;

/// <summary>
/// The blueprint factory's screen: pick a saved blueprint and see its whole bill of materials
/// (refined materials, never ore) against what storage holds, what's missing, and how long it
/// takes. It refuses with the missing list; while it builds, the bar runs and the time counts down.
/// </summary>
public partial class FactoryPage : MachinePage
{
    readonly Workshop _w;
    int _pick;
    FactoryQuote _quote;
    int _quoteKey = int.MinValue;

    public FactoryPage(Workshop w) => _w = w;

    BlueprintFactory F => _w.Factory;
    BlueprintEntry Selected => _w.Blueprints[Wrap(_pick, _w.Blueprints.Count)];

    public override string Prompt => F.Current != null ? $"BUILDING {F.Current.Name.ToUpperInvariant()}" : $"CLICK: BUILD {Selected.Name.ToUpperInvariant()}   WHEEL: BLUEPRINT";

    protected override int Signature() => HashCode.Combine(F.Version, _w.Storage.Version, _pick, _w.Rules.Mode);

    FactoryQuote Quote()
    {
        int key = HashCode.Combine(_w.Storage.Version, _pick, _w.Rules.Mode);
        if (key != _quoteKey || _quote == null)
        {
            _quote = F.Quote(Selected.Name, Selected.Blocks);
            _quoteKey = key;
        }
        return _quote;
    }

    public override string Handle(ScreenKey key)
    {
        switch (key)
        {
            case ScreenKey.Up: _pick--; return null;
            case ScreenKey.Down: _pick++; return null;
            case ScreenKey.Act or ScreenKey.ActMany:
                FactoryStart r = F.Start(Selected.Name, Selected.Blocks);
                return r.Ok ? $"BUILDING {Selected.Name.ToUpperInvariant()}: {Kit.Clock(r.Quote.Seconds)}" : $"REFUSED: {r.Message.ToUpperInvariant()}";
        }
        return null;
    }

    public override void _Draw()
    {
        FactoryQuote q = F.Current ?? Quote();
        this.Block(new Rect2(0, 0, W, H), Kit.Glass);
        this.Header(W, "BLUEPRINT FACTORY", F.Def.Tag, new Color("5fe8ff"));
        string status = F.Current != null ? $"BUILDING {F.Current.Name.ToUpperInvariant()}  -  {Kit.Clock(F.SecondsLeft)} LEFT"
            : q.CanBuild ? "READY TO BUILD" : $"MISSING {q.Missing.Kinds} MATERIALS";
        Color sc = F.Current != null ? Kit.Accent2 : q.CanBuild ? Kit.Good : Kit.Danger;
        this.Strip(W, status, sc, _w.Rules.Mode.ToString().ToUpperInvariant(), _w.Rules.Creative ? Kit.Good : Kit.Dim);

        // Left: blueprints.
        this.Caption(new Vector2(24, 152), "BLUEPRINTS", Kit.Accent);
        float y = 192;
        for (int i = 0; i < _w.Blueprints.Count; i++)
        {
            BlueprintEntry e = _w.Blueprints[i];
            var r = new Rect2(24, y, 380, 76);
            this.Row(r, Kit.Accent, i == Wrap(_pick, _w.Blueprints.Count));
            this.Text(Kit.Label, Kit.SizeSmall, r.Position + new Vector2(20, 8), e.Name.ToUpperInvariant(), Kit.Cream);
            this.Text(Kit.Label, Kit.SizeTiny, r.Position + new Vector2(20, 42), Kit.Fit(Kit.Label, Kit.SizeTiny, $"{e.Structure.Count} BLOCKS  -  {e.Note.ToUpperInvariant()}", 350), Kit.Dim);
            y += 84;
        }
        this.Block(new Rect2(420, 152, 4, 516), Kit.Rule);

        // Middle: the bill of materials against storage.
        this.Caption(new Vector2(440, 152), "BILL OF MATERIALS", Kit.Accent2);
        this.Text(Kit.Label, Kit.SizeTiny, new Vector2(700, 156), "NEED", Kit.Dim);
        this.Text(Kit.Label, Kit.SizeTiny, new Vector2(790, 156), "HAVE", Kit.Dim);
        y = 190;
        foreach (var (id, need) in q.Materials.OrderByDescending(kv => kv.Value).Take(9))
        {
            ItemDef item = _w.Items.Item(id);
            int have = _w.Storage.Count(id);
            bool ok = F.Current != null || _w.Rules.Creative || have >= need;
            var r = new Rect2(440, y, 440, 40);
            this.Block(r, Kit.Panel);
            this.Block(new Rect2(r.Position, new Vector2(8, r.Size.Y)), Kit.Of(item.Rarity));
            this.TextIn(Kit.Label, Kit.SizeSmall, new Rect2(r.Position.X + 18, r.Position.Y, 230, r.Size.Y), Kit.Fit(Kit.Label, Kit.SizeSmall, item.Name.ToUpperInvariant(), 230), Kit.Cream);
            this.TextIn(Kit.Mono, Kit.SizeSmall, new Rect2(r.Position.X + 255, r.Position.Y, 80, r.Size.Y), $"{need}", Kit.Cream);
            this.TextIn(Kit.Mono, Kit.SizeSmall, new Rect2(r.Position.X + 345, r.Position.Y, 90, r.Size.Y), F.Current != null ? "-" : $"{have}", ok ? Kit.Good : Kit.Danger);
            y += 44;
        }
        if (q.Materials.Kinds > 9) this.Text(Kit.Label, Kit.SizeTiny, new Vector2(440, y + 4), $"+{q.Materials.Kinds - 9} MORE", Kit.Dim);
        this.Text(Kit.Label, Kit.SizeTiny, new Vector2(440, 640), "PAID IN REFINED MATERIALS, NEVER ORE", Kit.Dim);

        // Right: time, blocks, missing, progress.
        var panel = new Rect2(900, 152, 356, 516);
        this.Block(panel, Kit.Panel.Darkened(0.25f));
        this.Text(Kit.Label, Kit.SizeTiny, panel.Position + new Vector2(18, 14), "TIME AT THIS FACTORY", Kit.Dim);
        this.Text(Kit.Mono, 48, panel.Position + new Vector2(18, 36), Kit.Clock(F.Current != null ? F.SecondsLeft : q.Seconds), Kit.Cream);
        this.Text(Kit.Label, Kit.SizeTiny, panel.Position + new Vector2(18, 110), $"{q.BlockTotal} BLOCKS, {q.Parts.Total} PARTS MADE HERE", Kit.Dim);
        if (!q.Byproducts.IsEmpty)
            this.Text(Kit.Label, Kit.SizeTiny, panel.Position + new Vector2(18, 138), Kit.Fit(Kit.Label, Kit.SizeTiny, "SPARE BACK TO STORAGE: " + _w.Items.Describe(q.Byproducts, 2).ToUpperInvariant(), 320), Kit.Dim);

        if (F.Current != null)
        {
            this.Caption(panel.Position + new Vector2(18, 190), "BUILDING", Kit.Accent2);
            this.Segments(new Rect2(panel.Position.X + 18, panel.Position.Y + 234, 300, 26), F.Progress, 1, Kit.Accent2, 12, 4);
        }
        else if (!q.Missing.IsEmpty)
        {
            this.Block(new Rect2(panel.Position.X, panel.Position.Y + 186, panel.Size.X, 46), Kit.Danger);
            this.TextIn(Kit.Display, Kit.SizeSmall, new Rect2(panel.Position.X, panel.Position.Y + 186, panel.Size.X, 46), "MISSING", Kit.Glass, padX: 18);
            float my = panel.Position.Y + 244;
            foreach (var (id, n) in q.Missing.OrderByDescending(kv => kv.Value).Take(8))
            {
                this.Text(Kit.Mono, Kit.SizeSmall, new Vector2(panel.Position.X + 18, my), $"{n}", Kit.Danger);
                this.Text(Kit.Label, Kit.SizeSmall, new Vector2(panel.Position.X + 100, my), Kit.Fit(Kit.Label, Kit.SizeSmall, _w.Items.Item(id).Name.ToUpperInvariant(), 240), Kit.Cream);
                my += 36;
            }
        }
        else
        {
            this.Block(new Rect2(panel.Position.X, panel.Position.Y + 186, panel.Size.X, 46), Kit.Good);
            this.TextIn(Kit.Display, Kit.SizeSmall, new Rect2(panel.Position.X, panel.Position.Y + 186, panel.Size.X, 46), _w.Rules.Creative ? "FREE IN CREATIVE" : "ALL IN STORAGE", Kit.Glass, padX: 18);
        }
        if (F.LastFinished != null && F.Current == null)
            this.Text(Kit.Label, Kit.SizeTiny, panel.Position + new Vector2(18, 480), $"LAST BUILT: {F.LastFinished.Name.ToUpperInvariant()}", Kit.Good);
        if (Focused) this.Hints(W, H, Prompt);
    }
}
