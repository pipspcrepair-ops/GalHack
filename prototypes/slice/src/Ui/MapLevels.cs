using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using GalHack.Physics.Engine;
using DiegeticUi.Theme;
using Palette = DiegeticUi.Theme.Palette;
using Godot;
using Planets.Look;
using Slice.Orbit;
using Slice.Surface;
using Slice.Vessels;

namespace Slice.Ui;

/// <summary>Which level the Map tab shows.</summary>
public enum MapLevel { Local, World, System, Stars }

/// <summary>
/// The Map tab's three further levels: the world (the generator's map, wrapping both ways, dragged
/// and zoomed), the system (the star's bodies and the wrecks found, with a destination and a route
/// line) and the stars (home and its neighbours). Each shows only what has been discovered
/// (<see cref="Charts"/>), in the suit terminal's look.
/// </summary>
public sealed class MapLevels
{
    static Palette C => UiTheme.Colors;
    readonly Game.Game _game;
    public MapLevel Level = MapLevel.Local;
    public WorldMapView View { get; private set; }
    Task<Image> _painting;
    ImageTexture _chart;
    Vector2? _dragFrom;

    public MapLevels(Game.Game game) => _game = game;

    /// <summary>What the panel's picture depends on, so dragging and zooming redraw it at once.</summary>
    public string Signature => $"{Level}|{View?.CentreX:F0}|{View?.CentreZ:F0}|{View?.MetresPerPixel:F2}|{_chart != null}|{_game.Spaceflight.Charts.Marks.Count()}|{_game.Spaceflight.Charts.Wrecks.Count}|{_game.Spaceflight.Charts.Destination}";

    /// <summary>The level tabs (under the legend): true if a level was changed.</summary>
    public void Tabs(Gui g, Vector2 at)
    {
        (string id, string label, MapLevel level)[] tabs = { ("map:local", "HERE", MapLevel.Local), ("map:world", "WORLD", MapLevel.World), ("map:system", "SYSTEM", MapLevel.System), ("map:stars", "STARS", MapLevel.Stars) };
        for (int i = 0; i < tabs.Length; i++)
            if (g.Tab(tabs[i].id, new Rect2(at + new Vector2(i % 2 * 156, i / 2 * 38), new Vector2(150, 34)), tabs[i].label, Level == tabs[i].level, C.Accent2)) Level = tabs[i].level;
    }

    /// <summary>Draws the level that isn't the local map. False for the local map (the section draws it).</summary>
    public bool Build(Gui g, Rect2 area)
    {
        if (Level == MapLevel.Local) return false;
        var panel = new Rect2(area.Position, new Vector2(area.Size.X - 360, area.Size.Y));
        g.Fill(panel, new Color(C.Panel, 0.8f));
        g.Outline(panel, C.Rule, 2f);
        var side = new Rect2(panel.End.X + 30, area.Position.Y, 320, area.Size.Y);
        switch (Level)
        {
            case MapLevel.World: World(g, panel, side); break;
            case MapLevel.System: SystemView(g, panel, side); break;
            default: Stars(g, panel, side); break;
        }
        Tabs(g, side.Position + new Vector2(0, 330));
        return true;
    }

    // ---- The world --------------------------------------------------------------------------

    /// <summary>How close, in pixels, a right click must be to take down one of your marks.</summary>
    public const float MarkHit = 12f;

    /// <summary>The generator's terrain colours behind the local map: <paramref name="halfMetres"/> each way from you. False while it is still being charted.</summary>
    public bool Terrain(Gui g, Rect2 panel, float halfMetres)
    {
        Paint();
        if (_chart == null || !g.Drawing) return false;
        var (ux, uz) = You();
        var planet = _game.Surface.Planet;
        Tiles(g, panel, ux, uz, 2.0 * halfMetres / panel.Size.X, planet.Width, planet.Height);
        return true;
    }

