using System;
using System.Linq;
using GalHack.Items;
using Godot;
using Manufacturing.Game;

namespace Manufacturing.Screens;

/// <summary>
/// A container's screen: the structure's one shared storage, every stack on a grid of tiles in
/// its rarity colour, the capacity bar, and any spill crates waiting to be emptied.
/// </summary>
public partial class StoragePage : MachinePage
{
    const int Cols = 6, Rows = 5;
    readonly Workshop _w;
    int _page;

    public StoragePage(Workshop w) => _w = w;

    public override string Prompt => "WHEEL: SCROLL   G: GATHER ORE   F: TEST-FIRE THE GUNS   F2: SURVIVAL / CREATIVE";

    protected override int Signature() => HashCode.Combine(_w.Storage.Version, _w.Crates.Version, _page, _w.Rules.Mode);

    public override string Handle(ScreenKey key)
    {
        int pages = Math.Max(1, (int)Math.Ceiling(_w.Storage.Stacks.Count() / (double)(Cols * Rows)));
        if (key == ScreenKey.Down) _page = Wrap(_page + 1, pages);
        if (key == ScreenKey.Up) _page = Wrap(_page - 1, pages);
        return null;
    }

    public override void _Draw()
    {
        SharedStorage s = _w.Storage;
        this.Block(new Rect2(0, 0, W, H), Kit.Glass);
        this.Header(W, "SHARED STORAGE", "BASE", Kit.Accent2);
        double load = s.CapacitySu <= 0 ? 1 : s.UsedSu / s.CapacitySu;
        this.Strip(W, $"{s.UsedSu:0} / {s.CapacitySu:0} SU  -  {s.MassKg / 1000:0.0} T", load > 0.98 ? Kit.Danger : load > 0.85 ? Kit.Warn : Kit.Good,
            _w.Rules.Mode.ToString().ToUpperInvariant(), _w.Rules.Creative ? Kit.Good : Kit.Dim);
        this.Segments(new Rect2(24, 150, W - 48, 18), s.UsedSu, Math.Max(1, s.CapacitySu), load > 0.98 ? Kit.Danger : Kit.Accent2, 48, 3);

        var stacks = s.Stacks.ToList();
        int pages = Math.Max(1, (int)Math.Ceiling(stacks.Count / (double)(Cols * Rows)));
        int page = Wrap(_page, pages);
        const float tw = 200, th = 80, gap = 10, x0 = 24, y0 = 184;
        int i = 0;
        foreach (var (item, count) in stacks.Skip(page * Cols * Rows).Take(Cols * Rows))
        {
            float x = x0 + i % Cols * (tw + gap), y = y0 + i / Cols * (th + gap);
            var r = new Rect2(x, y, tw, th);
            this.Block(r, Kit.Panel);
            this.Block(new Rect2(x, y, tw, 8), Kit.Of(item.Rarity));
            this.Text(Kit.Label, Kit.SizeTiny, new Vector2(x + 12, y + 16), Kit.Fit(Kit.Label, Kit.SizeTiny, item.ShortName.ToUpperInvariant(), tw - 24), Kit.Cream);
            this.Text(Kit.Mono, Kit.SizeSmall + 4, new Vector2(x + 12, y + 42), $"{count}", Kit.Cream);
            this.TextIn(Kit.Label, Kit.SizeTiny - 4, new Rect2(x, y + 44, tw - 10, 26), item.Category.ToString().ToUpperInvariant(), Kit.Dim, HorizontalAlignment.Right);
            i++;
        }
        if (stacks.Count == 0) this.Text(Kit.Label, Kit.SizeLabel, new Vector2(x0, y0 + 20), "EMPTY", Kit.Dim);

        var foot = new Rect2(24, 640, W - 48, 40);
        int crates = _w.Crates.Crates.Count;
        this.Block(foot, crates > 0 ? Kit.Warn : Kit.Panel);
        this.TextIn(Kit.Label, Kit.SizeSmall, foot, crates > 0 ? $"{crates} SPILL CRATE{(crates > 1 ? "S" : "")} NEARBY: {_w.Crates.Total.Total} ITEMS. CRATES NEVER DESPAWN" : $"{stacks.Count} KINDS OF ITEM",
            crates > 0 ? Kit.Glass : Kit.Dim, padX: 16);
        this.TextIn(Kit.Mono, Kit.SizeSmall, foot, $"PAGE {page + 1}/{pages}", crates > 0 ? Kit.Glass : Kit.Dim, HorizontalAlignment.Right, 16);
        if (Focused) this.Hints(W, H, Prompt);
    }
}
