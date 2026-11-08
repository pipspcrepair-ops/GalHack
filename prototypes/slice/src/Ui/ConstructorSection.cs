using System;
using System.Collections.Generic;
using System.Linq;
using DiegeticUi.Theme;
using GalHack.Items;
using GalHack.Items.Production;
using Godot;
using Slice.Items;
using Palette = DiegeticUi.Theme.Palette;

namespace Slice.Ui;

/// <summary>
/// One constructor's manufacturing panel (shared/Items' constructor, as the manufacturing
/// prototype's screens read it): what it can make, each with its inputs' icons and counts (see
/// <see cref="Ingredients"/>), with a filter bar over the list (text, categories, sort) and a
/// tooltip on every row, ordered 1, 5 or 10 at a time; your orders with
/// the sub-parts it planned by itself under each; exactly what is stopping it (a material short,
/// a part a bigger constructor makes, a lock); and its ammo restock lane. Things the tech tree
/// hasn't unlocked, or that need a bigger constructor, are greyed out with the reason. The same
/// panel serves the suit fabricator, a placed survival constructor and every constructor on a
/// structure. On an in-world screen, which has no keyboard, the filter's text is read-only.
/// </summary>
public sealed class ConstructorSection : IMenuSection
{
    static Palette C => UiTheme.Colors;
    readonly Game.Game _game;
    readonly Func<Constructor> _machine;
    readonly string _title, _key;
    int _scroll;
    string _said = "";

    public ConstructorSection(Game.Game game, Func<Constructor> machine, string title, string key)
    {
        _game = game;
        _machine = machine;
        _title = title;
        _key = key;
    }

    public Vector2 PreferredSize => new(520, 740);

    /// <summary>What a constructor lists: only what it knows and the player has unlocked (cores, devices and blocks first, deco last).</summary>
    static IEnumerable<ItemDef> Recipes(Constructor c, Progress p) =>
        c.Items.Items.Where(i => c.Knows(i) && p.ItemAllowed(i) && !i.IsDecoPiece // pieces are placed from the universal deco block, never crafted one by one
                && !(c.Def.Tier <= 0 && ItemView.GroupOf(i) == ItemGroup.Deco)) // survival-tier machines and the suit fabricator never make deco
            .OrderBy(i => !c.Knows(i))
            .ThenBy(i => i.Recipe.Inputs.Count > 0) // anything free (the survival tool) first
            .ThenBy(i => Rank(i))
            .ThenBy(i => i.Recipe.Tier)
            .ThenBy(i => i.Name, StringComparer.Ordinal);

    static int Rank(ItemDef i) => ItemView.GroupOf(i) switch
    {
        ItemGroup.Devices => 0,
        ItemGroup.Blocks => 1,
        ItemGroup.Food or ItemGroup.Ammo or ItemGroup.Other => 2,
        ItemGroup.Parts or ItemGroup.Materials => 3,
        _ => 4,
    };

    /// <summary>A recipe row's height and the gap under it.</summary>
    public const float RowH = 30, RowGap = 2;

    /// <summary>The room the orders and what blocks them keep under the list.</summary>
    const float QueueH = 200;

