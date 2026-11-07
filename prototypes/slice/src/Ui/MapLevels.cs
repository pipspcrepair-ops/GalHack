using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using GalHack.Physics.Engine;
using GalHack.Stars;
using DiegeticUi.Theme;
using Palette = DiegeticUi.Theme.Palette;
using Godot;
using N = System.Numerics;
using Planets.Look;
using Slice.Orbit;
using Slice.Surface;
using Slice.Vessels;

namespace Slice.Ui;

/// <summary>
/// The Map tab's levels over one set of data (<see cref="MapTrail"/>): the galaxy (a 3D map turned by
/// dragging, of the stars charted and the nebulae, any star there can be picked and opened), a star's
/// system (its bodies and the wrecks found, with a destination and a route line), high orbit (the
/// world as a globe, painted from its own map, with what is round it) and the world (the generator's
/// map, wrapping both ways, dragged and zoomed). The wheel runs through them all: past the end of
/// one level's zoom is the next level, in and back out. Each shows only what has been discovered
/// (<see cref="Charts"/>), and where you are, in the suit terminal's look.
/// </summary>
public sealed class MapLevels
{
    static Palette C => UiTheme.Colors;
    readonly Game.Game _game;
    public readonly MapTrail Trail = new();
    public MapLevel Level
    {
        get => Trail.Level;
        set => Trail.Open(value);
    }
    public WorldMapView View { get; private set; }
    public GalaxyMapView Sky { get; private set; }
    Task<Image> _painting;
    ImageTexture _chart;
    Vector2? _dragFrom;

    public MapLevels(Game.Game game) => _game = game;

    /// <summary>What the panel's picture depends on, so dragging and zooming redraw it at once.</summary>
    public string Signature => $"{Level}|{View?.CentreX:F0}|{View?.CentreZ:F0}|{View?.MetresPerPixel:F2}|{_chart != null}|{_game.Spaceflight.Charts.Marks.Count()}|{_game.Spaceflight.Charts.Wrecks.Count}|{_game.Spaceflight.Charts.Destination}|{Trail.Star}|{_game.Spaceflight.Charts.ChosenStar}|{Sky?.Yaw:F3}|{Sky?.Pitch:F3}|{Sky?.Distance:F1}|{Sky?.Focus}|{_orbitX:F0}|{_orbitZ:F0}";

    GalaxySky Stars => _game.Surface.Sky.Stars;
    Galaxy Catalogue => Stars?.Galaxy;

    /// <summary>The level tabs (under the legend), and the way up and down the trail.</summary>
    public void Tabs(Gui g, Vector2 at)
    {
        Galaxy galaxy = Catalogue;
        if (galaxy != null && Stars.Here >= 0 && Trail.You != Stars.Here)
        {
            Trail.You = Trail.Star = Stars.Here;
            _game.Spaceflight.Charts.Visited.Add(Stars.Here);
        }
        MapLevel[] levels = { MapLevel.Local, MapLevel.World, MapLevel.HighOrbit, MapLevel.System, MapLevel.Galaxy };
        for (int i = 0; i < levels.Length; i++)
            if (g.Tab($"map:{levels[i]}", new Rect2(at + new Vector2(i % 2 * 156, i / 2 * 38), new Vector2(150, 34)), MapTrail.Name(levels[i]), Level == levels[i], C.Accent2)) Level = levels[i];
        // The trail: where this level sits, from the galaxy down.
        string star = galaxy != null && Trail.Star >= 0 ? galaxy.Name(Trail.Star) : "star";
        g.Text(string.Join(" > ", Trail.Crumbs(star).Select(c => c.name)), at + new Vector2(0, 122), C.Dim, 13);
        var up = new Rect2(at + new Vector2(0, 146), new Vector2(150, 34));
        var down = new Rect2(at + new Vector2(156, 146), new Vector2(150, 34));
        if (g.Button("map:up", up, "UP", C.Accent2, enabled: Level != MapLevel.Galaxy)) Trail.Up();
        if (g.Button("map:down", down, "DOWN", C.Accent2, enabled: Trail.CanGoDown)) Trail.Down();
    }

