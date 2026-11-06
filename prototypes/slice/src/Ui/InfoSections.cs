using System;
using System.Collections.Generic;
using System.Linq;
using DiegeticUi.Theme;
using GalHack.Physics.Engine;
using Godot;
using Slice.Items;
using Slice.Vessels;
using Palette = DiegeticUi.Theme.Palette;

namespace Slice.Ui;

/// <summary>
/// TECH: the tech tree, one tab per kind of thing (base, capital vessel, small vessel, hover
/// vessel, tools, weapons, deco, misc), built from the unlock on every item and block. Nodes stand
/// in columns by the level they open at, each joined to its parent. Unlocked nodes are lit; locked
/// ones say what they need (a level, points, their parent); one you can afford unlocks on a click.
/// The deco tab holds a family per theme set: pick a set, then its pieces show.
/// </summary>
public sealed class TechSection : IMenuSection
{
    static Palette C => UiTheme.Colors;
    readonly Game.Game _game;
    public string Tab = "base";
    /// <summary>The deco set shown on a tab with groups.</summary>
    public string Group;

    public TechSection(Game.Game game) => _game = game;
    public Vector2 PreferredSize => new(1560, 790);

    public void Build(Gui g, Rect2 area)
    {
        Progress p = _game.Progress;
        Vector2 o = area.Position;
        for (int i = 0; i < Progress.Tabs.Length; i++)
        {
            string t = Progress.Tabs[i];
            if (g.Tab($"tech:tab:{t}", new Rect2(o + new Vector2(i * 189, 0), new Vector2(182, 36)), Progress.TabName(t), Tab == t, C.Accent2)) Tab = t;
        }
        g.Text($"LEVEL {p.Level}   {p.Points} UNLOCK POINTS   {p.Xp} XP{(p.Creative ? "   (CREATIVE: everything unlocked)" : "")}", o + new Vector2(0, 46), C.Accent2, 17, UiTheme.Display);
        var nodes = p.Nodes.Where(n => n.Tab == Tab).ToList();
        if (nodes.Count == 0) return;
        // Tabs with groups (deco): a row of sets, then one set's pieces. Up to 12 sets a row, two rows.
        var groups = nodes.Select(n => n.Group).Where(x => x != null).Distinct().ToList();
        float top = 0;
        if (groups.Count > 0)
        {
            if (Group == null || !groups.Contains(Group)) Group = groups[0];
            float gw = (area.Size.X - 10) / 12;
            for (int i = 0; i < groups.Count; i++)
            {
                var r = new Rect2(o.X + i % 12 * gw, o.Y + 76 + i / 12 * 30, gw - 4, 26);
                if (g.Button($"tech:group:{groups[i]}", r, groups[i].ToUpperInvariant(), C.Holo, lit: groups[i] == Group, size: 11)) Group = groups[i];
            }
            top = (groups.Count + 11) / 12 * 30 + 4;
            nodes = nodes.Where(n => n.Group == Group).ToList();
        }
        // Columns by level, rows in order within a column.
        var levels = nodes.Select(n => n.Level).Distinct().OrderBy(l => l).ToList();
        float colW = Math.Min(230f, (area.Size.X - 10) / Math.Max(1, levels.Count)), boxW = colW - 22, boxH = 54;
        var at = new Dictionary<string, Rect2>();
        foreach (int level in levels)
        {
            int col = levels.IndexOf(level);
            var inCol = nodes.Where(n => n.Level == level).ToList();
            g.TextIn($"LEVEL {level}", new Rect2(o.X + col * colW, o.Y + 80 + top, boxW, 20), p.Level >= level ? C.Good : C.Dim, 14, HorizontalAlignment.Center, UiTheme.Mono);
            // Long columns wrap into a second and third column's width by shrinking the rows.
            float rowH = Math.Min(boxH + 12, (area.Size.Y - 110 - top) / Math.Max(1, inCol.Count));
            for (int i = 0; i < inCol.Count; i++)
                at[inCol[i].Id] = new Rect2(o.X + col * colW, o.Y + 106 + top + i * rowH, boxW, Math.Min(boxH, rowH - 4));
        }
        // Lines from each node to its parent (same tab only).
        foreach (TechNode n in nodes)
            if (n.Unlock.Parent != null && at.TryGetValue(n.Unlock.Parent, out Rect2 parent) && g.Drawing)
            {
                Rect2 me = at[n.Id];
                g.Canvas.DrawLine(new Vector2(parent.End.X, parent.Position.Y + boxH / 2), new Vector2(me.Position.X, me.Position.Y + boxH / 2),
                    p.IsUnlocked(n.Id) ? C.Good : C.Rule, 2f);
            }
        foreach (TechNode n in nodes)
        {
            Rect2 r = at[n.Id];
            UnlockState state = p.StateOf(n);
            Color tone = state switch { UnlockState.Unlocked => C.Good, UnlockState.Affordable => C.Accent, _ => C.Dim };
            if (g.Button($"tech:{n.Id}", r, "", tone, enabled: state == UnlockState.Affordable, lit: false))
            {
                if (p.Unlock(n.Id)) g.Sound = "unlock";
            }
            if (g.Drawing)
            {
                g.Fill(r, new Color(state == UnlockState.Unlocked ? C.Panel : C.Glass, 0.95f));
                g.Fill(new Rect2(r.Position, new Vector2(6, r.Size.Y)), tone);
                g.Outline(r, tone, state == UnlockState.Affordable ? 3 : 1.5f);
                g.Text(n.Name, r.Position + new Vector2(12, 5), state == UnlockState.Unlocked ? C.Text : C.Dim, 14);
                g.Text(state == UnlockState.Unlocked ? $"UNLOCKED · {n.Cost} PT" : p.Needs(n), r.Position + new Vector2(12, 29), state == UnlockState.Affordable ? C.Accent : state == UnlockState.Unlocked ? C.Good : C.Warn, 12);
            }
        }
    }
}