    /// <summary>The map point of a spot on the local map, and the other way: where a place is, as a point near you in world coordinates (the nearest copy on the torus).</summary>
    public Vector3 NearYou(double x, double z)
    {
        Vector3 me = _game.Survivor.WorldFeet;
        var planet = _game.Surface.Planet;
        var (ux, uz) = You();
        double dx = x - ux, dz = z - uz;
        if (dx > planet.Width / 2) dx -= planet.Width; else if (dx < -planet.Width / 2) dx += planet.Width;
        if (dz > planet.Height / 2) dz -= planet.Height; else if (dz < -planet.Height / 2) dz += planet.Height;
        return new Vector3(me.X + (float)dx, me.Y, me.Z + (float)dz);
    }

    /// <summary>The local map's right click: on a mark of yours, take it down; anywhere else, make one there.</summary>
    public void LocalRightClick(Vector2 mouse, Func<Vector3, Vector2> project, Func<Vector2, Vector3> unproject)
    {
        if (_game.InOrbit) return;
        Charts charts = _game.Spaceflight.Charts;
        ChartMark hit = charts.OfKind("mark").FirstOrDefault(m => project(NearYou(m.X, m.Z)).DistanceTo(mouse) <= MarkHit);
        if (hit != null) { charts.Unmark(hit.Key); return; }
        Vector3 at = unproject(mouse);
        var planet = _game.Surface.Planet;
        charts.Mark(Planets.Gen.Mathx.WrapX(at.X, planet.Width), Planets.Gen.Mathx.Wrap(at.Z, planet.Height));
    }

    (double x, double z) You()
    {
        Vector3 p = _game.Survivor.WorldFeet;
        return _game.InOrbit ? _game.Orbit.Under(p) : (Planets.Gen.Mathx.WrapX(p.X, _game.Surface.Planet.Width), Planets.Gen.Mathx.Wrap(p.Z, _game.Surface.Planet.Height));
    }

    void Paint()
    {
        var planet = _game.Surface.Planet;
        _painting ??= Task.Run(() =>
        {
            ColorMap colors = ColorMap.Build(planet.Map, ArchetypeLook.For(planet.Spec), 2048);
            return Image.CreateFromData(colors.Width, colors.Height, false, Image.Format.Rgb8, colors.Composite());
        });
        if (_chart == null && _painting.IsCompletedSuccessfully) _chart = ImageTexture.CreateFromImage(_painting.Result);
    }

    void World(Gui g, Rect2 panel, Rect2 side)
    {
        var planet = _game.Surface.Planet;
        Paint();
        View ??= new WorldMapView(planet.Width, planet.Height, planet.Width / panel.Size.X);
        var (ux, uz) = You();
        Vector2 c = panel.GetCenter();
        Vector2 Screen(double x, double z)
        {
            var (px, py) = View.ToView(x, z);
            return c + new Vector2((float)px, (float)py);
        }
        // Drag to move, the wheel to zoom, a right click to mark.
        if (!g.Drawing)
        {
            if (g.Over(panel) && _game.Controls.Wheel != 0) View.Zoom(_game.Controls.Wheel);
            if (g.Pressed && g.Over(panel)) _dragFrom = g.Mouse;
            if (_dragFrom is Vector2 from && g.Held)
            {
                View.Drag(g.Mouse.X - from.X, g.Mouse.Y - from.Y);
                _dragFrom = g.Mouse;
            }
            if (g.Released) _dragFrom = null;
            if (g.RightClicked(panel))
            {
                // On one of your own marks: take it down. Anywhere else: make one.
                Charts own = _game.Spaceflight.Charts;
                ChartMark hit = own.OfKind("mark").FirstOrDefault(m => Screen(m.X, m.Z).DistanceTo(g.Mouse) <= MarkHit);
                if (hit != null) own.Unmark(hit.Key);
                else
                {
                    var (mx, mz) = View.FromView(g.Mouse.X - c.X, g.Mouse.Y - c.Y);
                    own.Mark(mx, mz);
                }
            }
        }
        if (g.Drawing)
        {
            if (_chart != null) Tiles(g, panel, View.CentreX, View.CentreZ, View.MetresPerPixel, View.Width, View.Height);
            else g.TextIn("Charting the world…", panel, C.Dim, 18, HorizontalAlignment.Center);
            var labels = new MapLabels();
            void Mark(double x, double z, string label, Color colour, float r, int priority)
            {
                Vector2 q = Screen(x, z);
                if (!panel.HasPoint(q)) return;
                g.Canvas.DrawCircle(q, r, colour);
                labels.Add(q, r, label, colour, priority);
            }
            Charts charts = _game.Spaceflight.Charts;
            foreach (ChartMark m in charts.Marks)
                Mark(m.X, m.Z, m.Kind == "deposit" ? m.Label.ToUpperInvariant() : m.Label.ToUpperInvariant(), m.Kind switch { "mark" => C.Danger, "deposit" => C.Holo, _ => C.Accent2 }, m.Kind == "mark" ? 6f : 4f, m.Kind switch { "place" => 2, "mark" => 3, _ => 4 });
            foreach (Vessel v in _game.Surface.Vessels)
            {
                Vector3 p = v.IsRuin ? _game.Start : v.Body.Position.ToGodot();
                Mark(p.X, p.Z, v.IsRuin ? "BUNKER" : v.Name.ToUpperInvariant(), v.IsRuin ? C.Accent : v.IsBase ? C.Good : C.Text, 6f, 1);
            }
            Mark(ux, uz, "YOU", C.Accent, 7f, 0);
            labels.Flush(g, panel);
        }
        g.Caption("WORLD MAP", side.Position, side.Size.X);
        string[] lines = { "drag to move, wheel to zoom", "right click: mark a place, or", "take down a mark of yours", "it wraps both ways", "found places and deposits", "show here, with your marks" };
        for (int i = 0; i < lines.Length; i++) g.Text(lines[i], side.Position + new Vector2(0, 40 + i * 26), C.Dim, 15);
        g.Text($"{View.MetresPerPixel * panel.Size.X / 1000:F1} KM ACROSS", side.Position + new Vector2(0, 216), C.Text, 15, UiTheme.Mono);
        if (g.Button("map:home", new Rect2(side.Position + new Vector2(0, 250), new Vector2(150, 34)), "ON ME", C.Accent2))
        {
            View.CentreX = ux;
            View.CentreZ = uz;
        }
    }

