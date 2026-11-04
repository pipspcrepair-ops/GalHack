using System.Collections.Generic;
using System.Linq;
using Godot;
using PoiGenerator.Gen;

namespace PoiGenerator;

/// <summary>
/// The Tab map: the layout from above. Walkable floor is shaded by depth, modules are
/// outlined (key rooms brighter), and on top go the critical path from the way in to the
/// vault, the vault, the core, enemy spawns, caches, terminals and story props.
/// </summary>
public partial class Overlay : Control
{
    PoiInstance _poi;
    List<(Int3 cell, int level)> _floor = new();
    int _minY, _maxY;
    public Vector3? PlayerCell;   // in the place's local cells
    public float PlayerYaw;

    static readonly Color Back = new(0.04f, 0.05f, 0.07f, 0.88f);
    static readonly Color PathColor = new("ff8a2a");
    static readonly Color VaultColor = new("ffc840");
    static readonly Color CoreColor = new("3de8ff");

    public override void _Ready()
    {
        SetAnchorsPreset(LayoutPreset.FullRect);
        MouseFilter = MouseFilterEnum.Ignore;
    }

    public void SetPlace(PoiInstance poi)
    {
        _poi = poi;
        var nav = poi.Nav();
        // Only floor inside the building: the open ground around it is walkable too, but it isn't the layout.
        _floor = nav.Reachable(poi.Layout.EntranceCell).Where(c => poi.Layout.Occupied[c]).Select(c => (c, c.Y)).OrderBy(c => c.Y).ToList();
        _minY = _floor.Count > 0 ? _floor.Min(f => f.level) : 0;
        _maxY = _floor.Count > 0 ? _floor.Max(f => f.level) : 0;
        QueueRedraw();
    }

    public override void _Process(double delta)
    {
        if (Visible && PlayerCell != null) QueueRedraw();
    }

    public override void _Draw()
    {
        if (_poi == null) return;
        var size = GetViewportRect().Size;
        DrawRect(new Rect2(Vector2.Zero, size), Back);

        var layout = _poi.Layout;
        Int3 min = layout.Modules.Select(m => m.Min).Aggregate(Int3.Min) - new Int3(2, 0, 2);
        Int3 max = layout.Modules.Select(m => m.Max).Aggregate(Int3.Max) + new Int3(2, 0, 2);
        float w = max.X - min.X + 1, d = max.Z - min.Z + 1;
        float s = Mathf.Min((size.X - 420f) / w, (size.Y - 160f) / d);
        var origin = new Vector2((size.X - 320f - w * s) / 2f + 20f, 90f + (size.Y - 160f - d * s) / 2f);
        Vector2 P(float x, float z) => origin + new Vector2((x - min.X) * s, (z - min.Z) * s);
        Vector2 C(Int3 c) => P(c.X + 0.5f, c.Z + 0.5f);

        // Module footprints, then walkable floor shaded by depth (deeper is bluer).
        foreach (var m in layout.Modules)
        {
            var col = m.Underground ? new Color(0.1f, 0.13f, 0.2f) : new Color(0.16f, 0.14f, 0.13f);
            foreach (var c in m.Shape.Occupied.Where(c => c.Y == 0).Select(m.World))
                DrawRect(new Rect2(P(c.X, c.Z), new Vector2(s, s)), col);
        }
        foreach (var (c, level) in _floor)
        {
            float t = _maxY == _minY ? 1f : (level - _minY) / (float)(_maxY - _minY);
            var col = new Color("2c3a6a").Lerp(new Color("d8c6a4"), t);
            DrawRect(new Rect2(P(c.X, c.Z) + Vector2.One * 0.08f * s, new Vector2(s * 0.84f, s * 0.84f)), col);
        }
        foreach (var m in layout.Modules)
        {
            bool key = layout.KeyModules.Contains(m.Index);
            var col = m.Index == layout.VaultModule ? VaultColor : key ? new Color("e8e0d0") : new Color("70685e");
            DrawRect(new Rect2(P(m.Min.X, m.Min.Z), new Vector2(m.Shape.W * s, m.Shape.D * s)), col, false, key ? 2f : 1f);
        }

        // The critical path.
        var path = _poi.CriticalPath;
        for (int i = 1; i < path.Count; i++) DrawLine(C(path[i - 1]), C(path[i]), PathColor, Mathf.Max(2f, s * 0.35f));

        // Way in.
        Vector2 e = C(layout.EntranceCell);
        Vector2 dir = new(Look.ToVector(layout.EntranceFacing).X, Look.ToVector(layout.EntranceFacing).Z);
        DrawArrow(e + dir * s * 2.5f, e - dir * s * 0.5f, Colors.White, s);

        foreach (var c in _poi.Caches)
            DrawRect(new Rect2(C(c.Cell) - Vector2.One * s * 0.45f, Vector2.One * s * 0.9f), c.InVault ? VaultColor : new Color("d0a050"));
        foreach (var t in _poi.Terminals)
            DrawRect(new Rect2(C(t.Cell) - Vector2.One * s * 0.4f, Vector2.One * s * 0.8f), new Color("7cffb0"));
        foreach (var st in _poi.Story)
        {
            var c = C(st.Cell);
            float r = s * 0.35f;
            DrawLine(c - new Vector2(r, r), c + new Vector2(r, r), new Color("b0a8a0"), 1.5f);
            DrawLine(c - new Vector2(r, -r), c + new Vector2(r, -r), new Color("b0a8a0"), 1.5f);
        }
        foreach (var en in _poi.Enemies)
        {
            var col = en.Kind switch
            {
                EnemyKind.Boss => new Color("ff2050"),
                EnemyKind.Turret => new Color("ff6040"),
                EnemyKind.Corrupted or EnemyKind.Hollow => new Color("e040a0"),
                EnemyKind.Fauna => new Color("e0a040"),
                _ => new Color("ff3030"),
            };
            float r = (en.Kind == EnemyKind.Boss || en.Name.StartsWith("Alpha") ? 0.75f : 0.42f) * s;
            DrawCircle(C(en.Cell), r, col);
        }

        // The core: a diamond with a ring, so it stands out from everything else.
        var core = C(_poi.CoreCell);
        float k = s * 0.9f;
        DrawColoredPolygon(new[] { core + new Vector2(0, -k), core + new Vector2(k, 0), core + new Vector2(0, k), core + new Vector2(-k, 0) }, CoreColor);
        DrawArc(core, k * 1.7f, 0, Mathf.Tau, 24, CoreColor, 2f);

        if (PlayerCell is Vector3 pc)
        {
            var at = P(pc.X, pc.Z);
            var fwd = new Vector2(-Mathf.Sin(PlayerYaw), -Mathf.Cos(PlayerYaw));
            var side = new Vector2(-fwd.Y, fwd.X);
            DrawColoredPolygon(new[] { at + fwd * s * 1.4f, at + side * s * 0.7f - fwd * s * 0.6f, at - side * s * 0.7f - fwd * s * 0.6f }, Colors.White);
        }

        DrawLegend(size);
    }