/// <summary>STATS: what you have done since the crash.</summary>
public sealed class StatsSection : IMenuSection
{
    static Palette C => UiTheme.Colors;
    readonly Game.Game _game;
    public StatsSection(Game.Game game) => _game = game;
    public Vector2 PreferredSize => new(1560, 790);

    public void Build(Gui g, Rect2 area)
    {
        Game.GameStats s = _game.Stats;
        Progress p = _game.Progress;
        var rows = new (string, string)[]
        {
            ("TIME SINCE THE CRASH", TimeSpan.FromSeconds(s.Seconds).ToString(@"h\:mm\:ss")),
            ("LEVEL", $"{p.Level} ({p.Xp} XP, {p.State.PointsSpent} points spent, {p.Points} to spend)"),
            ("TECH UNLOCKED", $"{p.Nodes.Count(n => p.IsUnlocked(n.Id))} of {p.Nodes.Count}"),
            ("WALKED", $"{s.Walked / 1000f:F2} km"),
            ("FLOWN", $"{s.Flown / 1000f:F2} km (top speed {s.TopSpeed:F0} m/s)"),
            ("WORLD", $"{_game.World?.Name}   SEED {_game.World?.Seed}"),
            ("ORE CELLS DUG", $"{_game.Tool.OreCells}"),
            ("PLANTS CUT", $"{_game.Tool.PlantsCut}"),
            ("SHOTS FIRED", $"{_game.Tool.Shots}"),
            ("CREATURES BROUGHT DOWN", $"{s.Kills}"),
            ("SPECIES MET", $"{s.SpeciesMet} of {_game.Zone.Wildlife.Fauna?.Species.Count ?? 0}"),
            ("DEPOSITS FOUND", $"{s.DepositsFound}"),
            ("ITEMS CRAFTED", $"{s.Crafted}"),
            ("BLOCKS PLACED / TAKEN DOWN", $"{_game.Builder.Placed} / {_game.Builder.TakenDown}"),
            ("STRUCTURES", string.Join(", ", _game.Zone.Vessels.Where(v => !v.IsWreck && !v.IsRuin).Select(v => v.Name))),
            ("SUPPLIES USED", $"{s.Used}"),
            ("DEATHS", $"{s.Deaths}"),
        };
        g.Caption("SINCE YOU WOKE", area.Position, area.Size.X);
        // A new game (the start screen: type this world's seed there to replay it).
        if (g.Button("options:new-game", new Rect2(area.End.X - 640, area.Position.Y, 200, 34), "NEW GAME", C.Accent) && _game.Interactive) _game.NewGame();
        // Options: the master volume.
        var vol = new Rect2(area.End.X - 420, area.Position.Y + 2, 420, 34);
        float master = _game.Audio?.Master ?? 1f;
        g.Text($"MASTER VOLUME {master * 100:F0}%", vol.Position + new Vector2(0, 6), C.Text, 16);
        if (g.Button("options:volume-down", new Rect2(vol.Position + new Vector2(230, 0), new Vector2(80, 32)), "-", C.Accent2) && _game.Audio != null) _game.Audio.Master = Mathf.Max(0f, master - 0.1f);
        if (g.Button("options:volume-up", new Rect2(vol.Position + new Vector2(320, 0), new Vector2(80, 32)), "+", C.Accent2) && _game.Audio != null) _game.Audio.Master = Mathf.Min(1f, master + 0.1f);
        for (int i = 0; i < rows.Length; i++)
        {
            var (label, value) = rows[i];
            float y = area.Position.Y + 44 + i * 42;
            g.Fill(new Rect2(area.Position.X, y - 6, area.Size.X, 36), new Color(i % 2 == 0 ? C.Panel : C.Glass, 0.6f));
            g.Text(label, new Vector2(area.Position.X + 12, y), C.Dim, 17);
            g.Text(value, new Vector2(area.Position.X + 420, y - 2), C.Text, 19, UiTheme.Mono);
        }
    }
}

