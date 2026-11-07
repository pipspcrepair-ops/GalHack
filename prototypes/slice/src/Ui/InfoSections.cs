using System;
using System.Collections.Generic;
using System.Linq;
using DiegeticUi.Theme;
using GalHack.Physics.Engine;
using Godot;
using Slice.Items;
using Slice.Orbit;
using Slice.Vessels;
using Palette = DiegeticUi.Theme.Palette;

namespace Slice.Ui;

/// <summary>STATS: what you have done since the crash, and behind two buttons, the settings and the credits.</summary>
public sealed class StatsSection : IMenuSection
{
    static Palette C => UiTheme.Colors;
    enum Page { Stats, Settings, Credits }
    readonly Game.Game _game;
    Page _page;
    int _creditsScroll;
    public StatsSection(Game.Game game) => _game = game;
    public Vector2 PreferredSize => new(1560, 790);

    /// <summary>What the page shows, beyond the menu's own tick: a slider moved, a page turned, the credits scrolled.</summary>
    public string Signature => $"{_page}|{_creditsScroll}|{_game.Audio?.Settings.Version}";

    public void Build(Gui g, Rect2 area)
    {
        g.Caption(_page switch { Page.Settings => "SETTINGS", Page.Credits => "CREDITS", _ => "SINCE YOU WOKE" }, area.Position, area.Size.X);
        // A new game (the start screen: type this world's seed there to replay it).
        if (g.Button("options:new-game", new Rect2(area.End.X - 640, area.Position.Y, 200, 34), "NEW GAME", C.Accent) && _game.Interactive) _game.NewGame();
        if (g.Button("options:settings", new Rect2(area.End.X - 430, area.Position.Y, 200, 34), "SETTINGS", C.Accent2, lit: _page == Page.Settings)) _page = _page == Page.Settings ? Page.Stats : Page.Settings;
        if (g.Button("options:credits", new Rect2(area.End.X - 220, area.Position.Y, 200, 34), "CREDITS", C.Accent2, lit: _page == Page.Credits)) _page = _page == Page.Credits ? Page.Stats : Page.Credits;
        var below = new Rect2(area.Position + new Vector2(0, 44), area.Size - new Vector2(0, 44));
        switch (_page)
        {
            case Page.Settings: Settings(g, below); break;
            case Page.Credits: Credits(g, below); break;
            default: Numbers(g, area); break;
        }
    }

    /// <summary>The four volumes. Remembered between runs.</summary>
    void Settings(Gui g, Rect2 area)
    {
        Game.AudioSettings settings = _game.Audio?.Settings;
        if (settings == null) return;
        for (int i = 0; i < Game.AudioSettings.Volumes.Length; i++)
        {
            string key = Game.AudioSettings.Volumes[i];
            float value = settings.Get(key), y = area.Position.Y + 20 + i * 64;
            g.Fill(new Rect2(area.Position.X, y - 12, area.Size.X, 56), new Color(i % 2 == 0 ? C.Panel : C.Glass, 0.6f));
            g.Text($"{key.ToUpperInvariant()} VOLUME", new Vector2(area.Position.X + 12, y + 4), C.Dim, 17);
            g.Text($"{value * 100f:F0}%", new Vector2(area.Position.X + 300, y + 2), C.Text, 19, UiTheme.Mono);
            if (g.Slider($"options:volume-{key}", new Rect2(area.Position.X + 420, y, 760, 32), ref value, C.Accent2)) _game.Audio.SetVolume(key, value);
        }
    }

    /// <summary>Everyone whose work is in the game, generated music included. The wheel scrolls it.</summary>
    void Credits(Gui g, Rect2 area)
    {
        const float RowHeight = 54f;
        var rows = Game.Credits.Shared.Rows;
        int fits = Math.Max(1, (int)(area.Size.Y / RowHeight));
        if (!g.Drawing && g.Wheel != 0) _creditsScroll = Math.Clamp(_creditsScroll - g.Wheel * 2, 0, Math.Max(0, rows.Count - fits));
        for (int i = _creditsScroll; i < rows.Count && i < _creditsScroll + fits; i++)
        {
            Game.CreditRow row = rows[i];
            float y = area.Position.Y + (i - _creditsScroll) * RowHeight;
            g.Fill(new Rect2(area.Position.X, y, area.Size.X, RowHeight - 4), new Color(i % 2 == 0 ? C.Panel : C.Glass, 0.6f));
            g.TextIn($"{row.Section}: {row.What}", new Rect2(area.Position.X + 12, y + 4, area.Size.X - 24, 24), C.Text, 17);
            g.TextIn($"{row.Author}   ·   {row.Licence}   ·   {row.Source}", new Rect2(area.Position.X + 12, y + 28, area.Size.X - 24, 20), C.Dim, 14);
        }
    }

    void Numbers(Gui g, Rect2 area)
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
    public const float MinRange = 40f, MaxRange = 4000f;
    /// <summary>The world, system and star levels (and which is showing).</summary>
    public readonly MapLevels Levels;
    public MapSection(Game.Game game)
    {
        _game = game;
        Levels = new MapLevels(game);
    }
    public Vector2 PreferredSize => new(1560, 790);