    ulong _wheelMs;

    /// <summary>The wheel past the end of a level's zoom: one level in or out, a moment apart, so a spin doesn't run down all of them.</summary>
    public void WheelPast(int wheel)
    {
        if (wheel == 0 || Time.GetTicksMsec() - _wheelMs < 350) return;
        _wheelMs = Time.GetTicksMsec();
        if (wheel > 0) Trail.Up();
        else Trail.Down();
    }

    /// <summary>Draws the level that isn't the local map. False for the local map (the section draws it).</summary>
    public bool Build(Gui g, Rect2 area)
    {
        if (Level == MapLevel.Local) return false;
        var panel = new Rect2(area.Position, new Vector2(area.Size.X - 360, area.Size.Y));
        g.Fill(panel, new Color(C.Panel, 0.8f));
        g.Outline(panel, C.Rule, 2f);
        var side = new Rect2(panel.End.X + 30, area.Position.Y, 320, area.Size.Y);
        // These two have no zoom of their own: the wheel goes straight to the next level.
        if (!g.Drawing && Level is MapLevel.System or MapLevel.HighOrbit && g.Over(panel)) WheelPast(_game.Controls.Wheel);
        switch (Level)
        {
            case MapLevel.World: World(g, panel, side); break;
            case MapLevel.HighOrbit: HighOrbit(g, panel, side); break;
            case MapLevel.System:
                if (Trail.AtYourStar) SystemView(g, panel, side);
                else Uncharted(g, panel, side);
                break;
            default: GalaxyLevel(g, panel, side); break;
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
            int wheel = _game.Controls.Wheel;
            if (g.Over(panel) && wheel != 0)
            {
                if ((wheel < 0 && View.AtClosest) || (wheel > 0 && View.AtWidest)) WheelPast(wheel);
                else View.Zoom(wheel);
            }
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
            if (e.Kind == "world")
            {
                world = q;
                // Your world opens high orbit.
                if (!g.Drawing && g.Pressed && g.Mouse.DistanceTo(q) < 16f) Trail.Down();
            }
            places[e.Key] = q;
            if (!g.Drawing) continue;
            if (e.Orbit > 0f) g.Canvas.DrawArc(about, e.Orbit * unit, 0, Mathf.Tau, 96, new Color(C.Rule, 0.6f), 1f);
            float r = e.Size * 70f + 3f;
            g.Canvas.DrawCircle(q, r, e.Kind switch { "star" => StarColour(Trail.Star), "world" => C.Holo, "giant" => C.Accent2, _ => C.Text });
            g.Text(e.Label.ToUpperInvariant(), q + new Vector2(r + 4, -9), C.Text, 13);
            if (e.Kind == "world")
            {
                g.Canvas.DrawArc(q, r + 7f, 0, Mathf.Tau, 32, C.Accent, 2f);
                g.Text("YOU ARE HERE", q + new Vector2(r + 4, 8), C.Accent, 12);
            }
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

    Color StarColour(int id)
    {
        if (Catalogue == null || id < 0) return C.Accent;
        N.Vector3 tint = SkyView.Colour(Catalogue.Stars[id].Kind);
        return new Color(tint.X, tint.Y, tint.Z);
    }

    /// <summary>A star not yet visited: all that is known is its catalogue entry. Its system is generated on the first visit.</summary>
    void Uncharted(Gui g, Rect2 panel, Rect2 side)
    {
        Vector2 c = panel.GetCenter();
        if (g.Drawing)
        {
            g.Canvas.DrawCircle(c, 26f, StarColour(Trail.Star));
            for (int k = 1; k <= 3; k++) g.Canvas.DrawArc(c, 90f * k, 0, Mathf.Tau, 64, new Color(C.Rule, 0.35f), 1f);
            g.TextIn("NOT VISITED: SYSTEM NOT SURVEYED", new Rect2(c + new Vector2(-300, 40), new Vector2(600, 30)), C.Dim, 16, HorizontalAlignment.Center);
        }
        g.Caption("SYSTEM MAP", side.Position, side.Size.X);
        StarFacts(g, side.Position + new Vector2(0, 40), Trail.Star);
    }

    /// <summary>What the catalogue says of a star on the chart.</summary>
    void StarFacts(Gui g, Vector2 at, int id)
    {
        Galaxy galaxy = Catalogue;
        Star s = galaxy.Stars[id];
        float light = N.Vector3.Distance(s.Position, galaxy.Stars[Trail.You].Position);
        var lines = new List<string> { galaxy.Name(id).ToUpperInvariant() };
        lines.Add(GalaxyNames.Kind(s.Kind) + (s.Companions == 1 ? ", double" : s.Companions > 1 ? ", triple" : ""));
        lines.Add(GalaxyNames.Of(s.Region));
        if (s.Nebula >= 0) lines.Add("in the " + galaxy.Nebulae[s.Nebula].Name);
        lines.Add(id == Trail.You ? "you are here" : $"{light:F1} LY away{(light <= Galaxy.JumpT1 ? ", one jump" : "")}");
        if (id != Trail.You && Stars.View != null)
        {
            Vector3 look = _game.Spaceflight.LookToward(id);
            lines.Add($"in your sky: {(MathF.Atan2(look.X, -look.Z) * 180f / MathF.PI + 360f) % 360f:000} deg, {MathF.Asin(Math.Clamp(look.Y, -1f, 1f)) * 180f / MathF.PI:+00;-00} up");
        }
        for (int i = 0; i < lines.Count; i++) g.Text(lines[i], at + new Vector2(0, i * 24), i == 0 ? C.Text : C.Dim, 15);
    }

    // ---- High orbit -----------------------------------------------------------------------------

    double _orbitX = double.NaN, _orbitZ;

    /// <summary>
    /// The world as a globe, painted from its own map: a place on it is the same place on the world
    /// map. Your spot is marked, it can be turned by dragging, and a click opens the world map there.
    /// What stands round it comes from the system's bodies and the wrecks found, by bearing only,
    /// until the system model gives them true orbits.
    /// </summary>
    void HighOrbit(Gui g, Rect2 panel, Rect2 side)
    {
        var planet = _game.Surface.Planet;
        Paint();
        var (ux, uz) = You();
        if (double.IsNaN(_orbitX)) (_orbitX, _orbitZ) = (ux, uz);
        Vector2 c = panel.GetCenter();
        float R = panel.Size.Y * 0.36f;
        var map = new GlobeMap(planet.Width, planet.Height, R);
        System.Numerics.Quaternion frame = map.Frame(_orbitX, _orbitZ);
        System.Numerics.Vector3 east = System.Numerics.Vector3.Transform(System.Numerics.Vector3.UnitX, frame), up = System.Numerics.Vector3.Transform(System.Numerics.Vector3.UnitY, frame), south = System.Numerics.Vector3.Transform(System.Numerics.Vector3.UnitZ, frame);
        System.Numerics.Vector3 Globe(Vector2 q) => up * MathF.Sqrt(MathF.Max(1f - q.LengthSquared(), 0f)) + east * q.X + south * q.Y;
        if (!g.Drawing && g.Over(panel))
        {
            Vector2 q = (g.Mouse - c) / R;
            if (g.Pressed)
            {
                _dragFrom = g.Mouse;
                _dragged = false;
            }
            if (_dragFrom is Vector2 from && g.Held && from.DistanceTo(g.Mouse) > 3f)
            {
                _dragged = true;
                _orbitX = Planets.Gen.Mathx.WrapX(_orbitX - (g.Mouse.X - from.X) / (R * Mathf.Tau) * planet.Width, planet.Width);
                _orbitZ = Math.Clamp(_orbitZ - (g.Mouse.Y - from.Y) / (R * Mathf.Pi) * planet.Height, planet.Height * 0.02, planet.Height * 0.98);
                _dragFrom = g.Mouse;
            }
            if (g.Released)
            {
                if (!_dragged && q.LengthSquared() < 1f)
                {
                    var (mx, mz) = map.MapPoint(Globe(q));
                    View ??= new WorldMapView(planet.Width, planet.Height, planet.Width / panel.Size.X);
                    (View.CentreX, View.CentreZ) = (mx, mz);
                    Level = MapLevel.World;
                }
                _dragFrom = null;
            }
        }
        if (g.Drawing)
        {
            if (_chart != null)
            {
                g.Canvas.TextureRepeat = CanvasItem.TextureRepeatEnum.Enabled;
                const int Rings = 26, Sectors = 72;
                var q = new Vector2[4];
                var uv = new Vector2[4];
                var white = new[] { Colors.White, Colors.White, Colors.White, Colors.White };
                Vector2 Point(int ring, int sector) => new Vector2(MathF.Cos(sector * Mathf.Tau / Sectors), MathF.Sin(sector * Mathf.Tau / Sectors)) * MathF.Sin(ring / (float)Rings * Mathf.Pi * 0.5f);
                for (int ring = 0; ring < Rings; ring++)
                for (int sector = 0; sector < Sectors; sector++)
                {
                    q[0] = Point(ring, sector);
                    q[1] = Point(ring + 1, sector);
                    q[2] = Point(ring + 1, sector + 1);
                    q[3] = Point(ring, sector + 1);
                    float first = 0f;
                    for (int k = 0; k < 4; k++)
                    {
                        var (mx, mz) = map.MapPoint(Globe(q[k]));
                        float u = (float)(mx / planet.Width), v = (float)(mz / planet.Height);
                        if (k == 0) first = u;
                        else if (u - first > 0.5f) u -= 1f;
                        else if (first - u > 0.5f) u += 1f;
                        uv[k] = new Vector2(u + 1f, v);
                    }
                    var points = new[] { c + q[0] * R, c + q[1] * R, c + q[2] * R, c + q[3] * R };
                    g.Canvas.DrawPolygon(points, white, uv, _chart);
                }
            }
            else g.TextIn("Charting the world…", panel, C.Dim, 18, HorizontalAlignment.Center);
            g.Canvas.DrawArc(c, R, 0, Mathf.Tau, 96, new Color(C.Holo, 0.7f), 2f);
            // Your spot, if it is on this side of the globe.
            System.Numerics.Vector3 you = map.Direction(ux, uz);
            float facing = System.Numerics.Vector3.Dot(you, up);
            if (facing > 0f)
            {
                Vector2 at = c + new Vector2(System.Numerics.Vector3.Dot(you, east), System.Numerics.Vector3.Dot(you, south)) * R;
                g.Canvas.DrawCircle(at, 6f, C.Accent);
                g.Canvas.DrawArc(at, 11f, 0, Mathf.Tau, 24, C.Accent, 2f);
                g.Text("YOU", at + new Vector2(14, -9), C.Accent, 14);
            }
            float reach = R * 1.3f;
            foreach (SystemEntry e in Charts.Bodies(_game.Surface.Planet.Sky, "Your world").Where(e => e.Kind is "moon" or "giant"))
            {
                Vector2 q = c + new Vector2(MathF.Cos(e.Bearing), MathF.Sin(e.Bearing)) * reach;
                g.Canvas.DrawCircle(q, e.Kind == "giant" ? 9f : 5f, e.Kind == "giant" ? C.Accent2 : C.Text);
                g.Text(e.Label.ToUpperInvariant(), q + new Vector2(10, -9), C.Text, 13);
            }
            foreach (SystemEntry w in _game.Spaceflight.Charts.Wrecks)
                g.Canvas.DrawCircle(c + new Vector2(MathF.Cos(w.Bearing), MathF.Sin(w.Bearing)) * (R * 1.08f), 3f, C.Danger);
        }
        g.Caption("HIGH ORBIT", side.Position, side.Size.X);
        string[] lines = { "drag to turn the world", "click: the world map there", $"{planet.Width / 1000:F0} by {planet.Height / 1000:F0} km of map", "moons and wrecks by bearing:", "true orbits come with the", "system model" };
        for (int i = 0; i < lines.Length; i++) g.Text(lines[i], side.Position + new Vector2(0, 40 + i * 26), C.Dim, 15);
        if (g.Button("map:orbit-home", new Rect2(side.Position + new Vector2(0, 250), new Vector2(150, 34)), "ON ME", C.Accent2)) (_orbitX, _orbitZ) = (ux, uz);
    }

    // ---- The galaxy -----------------------------------------------------------------------------

    bool _dragged;
    ImageTexture _soft;
    Vector2 _mapMiddle;
    float _mapFocal;

    /// <summary>Where a star is on the galaxy map just now, on the screen, or null.</summary>
    public Vector2? ScreenOf(int star)
    {
        if (Sky == null || !Sky.Project(Catalogue.Stars[star].Position, _mapFocal, out N.Vector2 at, out _)) return null;
        return _mapMiddle + new Vector2(at.X, at.Y);
    }

    /// <summary>A round soft-edged blot, drawn over and over at each nebula lobe's own place and size.</summary>
    ImageTexture Soft()
    {
        if (_soft != null) return _soft;
        const int Size = 64;
        var bytes = new byte[Size * Size * 2];
        for (int y = 0; y < Size; y++)
        for (int x = 0; x < Size; x++)
        {
            float dx = (x + 0.5f) / Size * 2f - 1f, dy = (y + 0.5f) / Size * 2f - 1f, k = MathF.Max(1f - (dx * dx + dy * dy), 0f);
            bytes[(x + y * Size) * 2] = 255;
            bytes[(x + y * Size) * 2 + 1] = (byte)(255f * k * k);
        }
        return _soft = ImageTexture.CreateFromImage(Image.CreateFromData(Size, Size, false, Image.Format.La8, bytes));
    }

    /// <summary>
    /// The galaxy in 3D, turned by dragging, zoomed by the wheel. The disc's plane is drawn as rings,
    /// and each star charted hangs over it on a thin line, so its depth reads at a glance. Nebulae
    /// stand at their real place and size. The sphere round you is a jump's true 3D reach. A click
    /// picks a star, the sky marks it, and the wheel zooms in on it and on down into its system.
    /// </summary>
    void GalaxyLevel(Gui g, Rect2 panel, Rect2 side)
    {
        Galaxy galaxy = Catalogue;
        Charts charts = _game.Spaceflight.Charts;
        g.Caption("GALAXY MAP", side.Position, side.Size.X);
        if (galaxy == null || Trail.You < 0)
        {
            g.TextIn("Charting the galaxy…", panel, C.Dim, 18, HorizontalAlignment.Center);
            return;
        }
        Star you = galaxy.Stars[Trail.You];
        Sky ??= new GalaxyMapView { Focus = you.Position };
        Vector2 c = _mapMiddle = panel.GetCenter();
        float focal = _mapFocal = panel.Size.Y * 1.1f;
        bool At(N.Vector3 p, out Vector2 q, out float depth)
        {
            bool front = Sky.Project(p, focal, out N.Vector2 v, out depth);
            q = c + new Vector2(v.X, v.Y);
            return front;
        }
        List<int> shown = charts.Shown(galaxy);
        if (!g.Drawing)
        {
            int wheel = _game.Controls.Wheel;
            if (g.Over(panel) && wheel != 0)
            {
                int aim = charts.ChosenStar >= 0 ? charts.ChosenStar : Trail.You;
                if (wheel < 0 && Sky.AtClosest)
                {
                    if (Time.GetTicksMsec() - _wheelMs >= 350)
                    {
                        _wheelMs = Time.GetTicksMsec();
                        Trail.OpenStar(aim);
                    }
                    return;
                }
                if (wheel < 0) Sky.Focus = N.Vector3.Lerp(Sky.Focus, galaxy.Stars[aim].Position, 0.35f);
                Sky.Zoom(wheel);
            }
            if (g.Pressed && g.Over(panel))
            {
                _dragFrom = g.Mouse;
                _dragged = false;
            }
            if (_dragFrom is Vector2 from && g.Held && (_dragged || from.DistanceTo(g.Mouse) > 3f))
            {
                Sky.Turn(g.Mouse.X - from.X, g.Mouse.Y - from.Y);
                _dragged = true;
                _dragFrom = g.Mouse;
            }
            if (g.Released)
            {
                if (_dragFrom != null && !_dragged && g.Over(panel))
                {
                    int picked = -1;
                    float nearest = 14f * 14f;
                    foreach (int id in shown)
                        if (At(galaxy.Stars[id].Position, out Vector2 q, out _) && q.DistanceSquaredTo(g.Mouse) < nearest) (nearest, picked) = (q.DistanceSquaredTo(g.Mouse), id);
                    if (picked >= 0)
                    {
                        charts.ChosenStar = picked;
                        Trail.Star = picked;
                    }
                }
                _dragFrom = null;
            }
        }
        if (g.Drawing)
        {
            g.Fill(panel, new Color(0.008f, 0.01f, 0.025f, 0.92f));
            DrawPlane(g, panel, you, At);
            DrawNebulae(g, panel, galaxy, shown, At);
            var labels = new MapLabels();
            // The jump's reach: the sphere's outline, and its ring through you, level with the disc.
            if (At(you.Position, out Vector2 home, out float homeDepth))
            {
                float reach = Galaxy.JumpT1 * focal / homeDepth;
                if (reach > 6f) g.Canvas.DrawArc(home, reach, 0, Mathf.Tau, 96, new Color(C.Accent, 0.6f), 1.5f);
                Ring(g, panel, you.Position, Galaxy.JumpT1, new Color(C.Accent, 0.28f), 1f, At);
            }
            foreach (int id in shown.OrderByDescending(id => { At(galaxy.Stars[id].Position, out _, out float d); return d; }))
            {
                Star s = galaxy.Stars[id];
                if (!At(s.Position, out Vector2 q, out float depth) || !panel.HasPoint(q)) continue;
                // The thin line down to the plane: its length and side are the star's height.
                if (At(s.Position with { Y = 0f }, out Vector2 foot, out _) && panel.HasPoint(foot))
                {
                    Color line = s.Position.Y >= 0f ? new Color(C.Holo, 0.5f) : new Color(C.Warn, 0.5f);
                    g.Canvas.DrawLine(q, foot, line, 1f);
                    g.Canvas.DrawCircle(foot, 1.5f, line);
                }
                float r = Math.Clamp((3.2f + 0.8f * MathF.Log10(s.Light + 0.01f)) * MathF.Sqrt(96f / depth), 2.4f, 8f);
                g.Canvas.DrawCircle(q, r, StarColour(id));
                if (id != Trail.You && id != charts.ChosenStar) labels.Add(q, r, depth < 220f ? galaxy.Name(id).ToUpperInvariant() : null, C.Text, 5);
            }
            if (At(you.Position, out Vector2 me, out _) && panel.HasPoint(me))
            {
                g.Canvas.DrawArc(me, 9f, 0, Mathf.Tau, 24, C.Accent, 2f);
                labels.Add(me, 9f, "YOU", C.Accent, 0);
            }
            if (charts.ChosenStar >= 0 && At(galaxy.Stars[charts.ChosenStar].Position, out Vector2 chosen, out _) && panel.HasPoint(chosen))
            {
                g.Canvas.DrawArc(chosen, 12f, 0, Mathf.Tau, 24, C.Holo, 2f);
                if (panel.HasPoint(me)) g.Canvas.DrawDashedLine(me, chosen, new Color(C.Holo, 0.8f), 1f, 6f);
                labels.Add(chosen, 12f, galaxy.Name(charts.ChosenStar).ToUpperInvariant(), C.Holo, 1);
            }
            if (At(N.Vector3.Zero, out Vector2 core, out _) && panel.HasPoint(core)) labels.Add(core, 4f, "CORE", C.Dim, 3);
            labels.Flush(g, panel);
        }
        int facts = charts.ChosenStar >= 0 ? charts.ChosenStar : Trail.You;
        StarFacts(g, side.Position + new Vector2(0, 40), facts);
        if (g.Button("map:open-star", new Rect2(side.Position + new Vector2(0, 208), new Vector2(150, 34)), "OPEN SYSTEM", C.Accent2)) Trail.OpenStar(facts);
        if (g.Button("map:clear-star", new Rect2(side.Position + new Vector2(156, 208), new Vector2(150, 34)), "CLEAR MARK", C.Accent2, enabled: charts.ChosenStar >= 0)) charts.ChosenStar = -1;
        if (g.Button("map:on-me", new Rect2(side.Position + new Vector2(0, 248), new Vector2(150, 34)), "ON ME", C.Accent2))
        {
            Sky.Focus = you.Position;
            Sky.Distance = 96;
        }
        if (g.Button("map:whole", new Rect2(side.Position + new Vector2(156, 248), new Vector2(150, 34)), "WHOLE GALAXY", C.Accent2, size: 15))
        {
            Sky.Focus = N.Vector3.Zero;
            Sky.Distance = GalaxyMapView.Widest;
        }
        g.Text($"{shown.Count} stars charted, drag to turn", side.Position + new Vector2(0, 294), C.Dim, 14);
    }

    delegate bool Projects(N.Vector3 p, out Vector2 q, out float depth);

    /// <summary>A ring level with the galaxy's disc, as far as it stands inside the panel.</summary>
    static void Ring(Gui g, Rect2 panel, N.Vector3 centre, float radius, Color colour, float width, Projects at)
    {
        const int Steps = 96;
        Vector2 last = default;
        bool had = false;
        for (int i = 0; i <= Steps; i++)
        {
            float a = i * Mathf.Tau / Steps;
            bool ok = at(centre + new N.Vector3(MathF.Cos(a), 0f, MathF.Sin(a)) * radius, out Vector2 q, out _) && panel.HasPoint(q);
            if (ok && had) g.Canvas.DrawLine(last, q, colour, width);
            (last, had) = (q, ok);
        }
    }

    /// <summary>The galaxy's plane: rings every hundred light years round the core, and finer ones under you.</summary>
    static void DrawPlane(Gui g, Rect2 panel, Star you, Projects at)
    {
        for (int r = 100; r <= 500; r += 100) Ring(g, panel, N.Vector3.Zero, r, new Color(C.Rule, r == 500 ? 0.9f : 0.55f), 1f, at);
        var under = you.Position with { Y = 0f };
        for (int r = 10; r <= 40; r += 10) Ring(g, panel, under, r, new Color(C.Rule, 0.4f), 1f, at);
    }

    /// <summary>Every nebula, each lobe a soft blot at its real place and size; those with charted stars in them are named.</summary>
    void DrawNebulae(Gui g, Rect2 panel, Galaxy galaxy, List<int> shown, Projects at)
    {
        var known = new HashSet<int>(shown.Select(id => (int)galaxy.Stars[id].Nebula));
        foreach (Nebula n in galaxy.Nebulae)
        {
            Color colour = n.Kind == NebulaKind.Dark ? new Color(0.16f, 0.12f, 0.1f, 0.55f) : new Color(n.Colour.X, n.Colour.Y, n.Colour.Z, 0.34f);
            float mean = (n.Axes.X + n.Axes.Y + n.Axes.Z) / 3f;
            foreach (var (lobe, radius) in n.Lobes)
            {
                N.Vector3 p = n.Centre + N.Vector3.Transform(lobe * n.Axes * n.Radius, n.Turn);
                if (!at(p, out Vector2 q, out float depth)) continue;
                float r = radius * n.Radius * mean * _mapFocal / depth;
                var box = new Rect2(q - new Vector2(r, r), new Vector2(r * 2f, r * 2f));
                Rect2 seen = box.Intersection(panel);
                if (seen.Size.X <= 0f || seen.Size.Y <= 0f) continue;
                var source = new Rect2((seen.Position - box.Position) / box.Size * 64f, seen.Size / box.Size * 64f);
                g.Canvas.DrawTextureRectRegion(Soft(), seen, source, colour);
            }
            if (known.Contains(n.Id) && at(n.Centre, out Vector2 middle, out _) && panel.HasPoint(middle)) g.Text(n.Name.ToUpperInvariant(), middle, new Color(n.Colour.X, n.Colour.Y, n.Colour.Z), 12);
        }
    }
}