/// <summary>MAP: what is around you, north up, 600 m each way: your structures, devices, the pod, your backpack, bodies and drops, the nearest deposits and the creatures near you.</summary>
public sealed class MapSection : IMenuSection
{
    static Palette C => UiTheme.Colors;
    readonly Game.Game _game;
    public float Range = 600f;
    public MapSection(Game.Game game) => _game = game;
    public Vector2 PreferredSize => new(1560, 790);

    public void Build(Gui g, Rect2 area)
    {
        float size = Math.Min(area.Size.X - 360, area.Size.Y);
        var map = new Rect2(area.Position, new Vector2(size, size));
        g.Fill(map, new Color(C.Panel, 0.8f));
        g.Outline(map, C.Rule, 2f);
        Vector3 me = _game.Survivor.WorldFeet;
        Vector2 Proj(Vector3 p) => map.GetCenter() + new Vector2(p.X - me.X, p.Z - me.Z) / Range * (size / 2f);
        bool In(Vector2 q) => map.HasPoint(q);
        if (g.Drawing)
        {
            for (int k = 1; k <= 3; k++) g.Canvas.DrawArc(map.GetCenter(), size / 2f * k / 3f, 0, Mathf.Tau, 48, new Color(C.Rule, 0.7f), 1f);
            g.TextIn("N", new Rect2(map.GetCenter() + new Vector2(-10, -size / 2 + 4), new Vector2(20, 20)), C.Text, 16, HorizontalAlignment.Center);
        }
        void Mark(Vector3 at, string label, Color colour, float r = 6f)
        {
            Vector2 q = Proj(at);
            if (!In(q) || !g.Drawing) return;
            g.Canvas.DrawCircle(q, r, colour);
            if (label != null) g.Text(label, q + new Vector2(r + 3, -9), colour, 13);
        }
        if (_game.Zone.Wildlife.Sim is { } sim)
            foreach (var c in sim.Creatures)
                if (!c.Removed && !c.Dead) Mark(_game.Zone.Wildlife.FromPatch(c.Position), null, new Color(C.Warn, 0.7f), 3f);
        foreach (var (kind, at) in _game.Visor.Deposits.Near(_game.Zone, me)) Mark(at, kind.ToUpperInvariant(), C.Holo);
        foreach (Lootable l in _game.Zone.Loot.All)
            Mark(l.Position, l.Kind == LootKind.Backpack ? "YOUR BACKPACK" : null, l.Kind == LootKind.Backpack ? C.Danger : C.Accent2, l.Kind == LootKind.Backpack ? 8 : 4);
        foreach (Device d in _game.Zone.Devices) Mark(d.GlobalPosition, d.Label.ToUpperInvariant(), C.Accent2);
        foreach (Vessel v in _game.Zone.Vessels)
            if (v.IsRuin) Mark(_game.Start, "BUNKER", C.Accent, 7);
            else if (!v.IsWreck) Mark(v.Body.Position.ToGodot(), v.Name.ToUpperInvariant(), v.IsBase ? C.Good : C.Text, 7);
        if (g.Drawing)
        {
            Vector3 look = _game.Survivor.WorldLook();
            Vector2 c0 = map.GetCenter(), dir = new Vector2(look.X, look.Z).Normalized();
            g.Canvas.DrawLine(c0, c0 + dir * 22f, C.Accent, 3f);
            g.Canvas.DrawCircle(c0, 7f, C.Accent);
        }
        var legend = new Rect2(map.End.X + 30, area.Position.Y, 320, area.Size.Y);
        g.Caption("MAP · 600 M", legend.Position, legend.Size.X);
        string[] lines = { "orange: you, the bunker", "green: bases", "cream: vessels", "amber: devices, drops", "teal: nearest deposits", "red: your backpack", "dots: creatures" };
        for (int i = 0; i < lines.Length; i++) g.Text(lines[i], legend.Position + new Vector2(0, 40 + i * 26), C.Dim, 15);
        if (g.Button("map:near", new Rect2(legend.Position + new Vector2(0, 240), new Vector2(150, 34)), "300 M", C.Accent2, lit: Range == 300f)) Range = 300f;
        if (g.Button("map:far", new Rect2(legend.Position + new Vector2(160, 240), new Vector2(150, 34)), "600 M", C.Accent2, lit: Range == 600f)) Range = 600f;
    }
}