    public void Build(Gui g, Rect2 area)
    {
        if (Levels.Build(g, area)) return;
        float size = Math.Min(area.Size.X - 360, area.Size.Y);
        var map = new Rect2(area.Position, new Vector2(size, size));
        g.Fill(map, new Color(C.Panel, 0.8f));
        // The generator's terrain behind the dots, a little dimmed so they read over it.
        if (Levels.Terrain(g, map, Range, Levels.LocalPan)) g.Fill(map, new Color(C.Glass, 0.35f));
        g.Outline(map, C.Rule, 2f);
        Vector3 you = _game.Survivor.WorldFeet;
        Vector3 me = you + new Vector3(Levels.LocalPan.X, 0f, Levels.LocalPan.Y);
        Vector2 Proj(Vector3 p) => map.GetCenter() + new Vector2(p.X - me.X, p.Z - me.Z) / Range * (size / 2f);
        bool In(Vector2 q) => map.HasPoint(q);
        if (!g.Drawing)
        {
            // Zoom with the wheel or + and -, pan with the middle button; past the widest, the wheel goes up a level.
            int steps = Levels.ZoomSteps(g, map);
            if (steps != 0)
            {
                if (steps > 0 && Range >= MaxRange * 0.999f) Levels.WheelPast(steps);
                else Range = Math.Clamp(Range * MathF.Pow(1.25f, steps), MinRange, MaxRange);
            }
            Levels.LocalRange = Range;
            Vector2 slide = Levels.SlideBy(g, map);
            if (slide != Vector2.Zero) Levels.LocalPan -= slide * (Range / (size / 2f));
            me = you + new Vector3(Levels.LocalPan.X, 0f, Levels.LocalPan.Y);
        }
        if (g.RightClicked(map)) Levels.LocalRightClick(g.Mouse, Proj, q => me + new Vector3(q.X - map.GetCenter().X, 0, q.Y - map.GetCenter().Y) * (Range / (size / 2f)));
        var labels = new MapLabels();
        if (g.Drawing)
        {
            for (int k = 1; k <= 3; k++) MapLevels.ArcIn(g, map, Proj(you), size / 2f * k / 3f, new Color(C.Rule, 0.7f));
            g.TextIn("N", new Rect2(map.GetCenter() + new Vector2(-10, -size / 2 + 4), new Vector2(20, 20)), C.Text, 16, HorizontalAlignment.Center);
        }
        // Lower priority keeps its label's place when two would collide.
        void Mark(Vector3 at, string label, Color colour, float r = 6f, int priority = 5)
        {
            Vector2 q = Proj(at);
            if (!In(q) || !g.Drawing) return;
            g.Canvas.DrawCircle(q, r, colour);
            labels.Add(q, r, label, colour, priority);
        }
        if (_game.Zone.Wildlife.Sim is { } sim)
            foreach (var c in sim.Creatures)
                if (!c.Removed && !c.Dead) Mark(_game.Zone.Wildlife.FromPatch(c.Position), null, new Color(C.Warn, 0.7f), 3f);
        foreach (var (kind, at) in _game.Visor.Deposits.Near(_game.Zone, me)) Mark(at, kind.ToUpperInvariant(), C.Holo, 6f, 6);
        foreach (Lootable l in _game.Zone.Loot.All)
            if (l.Kind != LootKind.Cache) // a place's caches are found by going in, not from the map
                Mark(l.Position, l.Kind == LootKind.Backpack ? "YOUR BACKPACK" : null, l.Kind == LootKind.Backpack ? C.Danger : C.Accent2, l.Kind == LootKind.Backpack ? 8 : 4, 2);
        foreach (Device d in _game.Zone.Devices) Mark(d.GlobalPosition, d.Label.ToUpperInvariant(), C.Accent2, 6f, 4);
        foreach (Vessel v in _game.Zone.Vessels)
            if (v.IsRuin) Mark(_game.Start, "BUNKER", C.Accent, 7, 1);
            else if (!v.IsWreck) Mark(v.Body.Position.ToGodot(), v.Name.ToUpperInvariant(), v.IsBase ? C.Good : C.Text, 7, 1);
        // Places you have found, and your own marks.
        if (!_game.InOrbit)
        {
            foreach (ChartMark m in _game.Spaceflight.Charts.Marks)
                if (m.Kind == "place") Mark(Levels.NearYou(m.X, m.Z), m.Label.ToUpperInvariant(), C.Accent2, 7, 3);
                else if (m.Kind == "mark") Mark(Levels.NearYou(m.X, m.Z), m.Label, C.Danger, 6, 3);
        }
        if (g.Drawing)
        {
            Vector3 look = _game.Survivor.WorldLook();
            Vector2 c0 = Proj(you), dir = new Vector2(look.X, look.Z).Normalized();
            if (map.HasPoint(c0))
            {
                g.Canvas.DrawLine(c0, c0 + dir * 22f, C.Accent, 3f);
                g.Canvas.DrawCircle(c0, 7f, C.Accent);
                labels.Add(c0, 7f, null, C.Accent, 0);
            }
            labels.Flush(g, map);
        }
        var legend = new Rect2(map.End.X + 30, area.Position.Y, 320, area.Size.Y);
        g.Caption($"MAP · {Range:0} M", legend.Position, legend.Size.X);
        string[] lines = { "orange: you, the bunker", "red: your marks (right click)", "green: bases", "cream: vessels", "amber: devices, places, drops", "teal: nearest deposits", "red: your backpack", "dots: creatures" };
        for (int i = 0; i < lines.Length; i++) g.Text(lines[i], legend.Position + new Vector2(0, 40 + i * 26), C.Dim, 15);
        if (g.Button("map:near", new Rect2(legend.Position + new Vector2(0, 270), new Vector2(150, 34)), "300 M", C.Accent2, lit: Range == 300f)) { Range = 300f; Levels.LocalPan = Vector2.Zero; }
        if (g.Button("map:far", new Rect2(legend.Position + new Vector2(160, 270), new Vector2(150, 34)), "600 M", C.Accent2, lit: Range == 600f)) { Range = 600f; Levels.LocalPan = Vector2.Zero; }
        Levels.Tabs(g, legend.Position + new Vector2(0, 330));
    }
}