    public void Build(Gui g, Rect2 area)
    {
        Constructor c = _machine();
        Vector2 o = area.Position;
        g.Caption(_title, o, area.Size.X);
        if (c == null)
        {
            g.Text("NONE", o + new Vector2(0, 40), C.Dim, 16);
            return;
        }
        Progress p = _game.Progress;
        g.Text($"{c.Def.Tag}  ×{c.Def.Speed:0.##}{(_game.CraftTimeScale != 1 ? $"  TEST ×{_game.CraftTimeScale:0}" : "")}", o + new Vector2(0, 30), C.Dim, 13, UiTheme.Mono);
        g.TextIn(c.Status, new Rect2(o.X + 200, o.Y + 28, area.Size.X - 200, 20), c.Current != null ? C.Good : C.Accent2, 14, HorizontalAlignment.Right, UiTheme.Mono);

        // ---- What it can make: a dense list, one line a recipe ------------------------------------
        ItemView view = FilterBar.Build(g, $"recipes:{_key}", new Rect2(o.X, o.Y + 54, area.Size.X, FilterBar.Height), readOnly: _key == "screen");
        var list = view.Apply(Recipes(c, p), i => c.Storage.Count(i.Id));
        if (view.Sort == ItemSort.Slot) list = list.OrderBy(i => !c.Knows(i)).ThenBy(i => !p.ItemAllowed(i)).ToList();
        float listTop = o.Y + 54 + FilterBar.Height;
        int perPage = Math.Max(3, (int)((area.End.Y - QueueH - 40 - listTop) / (RowH + RowGap)));
        _scroll = Math.Clamp(_scroll, 0, Math.Max(0, (list.Count - 1) / perPage));
        float y = listTop;
        const float ButtonW = 42, TimeW = 44;
        // The inputs' slots line up down the list; the name takes what they leave (narrow on an in-world screen).
        float slot = Ingredients.Slot(RowH, list);
        float room = area.Size.X - (RowH + 8) - 6 - TimeW - 6 - 3 * (ButtonW + 3);
        float nameW = Math.Clamp(room - Ingredients.Room(slot, list), 130, 220);
        for (int i = 0; i < perPage; i++)
        {
            int k = _scroll * perPage + i;
            if (k >= list.Count) break;
            ItemDef item = list[k];
            bool known = c.Knows(item), allowed = p.ItemAllowed(item);
            var row = new Rect2(o.X, y, area.Size.X, RowH);
            g.Fill(row, g.Back(new Color(known && allowed ? C.Panel : C.Glass, 0.75f)));
            g.Fill(new Rect2(row.Position, new Vector2(4, row.Size.Y)), new Color(Rarities.Color(item.Rarity) << 8 | 0xff));
            if (ItemIcons.Shared?.Get(item) is Texture2D icon)
                g.Image(icon, new Rect2(row.Position + new Vector2(7, 2), new Vector2(RowH - 4, RowH - 4)), new Color(1, 1, 1, known && allowed ? 1f : 0.45f));
            float x = row.Position.X + RowH + 8;
            g.TextIn(HudLayout.Fit(UiTheme.Label, 14, $"{item.Name}{(item.Recipe.Count > 1 ? $" ×{item.Recipe.Count}" : "")}", nameW), new Rect2(x, y, nameW, RowH), known && allowed ? C.Text : C.Dim, 14);
            x += nameW + 6;
            float buttons = row.End.X - 3 * (ButtonW + 3);
            float inputsW = buttons - TimeW - 6 - x;
            // The row's tooltip first: an input's own, over its icon, wins.
            g.SetTip(new Rect2(row.Position, new Vector2(buttons - row.Position.X, row.Size.Y)), () => Tooltips.For(_game, item, 0));
            if (!known || !allowed)
                g.TextIn(HudLayout.Fit(UiTheme.Label, 12, !known ? $"{c.Items.Machines.TierName(item.Recipe.Tier).ToUpperInvariant()} CONSTRUCTOR" : $"LV {p.Node(item.Id)?.Level}", inputsW), new Rect2(x, y, inputsW, RowH), C.Warn, 12);
            else Ingredients.Draw(g, _game, new Rect2(x, y, inputsW, RowH), item.Recipe.Inputs, slot, input => c.Storage.Count(input.Id), c.Knows);
            g.TextIn($"{c.RunSeconds(item.Recipe):0.#}s", new Rect2(buttons - TimeW - 4, y, TimeW, RowH), C.Dim, 12, HorizontalAlignment.Right, UiTheme.Mono);
            int[] counts = { 1, 5, 10 };
            for (int n = 0; n < counts.Length; n++)
            {
                var b = new Rect2(buttons + n * (ButtonW + 3), row.Position.Y + 2, ButtonW, RowH - 4);
                if (g.Button($"order:{_key}:{item.Id}:{counts[n]}", b, $"+{counts[n]}", C.Accent, known && allowed, size: 13))
                {
                    OrderResult r = c.Order(item.Id, counts[n]);
                    _said = r.Ok ? "" : r.Message;
                    g.Sound = r.Ok ? "queued" : "refused";
                }
            }
            y += RowH + RowGap;
        }
        y = listTop + perPage * (RowH + RowGap) + 2;
        int pages = Math.Max(1, (list.Count + perPage - 1) / perPage);
        {
            if (g.Button($"{_key}:up", new Rect2(o.X, y, 70, 26), "UP", C.Accent2, _scroll > 0, size: 13)) _scroll--;
            if (g.Button($"{_key}:down", new Rect2(o.X + 76, y, 70, 26), "DOWN", C.Accent2, (_scroll + 1) * perPage < list.Count, size: 13)) _scroll++;
            if (pages > 1) g.Text($"{_scroll + 1}/{pages}", new Vector2(o.X + 156, y + 4), C.Dim, 14, UiTheme.Mono);
        }
        if (_said != "") g.TextIn(HudLayout.Fit(UiTheme.Label, 13, _said, area.Size.X - 220), new Rect2(o.X + 210, y, area.Size.X - 210, 26), C.Warn, 13);
        y += 34;
        // ---- Queues -----------------------------------------------------------------------------
        g.Caption("ORDERS", new Vector2(o.X, y), area.Size.X, C.Accent2);
        y += 32;
        // Each order, with the sub-parts it planned indented under it. Cancelling an order cancels its parts.
        int shown = 0;
        foreach (Job j in c.Orders.Take(4))
        {
            Row(g, j, new Rect2(o.X, y, area.Size.X, 24), c);
            if (g.Button($"cancel:{_key}:{j.Item.Id}", new Rect2(o.X + area.Size.X - 34, y, 30, 22), "X", C.Danger)) c.Cancel(j);
            y += 26;
            foreach (Job part in c.SubParts.Where(p2 => p2.Feeds == j))
            {
                if (shown >= 5) { shown++; continue; }
                Row(g, part, new Rect2(o.X + 22, y, area.Size.X - 22, 22), c);
                y += 22;
                shown++;
            }
        }
        int orphans = c.SubParts.Count(j => j.Feeds == null || !c.Orders.Take(4).Contains(j.Feeds));
        if (shown + orphans > 5) { g.Text($"+{shown + orphans - 5}", new Vector2(o.X + 22, y), C.Dim, 12); y += 18; }
        if (c.Orders.Count == 0) { g.Text("—", new Vector2(o.X, y), C.Dim, 13); y += 22; }
        List<string> blockers = c.Blockers;
        foreach (string b in blockers.Take(4))
        {
            g.Text($"NEEDS: {b}", new Vector2(o.X, y + 4), C.Danger, 13);
            y += 20;
        }
        if (blockers.Count > 4) { g.Text($"+{blockers.Count - 4}", new Vector2(o.X, y + 4), C.Danger, 12); y += 18; }
        if (c.Restock.InUse.Count > 0)
        {
            if (g.Button($"restock:{_key}", new Rect2(o.X, y + 6, 200, 30), c.Restock.Enabled ? "AMMO RESTOCK ON" : "AMMO RESTOCK OFF", C.Accent2, lit: c.Restock.Enabled))
                c.Restock.Enabled = !c.Restock.Enabled;
            g.Text(c.Restock.Status, new Vector2(o.X + 210, y + 12), C.Dim, 12);
        }
    }

    static void Row(Gui g, Job j, Rect2 r, Constructor c)
    {
        string feeds = j.Restocking != null ? "  restock" : "";
        g.Text($"{j.Item.Name} {j.Done}/{j.Count}{feeds}", r.Position, j.Running ? C.Good : C.Text, 13);
        if (j.Running) g.Bar(new Rect2(r.Position.X + r.Size.X - 150, r.Position.Y + 4, 110, 10), j.Progress, 1, C.Good, 8);
    }
}