    void DrawArrow(Vector2 from, Vector2 to, Color c, float s)
    {
        DrawLine(from, to, c, Mathf.Max(2f, s * 0.3f));
        Vector2 dir = (to - from).Normalized(), side = new(-dir.Y, dir.X);
        DrawColoredPolygon(new[] { to + dir * s * 0.6f, to - dir * s * 0.5f + side * s * 0.6f, to - dir * s * 0.5f - side * s * 0.6f }, c);
    }

    void DrawLegend(Vector2 size)
    {
        var font = ThemeDB.FallbackFont;
        var layout = _poi.Layout;
        string title = $"{layout.Family.Name.ToUpperInvariant()}  //  {layout.Name}";
        DrawString(font, new Vector2(30, 46), title, HorizontalAlignment.Left, -1, 26, new Color("ffe8c2"));
        DrawString(font, new Vector2(30, 72), $"{_poi.State} · {_poi.Rarity} · {layout.Modules.Count} modules · {_poi.Blocks.CountBlocks()} blocks · " +
            $"loot {_poi.LootValue} cr · {_poi.Enemies.Count} enemies · depth {_minY}..{_maxY}", HorizontalAlignment.Left, -1, 16, new Color("c8b8a0"));

        float x = size.X - 300f, y = 120f;
        void Row(Color c, string text, int shape)
        {
            var at = new Vector2(x, y);
            if (shape == 0) DrawRect(new Rect2(at - new Vector2(0, 9), new Vector2(14, 14)), c);
            else if (shape == 1) DrawCircle(at + new Vector2(7, -2), 7, c);
            else DrawLine(at + new Vector2(0, -2), at + new Vector2(16, -2), c, 4f);
            DrawString(font, at + new Vector2(26, 4), text, HorizontalAlignment.Left, -1, 15, new Color("e0d8c8"));
            y += 26f;
        }
        Row(PathColor, "critical path to the vault", 2);
        Row(VaultColor, "vault and its caches", 0);
        Row(CoreColor, "base core", 0);
        Row(new Color("d0a050"), "side cache", 0);
        Row(new Color("7cffb0"), "terminal", 0);
        Row(new Color("ff3030"), "defender spawn", 1);
        Row(new Color("ff6040"), "turret", 1);
        Row(new Color("e040a0"), "Hollow Bloom", 1);
        Row(new Color("e0a040"), "wildlife", 1);
        Row(new Color("ff2050"), "boss", 1);
        Row(new Color("b0a8a0"), "story prop (x)", 2);
        Row(new Color("d8c6a4"), "walkable floor, surface", 0);
        Row(new Color("2c3a6a"), "walkable floor, deepest", 0);
        y += 10f;
        DrawString(font, new Vector2(x, y), "Tab closes the map", HorizontalAlignment.Left, -1, 14, new Color("8a8070"));
    }
}