    /// <summary>The map's picture, as many wrapped copies as it takes to fill the panel.</summary>
    void Tiles(Gui g, Rect2 panel, double centreX, double centreZ, double metresPerPixel, double width, double height)
    {
        float w = (float)(width / metresPerPixel), h = (float)(height / metresPerPixel);
        Vector2 c = panel.GetCenter();
        float ox = c.X - (float)(centreX / metresPerPixel), oy = c.Y - (float)(centreZ / metresPerPixel);
        for (int i = -2; i <= 2; i++)
            for (int j = -2; j <= 2; j++)
            {
                var tile = new Rect2(ox + i * w, oy + j * h, w, h);
                Rect2 shown = tile.Intersection(panel);
                if (shown.Size.X <= 0f || shown.Size.Y <= 0f) continue;
                var source = new Rect2((shown.Position - tile.Position) / tile.Size * _chart.GetSize(), shown.Size / tile.Size * _chart.GetSize());
                g.Canvas.DrawTextureRectRegion(_chart, shown, source);
            }
    }

    // ---- The system -------------------------------------------------------------------------

    void SystemView(Gui g, Rect2 panel, Rect2 side)
    {
        Charts charts = _game.Spaceflight.Charts;
        List<SystemEntry> bodies = Charts.Bodies(_game.Surface.Planet.Sky, "Your world");
        // The outermost orbit fits the panel; nothing is drawn past its edge.
        Vector2 c = panel.GetCenter();
        float far = bodies.Where(e => e.Kind != "moon").Select(e => e.Orbit).DefaultIfEmpty(0f).Max();
        float unit = far > 0f ? panel.Size.Y * 0.47f / far : panel.Size.Y * 0.47f;
        var places = new Dictionary<string, Vector2>();
        Vector2 At(SystemEntry e, Vector2 about) => about + new Vector2(MathF.Cos(e.Bearing), MathF.Sin(e.Bearing)) * e.Orbit * unit;
        Vector2 world = c;
        foreach (SystemEntry e in bodies)
        {
            Vector2 about = e.Kind == "moon" ? world : c;
            Vector2 q = e.Kind == "star" ? c : At(e, about);
            if (e.Kind == "world") world = q;
            places[e.Key] = q;
            if (!g.Drawing) continue;
            if (e.Orbit > 0f) g.Canvas.DrawArc(about, e.Orbit * unit, 0, Mathf.Tau, 96, new Color(C.Rule, 0.6f), 1f);
            float r = e.Size * 70f + 3f;
            g.Canvas.DrawCircle(q, r, e.Kind switch { "star" => C.Accent, "world" => C.Holo, "giant" => C.Accent2, _ => C.Text });
            g.Text(e.Label.ToUpperInvariant(), q + new Vector2(r + 4, -9), C.Text, 13);
        }
        foreach (SystemEntry w in charts.Wrecks)
        {
            Vector2 q = world + new Vector2(MathF.Cos(w.Bearing), MathF.Sin(w.Bearing)) * (0.07f + w.Orbit * 0.08f) * unit;
            places[w.Key] = q;
            if (g.Drawing) g.Canvas.DrawCircle(q, 3f, C.Danger);
        }
        g.Caption("SYSTEM MAP", side.Position, side.Size.X);
        g.Text("travel between planets: warp drive", side.Position + new Vector2(0, 40), C.Dim, 14);
        g.Text("(not fitted). Wrecks by the world:", side.Position + new Vector2(0, 62), C.Dim, 14);
        g.Text("fly there on the route line.", side.Position + new Vector2(0, 84), C.Dim, 14);
        float y = 116;
        foreach (SystemEntry e in charts.Wrecks.Take(7))
        {
            bool picked = charts.Destination == e.Key;
            if (g.Button($"map:go:{e.Key}", new Rect2(side.Position + new Vector2(0, y), new Vector2(310, 28)), e.Label.ToUpperInvariant(), C.Accent2, lit: picked, size: 13)) charts.Destination = picked ? null : e.Key;
            y += 30;
        }
        if (charts.Wrecks.Count == 0) g.Text("No wrecks found yet: reach orbit.", side.Position + new Vector2(0, y), C.Dim, 14);
        if (g.Drawing && charts.Destination != null && places.TryGetValue(charts.Destination, out Vector2 to))
        {
            g.Canvas.DrawDashedLine(world, to, C.Accent, 2f, 8f);
            g.Canvas.DrawCircle(world, 5f, C.Accent);
        }
    }

