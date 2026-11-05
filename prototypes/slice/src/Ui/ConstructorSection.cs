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
/// prototype's screens read it): what it can make, ordered 1, 5 or 10 at a time; your orders
/// above and the sub-parts it planned by itself below, each saying which order it feeds; what it
/// is still missing; and its ammo restock lane. Things the tech tree hasn't unlocked, or that need
/// a bigger constructor, are greyed out with the reason. The same panel serves the suit
/// fabricator, a placed survival constructor and every constructor on a structure.
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

    /// <summary>What a constructor lists: everything it knows (products first), then what a bigger one would make, greyed.</summary>
    static IEnumerable<ItemDef> Recipes(Constructor c) =>
        c.Items.Items.Where(i => i.Recipe != null && i.Recipe.Tier <= c.Def.Tier + 1)
            .OrderBy(i => !c.Knows(i))
            .ThenBy(i => i.Category is ItemCategory.Refined or ItemCategory.Part ? 1 : 0)
            .ThenBy(i => i.Recipe.Tier)
            .ThenBy(i => i.Name, StringComparer.Ordinal);

    public void Build(Gui g, Rect2 area)
    {
        Constructor c = _machine();
        Vector2 o = area.Position;
        g.Caption(_title, o, area.Size.X);
        if (c == null)
        {
            g.Text("No constructor here.", o + new Vector2(0, 40), C.Dim, 16);
            return;
        }
        Progress p = _game.Progress;
        g.Text($"{c.Def.Tag}  {c.Def.Name.ToUpperInvariant()}  ·  speed {c.Def.Speed:0.##}x{(_game.CraftTimeScale != 1 ? $"  (TEST TIME-SCALE {_game.CraftTimeScale:0}x)" : "")}", o + new Vector2(0, 32), C.Dim, 13);
        g.Text(c.Status, o + new Vector2(0, 52), c.Current != null ? C.Good : C.Accent2, 15, UiTheme.Mono);

        // ---- What it can make -------------------------------------------------------------------
        var list = Recipes(c).ToList();
        int perPage = 8;
        _scroll = Math.Clamp(_scroll, 0, Math.Max(0, (list.Count - 1) / perPage));
        float y = o.Y + 80;
        for (int i = 0; i < perPage; i++)
        {
            int k = _scroll * perPage + i;
            if (k >= list.Count) break;
            ItemDef item = list[k];
            bool known = c.Knows(item), allowed = p.ItemAllowed(item);
            var row = new Rect2(o.X, y, area.Size.X, 40);
            g.Fill(row, new Color(known && allowed ? C.Panel : C.Glass, 0.9f));
            g.Fill(new Rect2(row.Position, new Vector2(5, row.Size.Y)), new Color(Rarities.Color(item.Rarity) << 8 | 0xff));
            g.Text($"{item.Name}{(item.Recipe.Count > 1 ? $" x{item.Recipe.Count}" : "")}", row.Position + new Vector2(12, 2), known && allowed ? C.Text : C.Dim, 15);
            string detail = !known ? $"needs a {c.Items.Machines.TierName(item.Recipe.Tier)} constructor"
                : !allowed ? p.LockReason(item.Id)
                : $"{c.Items.Describe(item.Recipe.InputsFor(1), 3)} · {c.RunSeconds(item.Recipe):0.#} s";
            g.Text(detail, row.Position + new Vector2(12, 21), !known || !allowed ? C.Warn : C.Dim, 12);
            int[] counts = { 1, 5, 10 };
            for (int n = 0; n < counts.Length; n++)
            {
                var b = new Rect2(row.End.X - 150 + n * 50, row.Position.Y + 5, 44, 30);
                if (g.Button($"order:{_key}:{item.Id}:{counts[n]}", b, $"+{counts[n]}", C.Accent, known && allowed))
                {
                    OrderResult r = c.Order(item.Id, counts[n]);
                    _said = r.Ok ? $"Queued {counts[n] * item.Recipe.Count} {item.Name}" : r.Message;
                    g.Sound = r.Ok ? "queued" : "refused";
                }
            }
            y += 44;
        }
        if (g.Button($"{_key}:up", new Rect2(o.X, y + 2, 70, 28), "UP", C.Accent2, _scroll > 0)) _scroll--;
        if (g.Button($"{_key}:down", new Rect2(o.X + 80, y + 2, 70, 28), "DOWN", C.Accent2, (_scroll + 1) * perPage < list.Count)) _scroll++;
        g.Text($"{_scroll + 1}/{(list.Count + perPage - 1) / perPage}", new Vector2(o.X + 160, y + 6), C.Dim, 14, UiTheme.Mono);
        g.Text(_said, new Vector2(o.X + 210, y + 6), C.Accent2, 13);
        y += 40;

        // ---- Queues -----------------------------------------------------------------------------
        g.Caption("YOUR ORDERS", new Vector2(o.X, y), area.Size.X, C.Accent2);
        y += 32;
        foreach (Job j in c.Orders.Take(4))
        {
            Row(g, j, new Rect2(o.X, y, area.Size.X, 26), c);
            if (g.Button($"cancel:{_key}:{j.Item.Id}", new Rect2(o.X + area.Size.X - 34, y, 30, 24), "X", C.Danger)) c.Cancel(j);
            y += 30;
        }
        if (c.Orders.Count == 0) { g.Text("none", new Vector2(o.X, y), C.Dim, 13); y += 22; }
        g.Caption("AUTO SUB-PARTS", new Vector2(o.X, y + 4), area.Size.X, C.Holo);
        y += 36;
        foreach (Job j in c.SubParts.Take(5))
        {
            Row(g, j, new Rect2(o.X, y, area.Size.X, 24), c);
            y += 26;
        }
        if (c.SubParts.Count > 5) { g.Text($"+{c.SubParts.Count - 5} more", new Vector2(o.X, y), C.Dim, 12); y += 18; }
        Bill missing = c.Missing;
        if (!missing.IsEmpty)
        {
            g.Text($"MISSING: {c.Items.Describe(missing, 4)}", new Vector2(o.X, y + 4), C.Danger, 14);
            y += 24;
        }
        if (c.Restock.InUse.Count > 0)
        {
            if (g.Button($"restock:{_key}", new Rect2(o.X, y + 6, 200, 30), c.Restock.Enabled ? "AMMO RESTOCK ON" : "AMMO RESTOCK OFF", C.Accent2, lit: c.Restock.Enabled))
                c.Restock.Enabled = !c.Restock.Enabled;
            g.Text(c.Restock.Status, new Vector2(o.X + 210, y + 12), C.Dim, 12);
        }
    }

    static void Row(Gui g, Job j, Rect2 r, Constructor c)
    {
        string feeds = j.Feeds != null ? $"  for {j.Feeds.Item.Name}" : j.Restocking != null ? "  restock" : "";
        g.Text($"{j.Item.Name} {j.Done}/{j.Count}{feeds}", r.Position, j.Running ? C.Good : C.Text, 13);
        if (j.Running) g.Bar(new Rect2(r.Position.X + r.Size.X - 150, r.Position.Y + 4, 110, 10), j.Progress, 1, C.Good, 8);
    }
}