    // ---- The stars --------------------------------------------------------------------------

    void Stars(Gui g, Rect2 panel, Rect2 side)
    {
        List<StarEntry> stars = Charts.Stars(_game.Surface.Spec.Seed, _game.Surface.Planet.Sky);
        Vector2 c = panel.GetCenter();
        float scale = panel.Size.Y * 0.44f / 30f; // pixels a light year
        if (g.Drawing)
        {
            g.Canvas.DrawArc(c, 30f * scale, 0, Mathf.Tau, 96, new Color(C.Rule, 0.7f), 1f);
            foreach (StarEntry s in stars)
            {
                Vector2 q = c + new Vector2(s.X, s.Y) * scale;
                if (!s.Home) g.Canvas.DrawDashedLine(c, q, new Color(C.Rule, 0.8f), 1f, 6f);
                g.Canvas.DrawCircle(q, s.Home ? 7f : 4.5f, s.Home ? C.Accent : s.Class switch
                {
                    PlanetSurface.Skies.StarClass.RedDwarf => new Color("e85d04"),
                    PlanetSurface.Skies.StarClass.Orange => new Color("f4a100"),
                    PlanetSurface.Skies.StarClass.Yellow => new Color("ffe8c2"),
                    PlanetSurface.Skies.StarClass.White => new Color("f5f3ee"),
                    _ => new Color("6ff2e0"),
                });
                g.Text(s.Home ? "HOME" : $"{s.Name}  {s.Light:F0} LY", q + new Vector2(9, -9), s.Home ? C.Accent : C.Text, 13);
            }
        }
        g.Caption("STAR MAP", side.Position, side.Size.X);
        string[] lines = { "the galaxy's rim: only the", "stars one jump away are known", "travel between stars: hyperdrive", "(capital vessels, fold crystal)", "not fitted", $"{stars.Count - 1} neighbours" };
        for (int i = 0; i < lines.Length; i++) g.Text(lines[i], side.Position + new Vector2(0, 40 + i * 26), C.Dim, 15);
    }
}
