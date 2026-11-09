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
    bool _homed;

    public MapLevels(Game.Game game) => _game = game;

    /// <summary>What the panel's picture depends on, so dragging and zooming redraw it at once.</summary>
    public string Signature => $"{Level}|{View?.CentreX:F0}|{View?.CentreZ:F0}|{View?.MetresPerPixel:F2}|{_chart != null}|{_game.Spaceflight.Charts.Marks.Count()}|{_game.Spaceflight.Charts.Wrecks.Count}|{_game.Spaceflight.Charts.Destination}|{Trail.Star}|{_game.Spaceflight.Charts.ChosenStar}|{Sky?.Yaw:F3}|{Sky?.Pitch:F3}|{Sky?.Distance:F1}|{Sky?.Focus}|{_orbitX:F0}|{_orbitZ:F0}|{_orbitZoom:F2}|{_orbitPan.X:F0},{_orbitPan.Y:F0}|{_sysZoom:F2}|{_sysPan.X:F0},{_sysPan.Y:F0}|{LocalPan.X:F0},{LocalPan.Y:F0}|{LocalRange:F0}|{_body}|{(Level is MapLevel.System or MapLevel.HighOrbit ? _game.Surface.Sky.Days : 0.0):F3}";

    GalaxySky Stars => _game.Surface.Sky.Stars;

    StarProfile _profile;
    Galaxy _profileOf;

    /// <summary>What each star holds, built on demand and kept (null until the catalogue is there).</summary>
    StarProfile Profile
    {
        get
        {
            Galaxy g = Catalogue;
            if (g == null) return null;
            if (_profileOf != g) (_profile, _profileOf) = (new StarProfile(g), g);
            return _profile;
        }
    }
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
        // An unsurveyed system has no zoom: the wheel goes straight to the next level.
        switch (Level)
        {
            case MapLevel.World: World(g, panel, side); break;
            case MapLevel.HighOrbit: HighOrbit(g, panel, side); break;
            case MapLevel.System:
                if (Trail.AtYourStar || Profile?.System(Trail.Star) != null) SystemView(g, panel, side);
                else Uncharted(g, panel, side);
                break;
            default: GalaxyLevel(g, panel, side); break;
        }
        Tabs(g, side.Position + new Vector2(0, 330));
        return true;
    }

    // ---- Zoom and pan, shared by every level -------------------------------------------------

    /// <summary>The wheel and the + and - keys this pass: negative zooms in, positive out. The wheel only counts over the panel.</summary>
    public int ZoomSteps(Gui g, Rect2 panel) => Steps(g, panel);

    public Vector2 SlideBy(Gui g, Rect2 panel) => MiddleDrag(g, panel);

    int Steps(Gui g, Rect2 panel)
    {
        Game.Controls c = _game.Controls;
        int w = g.Over(panel) ? (_game.Interactive ? -g.Wheel : c.Wheel) : 0;
        if (c.KeyWasPressed(Key.Equal) || c.KeyWasPressed(Key.KpAdd)) w--;
        if (c.KeyWasPressed(Key.Minus) || c.KeyWasPressed(Key.KpSubtract)) w++;
        return w;
    }

    Vector2? _middleFrom;

    /// <summary>How far the pointer moved this pass with the middle button held down over the panel (pixels), or zero.</summary>
    Vector2 MiddleDrag(Gui g, Rect2 panel)
    {
        if (g.Drawing) return Vector2.Zero;
        if (!g.MiddleHeld)
        {
            _middleFrom = null;
            return Vector2.Zero;
        }
        if (_middleFrom is not Vector2 from)
        {
            if (g.Over(panel)) _middleFrom = g.Mouse;
            return Vector2.Zero;
        }
        _middleFrom = g.Mouse;
        return g.Mouse - from;
    }

    /// <summary>A line cut to a rectangle (Liang-Barsky), so a zoomed map never draws outside its panel.</summary>
    public static void Clipped(CanvasItem canvas, Rect2 r, Vector2 a, Vector2 b, Color colour, float width)
    {
        float t0 = 0f, t1 = 1f;
        Vector2 d = b - a;
        float[] p = { -d.X, d.X, -d.Y, d.Y };
        float[] q = { a.X - r.Position.X, r.End.X - a.X, a.Y - r.Position.Y, r.End.Y - a.Y };
        for (int i = 0; i < 4; i++)
        {
            if (p[i] == 0f) { if (q[i] < 0f) return; continue; }
            float t = q[i] / p[i];
            if (p[i] < 0f) t0 = MathF.Max(t0, t);
            else t1 = MathF.Min(t1, t);
            if (t0 > t1) return;
        }
        canvas.DrawLine(a + d * t0, a + d * t1, colour, width);
    }

    /// <summary>A circle's outline, cut to a rectangle.</summary>
    public static void ArcIn(Gui g, Rect2 r, Vector2 centre, float radius, Color colour, float width = 1f)
    {
        if (!g.Drawing || radius < 0.5f) return;
        Vector2 nearest = new(Math.Clamp(centre.X, r.Position.X, r.End.X), Math.Clamp(centre.Y, r.Position.Y, r.End.Y));
        if (centre.DistanceTo(nearest) > radius) return;
        float far = MathF.Max(MathF.Max(centre.DistanceTo(r.Position), centre.DistanceTo(r.End)), MathF.Max(centre.DistanceTo(new Vector2(r.Position.X, r.End.Y)), centre.DistanceTo(new Vector2(r.End.X, r.Position.Y))));
        if (radius > far) return;
        int steps = Math.Clamp((int)(radius * 0.5f), 48, 600);
        Vector2 last = centre + new Vector2(radius, 0f);
        for (int i = 1; i <= steps; i++)
        {
            float a = i * Mathf.Tau / steps;
            Vector2 next = centre + new Vector2(MathF.Cos(a), MathF.Sin(a)) * radius;
            Clipped(g.Canvas, r, last, next, colour, width);
            last = next;
        }
    }

    /// <summary>Where the local map is panned to, metres from you (middle drag).</summary>
    public Vector2 LocalPan;
    public float LocalRange;

    // ---- The world --------------------------------------------------------------------------

    /// <summary>How close, in pixels, a right click must be to take down one of your marks.</summary>
    public const float MarkHit = 12f;

    /// <summary>The generator's terrain colours behind the local map: <paramref name="halfMetres"/> each way from you. False while it is still being charted.</summary>
    public bool Terrain(Gui g, Rect2 panel, float halfMetres, Vector2 pan = default)
    {
        Paint();
        if (_chart == null || !g.Drawing) return false;
        var (ux, uz) = You();
        var planet = _game.Surface.Planet;
        Tiles(g, panel, ux + pan.X, uz + pan.Y, 2.0 * halfMetres / panel.Size.X, planet.Width, planet.Height);
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
            byte[] rgb = colors.Composite();
            // The world's blasts as its ground has them: the dead ash round each, and its crater.
            planet.Palette.PaintBlasts(rgb, colors.Width, colors.Height);
            return Image.CreateFromData(colors.Width, colors.Height, false, Image.Format.Rgb8, rgb);
        });
        if (_chart == null && _painting.IsCompletedSuccessfully) _chart = ImageTexture.CreateFromImage(_painting.Result);
    }

    void World(Gui g, Rect2 panel, Rect2 side)
    {
        var planet = _game.Surface.Planet;
        Paint();
        if (View == null)
        {
            // Poles at the top and bottom, the equator across the middle; east-west on you.
            View = new WorldMapView(planet.Width, planet.Height, planet.Width / panel.Size.X) { PanelHeight = panel.Size.Y };
            View.CentreZ = planet.Height / 2;
        }
        View.PanelHeight = panel.Size.Y;
        View.Settle();
        var (ux, uz) = You();
        if (!_homed)
        {
            _homed = true;
            View.CentreX = ux;
        }
        Vector2 c = panel.GetCenter();
        Vector2 Screen(double x, double z)
        {
            var (px, py) = View.ToView(x, z);
            return c + new Vector2((float)px, (float)py);
        }
        // Drag to move, the wheel to zoom, a right click to mark.
        if (!g.Drawing)
        {
            int wheel = Steps(g, panel);
            if (wheel != 0)
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
            Vector2 slide = MiddleDrag(g, panel);
            if (slide != Vector2.Zero) View.Drag(slide.X, slide.Y);
            if (g.RightClicked(panel))
            {
                // On one of your own marks: take it down. Anywhere else: make one.
                Charts own = _game.Spaceflight.Charts;
                ChartMark hit = own.OfKind("mark").FirstOrDefault(m => Screen(m.X, m.Z).DistanceTo(g.Mouse) <= MarkHit);
                if (hit != null) own.Unmark(hit.Key);
                else
                {
                    var (mx, mz) = View.FromView(g.Mouse.X - c.X, g.Mouse.Y - c.Y);
                    if (!View.Past(mz)) own.Mark(mx, mz);
                }
            }
        }
        if (g.Drawing)
        {
            if (_chart != null) Tiles(g, panel, View.CentreX, View.CentreZ, View.MetresPerPixel, View.Width, View.Height, false);
            else g.TextIn("CHARTING…", panel, C.Dim, 18, HorizontalAlignment.Center);
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
        string[] lines = { "[DRAG] MOVE", "[WHEEL] ZOOM", "[RMB] MARK · UNMARK" };
        for (int i = 0; i < lines.Length; i++) g.Text(lines[i], side.Position + new Vector2(0, 40 + i * 26), C.Dim, 15);
        g.Text($"{View.MetresPerPixel * panel.Size.X / 1000:F1} KM ACROSS", side.Position + new Vector2(0, 216), C.Text, 15, UiTheme.Mono);
        if (g.Button("map:home", new Rect2(side.Position + new Vector2(0, 250), new Vector2(150, 34)), "ON ME", C.Accent2))
        {
            View.CentreX = ux;
            View.CentreZ = uz;
            View.Settle();
        }
    }

    /// <summary>The map's picture, as many wrapped copies as it takes to fill the panel.</summary>
    void Tiles(Gui g, Rect2 panel, double centreX, double centreZ, double metresPerPixel, double width, double height, bool wrapNorthSouth = true)
    {
        float w = (float)(width / metresPerPixel), h = (float)(height / metresPerPixel);
        Vector2 c = panel.GetCenter();
        float ox = c.X - (float)(centreX / metresPerPixel), oy = c.Y - (float)(centreZ / metresPerPixel);
        for (int i = -2; i <= 2; i++)
            for (int j = wrapNorthSouth ? -2 : 0; j <= (wrapNorthSouth ? 2 : 0); j++)
            {
                var tile = new Rect2(ox + i * w, oy + j * h, w, h);
                Rect2 shown = tile.Intersection(panel);
                if (shown.Size.X <= 0f || shown.Size.Y <= 0f) continue;
                var source = new Rect2((shown.Position - tile.Position) / tile.Size * _chart.GetSize(), shown.Size / tile.Size * _chart.GetSize());
                g.Canvas.DrawTextureRectRegion(_chart, shown, source);
            }
    }

    // ---- The system -------------------------------------------------------------------------

    string _body;

    /// <summary>A dot's size on the system map, pixels: by kind, not to scale, or the giants would hide the rest.</summary>
    static float DotOf(SystemEntry e) => e.Kind switch { "star" => 15f, "giant" => 8f + e.Size * 8f, "world" => 7f, "planet" => 4f + e.Size * 8f, "moon" => 2.5f, _ => 0f };

    /// <summary>
    /// The whole system from the model, on the shared clock: the star, every world on its orbit at
    /// its true bearing, the belts as bands, and each moon round its world at its true bearing, as
    /// they stand this moment. Labels go through <see cref="MapLabels"/>, so none lands on another.
    /// </summary>
    float _sysZoom = 1f;
    Vector2 _sysPan;

    void SystemView(Gui g, Rect2 panel, Rect2 side)
    {
        if (!g.Drawing)
        {
            int wheel = Steps(g, panel);
            if (wheel != 0)
            {
                if ((wheel < 0 && _sysZoom >= 6f * 0.999f) || (wheel > 0 && _sysZoom <= 0.4f * 1.001f)) WheelPast(wheel);
                else _sysZoom = Math.Clamp(_sysZoom * MathF.Pow(1.25f, -wheel), 0.4f, 6f);
            }
            _sysPan += MiddleDrag(g, panel);
            _sysPan = new Vector2(Math.Clamp(_sysPan.X, -panel.Size.X * 2f, panel.Size.X * 2f), Math.Clamp(_sysPan.Y, -panel.Size.Y * 2f, panel.Size.Y * 2f));
        }
        Charts charts = _game.Spaceflight.Charts;
        bool yours = Trail.AtYourStar;
        string star = Catalogue != null && Trail.Star >= 0 ? Catalogue.Name(Trail.Star) : "Star";
        PlanetSurface.Skies.StarSystem sysModel = yours ? _game.Surface.Planet.StarSystem : Profile.System(Trail.Star);
        List<SystemEntry> bodies = Charts.Bodies(sysModel, yours ? "Your world" : "Inner world", _game.Surface.Sky.Days, star);
        Vector2 c = panel.GetCenter() + _sysPan;
        float unit = panel.Size.Y * 0.49f * _sysZoom;
        var places = new Dictionary<string, Vector2> { ["star"] = c };
        var byKey = bodies.ToDictionary(e => e.Key);
        Vector2 Round(SystemEntry e, float reach, Vector2 about) => about + new Vector2(MathF.Cos(e.Bearing), MathF.Sin(e.Bearing)) * reach;
        foreach (SystemEntry e in bodies)
            if (e.Parent == null && e.Kind != "star") places[e.Key] = Round(e, e.Orbit * unit, c);
        // A moon of a moon's parent may come after it in the list: moons are placed once every world is.
        foreach (SystemEntry e in bodies.Where(e => e.Parent != null).OrderBy(e => byKey[e.Parent].Parent == null ? 0 : 1))
            places[e.Key] = Round(e, DotOf(byKey[e.Parent]) + 7f + e.Orbit * 6f, places[e.Parent]);
        if (!g.Drawing && g.Pressed && g.Over(panel))
        {
            SystemEntry hit = null;
            float nearest = 14f * 14f;
            foreach (SystemEntry e in bodies)
                if (e.Kind != "belt" && places[e.Key].DistanceSquaredTo(g.Mouse) < nearest) (nearest, hit) = (places[e.Key].DistanceSquaredTo(g.Mouse), e);
            if (hit?.Kind == "world" && yours) Trail.Down(); // your world opens high orbit
            else if (hit != null) _body = hit.Key;
        }
        Vector2 world = places["world"];
        if (g.Drawing)
        {
            var labels = new MapLabels();
            foreach (SystemEntry e in bodies)
            {
                if (e.Parent == null && e.Kind == "belt")
                {
                    // A belt: a broad scatter of rock, with what it is at its own place on it.
                    float across = MathF.Max(e.Width * unit, 3f);
                    ArcIn(g, panel, c, e.Orbit * unit, new Color(C.Accent2, 0.05f), across);
                    var rocks = new GalaxyRng((ulong)e.Key.GetHashCode(StringComparison.Ordinal) ^ 0x524F434BUL);
                    for (int i = 0; i < 320; i++)
                    {
                        float a = rocks.Range(0f, Mathf.Tau), r = e.Orbit * unit + (rocks.Float() + rocks.Float() - 1f) * across * 0.5f;
                        Vector2 rock = c + new Vector2(MathF.Cos(a), MathF.Sin(a)) * r;
                        if (panel.HasPoint(rock)) g.Canvas.DrawRect(new Rect2(rock, new Vector2(1.5f, 1.5f)), new Color(C.Accent2, 0.5f));
                    }
                    labels.Add(places[e.Key], 2f, e.Label.ToUpperInvariant(), C.Dim, 4);
                }
                else if (e.Parent == null && e.Orbit > 0f) ArcIn(g, panel, c, e.Orbit * unit, new Color(C.Rule, 0.55f));
                // A world that is a moon reads as one: its orbit round its giant, not a neighbour on the giant's.
                else if (e.Kind == "world" && e.Parent != null) ArcIn(g, panel, places[e.Parent], places[e.Key].DistanceTo(places[e.Parent]), new Color(C.Holo, 0.55f));
            }
            foreach (SystemEntry e in bodies)
            {
                Vector2 q = places[e.Key];
                float r = DotOf(e);
                if (e.Kind == "belt" || !panel.HasPoint(q)) continue;
                if (e.Kind == "star")
                {
                    g.Canvas.DrawCircle(q, r, StarColour(Trail.Star));
                    labels.Add(q, r, e.Label.ToUpperInvariant(), C.Text, 1);
                    continue;
                }
                g.Canvas.DrawCircle(q, r, e.Kind switch { "world" => C.Holo, "giant" => C.Accent2, "moon" => C.Text, _ => new Color(C.Text, 0.8f) });
                if (e.Key == _body) g.Canvas.DrawArc(q, r + 5f, 0, Mathf.Tau, 24, C.Holo, 1.5f);
                bool moonOfYours = e.Kind == "moon" && e.Parent == "world";
                if (e.Kind == "world") labels.Add(q, r + 7f, yours ? "YOUR WORLD: YOU ARE HERE" : e.Label.ToUpperInvariant(), yours ? C.Accent : C.Text, 0);
                else if (e.Kind != "moon" || moonOfYours || e.Key == _body) labels.Add(q, r, e.Label.ToUpperInvariant(), moonOfYours ? C.Dim : C.Text, moonOfYours ? 3 : 2);
                else labels.Add(q, r, null, C.Dim, 6);
            }
            if (yours && panel.HasPoint(world)) g.Canvas.DrawArc(world, DotOf(byKey["world"]) + 5f, 0, Mathf.Tau, 32, C.Accent, 2f);
            if (yours)
            foreach (SystemEntry w in charts.Wrecks)
            {
                Vector2 q = world + new Vector2(MathF.Cos(w.Bearing), MathF.Sin(w.Bearing)) * (20f + w.Orbit * 16f);
                places[w.Key] = q;
                if (!panel.HasPoint(q)) continue;
                g.Canvas.DrawCircle(q, 2.5f, C.Danger);
                labels.Add(q, 2.5f, null, C.Danger, 6);
            }
            labels.Flush(g, panel);
        }
        g.Caption("SYSTEM MAP", side.Position, side.Size.X);
        g.Text($"{bodies.Count(e => e.Kind is "planet" or "giant" or "world")} WORLDS · {bodies.Count(e => e.Kind == "moon")} MOONS · {bodies.Count(e => e.Kind == "belt")} BELTS", side.Position + new Vector2(0, 40), C.Dim, 14);
        if (yours)
        {
            g.Text("WARP DRIVE: NOT FITTED", side.Position + new Vector2(0, 62), C.Dim, 14);
            g.Text("WRECKS", side.Position + new Vector2(0, 84), C.Dim, 14);
        }
        float y = 112;
        if (yours)
        foreach (SystemEntry e in charts.Wrecks.Take(6))
        {
            bool picked = charts.Destination == e.Key;
            if (g.Button($"map:go:{e.Key}", new Rect2(side.Position + new Vector2(0, y), new Vector2(310, 28)), e.Label.ToUpperInvariant(), C.Accent2, lit: picked, size: 13)) charts.Destination = picked ? null : e.Key;
            y += 30;
        }
        if (yours && charts.Wrecks.Count == 0) g.Text("NONE FOUND", side.Position + new Vector2(0, y), C.Dim, 14);
        if (yours && g.Drawing && charts.Destination != null && places.TryGetValue(charts.Destination, out Vector2 to))
        {
            if (panel.HasPoint(world) && panel.HasPoint(to))
            {
                g.Canvas.DrawDashedLine(world, to, C.Accent, 2f, 8f);
                g.Canvas.DrawCircle(world, 5f, C.Accent);
            }
        }
        // What the model knows of the body you picked (or of the star).
        SystemEntry shown = _body != null && byKey.TryGetValue(_body, out SystemEntry b) ? b : bodies[0];
        g.Text(shown.Label.ToUpperInvariant(), side.Position + new Vector2(0, 530), C.Text, 15);
        int line = 0;
        foreach (string part in shown.Note.Split(", "))
            g.Text(part, side.Position + new Vector2(0, 554 + line++ * 22), C.Dim, 14);
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
        float depth = galaxy.DepthOf(id);
        lines.Add($"DEPTH {depth:0.00}  {Galaxy.Band(depth)}");
        if (s.Nebula >= 0) lines.Add("NEBULA · " + galaxy.Nebulae[s.Nebula].Name);
        lines.Add("TERRITORY · UNCLAIMED");
        lines.Add(id == Trail.You ? "YOU" : $"{light:F1} LY{(light <= Lanes.Range ? " · ONE JUMP" : "")}");
        if (id != Trail.You && Stars.View != null)
        {
            Vector3 look = _game.Spaceflight.LookToward(id);
            lines.Add($"SKY · {(MathF.Atan2(look.X, -look.Z) * 180f / MathF.PI + 360f) % 360f:000}° · {MathF.Asin(Math.Clamp(look.Y, -1f, 1f)) * 180f / MathF.PI:+00;-00}°");
        }
        for (int i = 0; i < lines.Count; i++) g.Text(lines[i], at + new Vector2(0, i * 21), i == 0 ? C.Text : C.Dim, 15);
    }

    Color StarColour(int id)
    {
        if (Catalogue == null || id < 0) return C.Accent;
        N.Vector3 tint = SkyView.Colour(Catalogue.Stars[id].Kind);
        return new Color(tint.X, tint.Y, tint.Z);
    }

    // ---- High orbit -----------------------------------------------------------------------------

    double _orbitX = double.NaN, _orbitZ;
    float _orbitZoom = 1f;
    Vector2 _orbitPan;

    /// <summary>
    /// The world as a globe, painted from its own map (a place on it is the same place on the world
    /// map) and lit by the star where it stands this moment, with the moons on their real orbits and
    /// the wrecks found at their real places. Distances out from the globe are eased to fit; every
    /// direction is true. It can be turned by dragging, and a click opens the world map there.
    /// </summary>
    void HighOrbit(Gui g, Rect2 panel, Rect2 side)
    {
        var planet = _game.Surface.Planet;
        Paint();
        var (ux, uz) = You();
        if (double.IsNaN(_orbitX)) (_orbitX, _orbitZ) = (ux, uz);
        if (!g.Drawing)
        {
            int wheel = Steps(g, panel);
            if (wheel != 0)
            {
                if ((wheel < 0 && _orbitZoom >= 1.6f * 0.999f) || (wheel > 0 && _orbitZoom <= 0.4f * 1.001f)) WheelPast(wheel);
                else _orbitZoom = Math.Clamp(_orbitZoom * MathF.Pow(1.2f, -wheel), 0.4f, 1.6f);
            }
            _orbitPan += MiddleDrag(g, panel);
        }
        float R = panel.Size.Y * 0.3f * _orbitZoom;
        // The globe stays inside its panel: panned as far as the edge, no further.
        _orbitPan = new Vector2(Math.Clamp(_orbitPan.X, -MathF.Max(panel.Size.X / 2f - R, 0f), MathF.Max(panel.Size.X / 2f - R, 0f)), Math.Clamp(_orbitPan.Y, -MathF.Max(panel.Size.Y / 2f - R, 0f), MathF.Max(panel.Size.Y / 2f - R, 0f)));
        Vector2 c = panel.GetCenter() + _orbitPan;
        var map = new GlobeMap(planet.Width, planet.Height, R);
        PlanetSurface.Skies.StarSystem model = planet.StarSystem;
        double days = _game.Surface.Sky.Days;
        PlanetSurface.Skies.StarSystem.Orientation turned = model.Orient(days);
        N.Vector3 G(PlanetSurface.Skies.D3 d) => new((float)d.X, (float)d.Y, (float)d.Z);
        // Toward the star from the world's centre, and each thing round the world, in the globe's frame.
        N.Vector3 toStar = G(turned.ToGlobe((model.Position(model.Star, days) - model.Position(model.Home, days)).Normalized));
        N.Vector3 Round(PlanetSurface.Skies.Orbiter b, double at) => G(turned.ToGlobe(model.Position(b, at) - model.Position(model.Home, at)));
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
                    View.PanelHeight = panel.Size.Y;
                    View.Settle();
                    _homed = true;
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
                var lit = new Color[4];
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
                        N.Vector3 d = Globe(q[k]);
                        var (mx, mz) = map.MapPoint(d);
                        float u = (float)(mx / planet.Width), v = (float)(mz / planet.Height);
                        if (k == 0) first = u;
                        else if (u - first > 0.5f) u -= 1f;
                        else if (first - u > 0.5f) u += 1f;
                        uv[k] = new Vector2(u + 1f, v);
                        float day = Math.Clamp(N.Vector3.Dot(d, toStar) * 4f + 0.5f, 0f, 1f);
                        lit[k] = new Color(0.14f + 0.86f * day, 0.15f + 0.85f * day, 0.22f + 0.78f * day);
                    }
                    var points = new[] { c + q[0] * R, c + q[1] * R, c + q[2] * R, c + q[3] * R };
                    g.Canvas.DrawPolygon(points, lit, uv, _chart);
                }
            }
            else g.TextIn("CHARTING…", panel, C.Dim, 18, HorizontalAlignment.Center);
            g.Canvas.DrawArc(c, R, 0, Mathf.Tau, 96, new Color(C.Holo, 0.7f), 2f);
            var labels = new MapLabels();
            // Out from the world's centre in true directions, the distances eased: the farthest moon stands at the panel's edge.
            float rh = (float)model.Home.Radius;
            float Reach(float metres) => R * (1.08f + 0.42f * MathF.Sqrt(Math.Clamp((metres / rh - 1f) / 39f, 0f, 1f)));
            bool Place(N.Vector3 g3, out Vector2 at, out bool behind)
            {
                float d = g3.Length();
                N.Vector3 n = g3 / d;
                float reach = Reach(d);
                at = c + new Vector2(N.Vector3.Dot(n, east), N.Vector3.Dot(n, south)) * reach;
                behind = N.Vector3.Dot(n, up) < 0f && at.DistanceTo(c) < R;
                return panel.HasPoint(at);
            }
            // The star's side.
            var sunward = new Vector2(N.Vector3.Dot(toStar, east), N.Vector3.Dot(toStar, south));
            if (sunward.LengthSquared() > 1e-4f)
            {
                Vector2 q = c + sunward.Normalized() * (R * 1.56f);
                g.Canvas.DrawCircle(q, 7f, StarColour(Trail.You));
                labels.Add(q, 7f, "TO THE STAR", C.Dim, 3);
            }
            if (model.Neighbour is { } giant && Place(Round(giant, days), out Vector2 big, out bool hidden) && !hidden)
            {
                g.Canvas.DrawCircle(big, 12f, C.Accent2);
                labels.Add(big, 12f, Charts.Tidy(giant.Name).ToUpperInvariant(), C.Text, 2);
            }
            foreach (PlanetSurface.Skies.Orbiter m in model.Under(model.Home))
            {
                // Its true orbit, drawn from its own positions over one month.
                const int Steps = 64;
                Vector2 last = default;
                bool had = false;
                for (int i = 0; i <= Steps; i++)
                {
                    bool ok = Place(Round(m, days + m.Period * i / Steps), out Vector2 p, out bool back) && !back;
                    if (ok && had) g.Canvas.DrawLine(last, p, new Color(C.Rule, 0.6f), 1f);
                    (last, had) = (p, ok);
                }
                if (!Place(Round(m, days), out Vector2 at, out bool behind) || behind) continue;
                bool fuel = m == model.FuelMoon;
                g.Canvas.DrawCircle(at, 5f, fuel ? C.Holo : C.Text);
                labels.Add(at, 5f, Charts.Tidy(m.Name).ToUpperInvariant() + (fuel ? ": WARP FUEL" : ""), fuel ? C.Holo : C.Text, 1);
            }
            foreach (DerelictPlan w in _game.Orbit?.Plans ?? Array.Empty<DerelictPlan>())
                if (_game.Spaceflight.Charts.Wrecks.Any(e => e.Key == w.Id) && Place(w.Position, out Vector2 at, out bool behind) && !behind)
                {
                    g.Canvas.DrawCircle(at, 2.5f, C.Danger);
                    labels.Add(at, 2.5f, null, C.Danger, 6);
                }
            // Your spot, if it is on this side of the globe.
            N.Vector3 you = map.Direction(ux, uz);
            if (N.Vector3.Dot(you, up) > 0f)
            {
                Vector2 at = c + new Vector2(N.Vector3.Dot(you, east), N.Vector3.Dot(you, south)) * R;
                g.Canvas.DrawCircle(at, 6f, C.Accent);
                g.Canvas.DrawArc(at, 11f, 0, Mathf.Tau, 24, C.Accent, 2f);
                labels.Add(at, 11f, "YOU", C.Accent, 0);
            }
            labels.Flush(g, panel);
        }
        g.Caption("HIGH ORBIT", side.Position, side.Size.X);
        string[] lines = { "[DRAG] TURN", "[LMB] WORLD MAP", $"{planet.Width / 1000:F0} x {planet.Height / 1000:F0} KM", $"DAY {Math.Floor(days) + 1:0} · {(days - Math.Floor(days)) * 24:00.0} H" };
        for (int i = 0; i < lines.Length; i++) g.Text(lines[i], side.Position + new Vector2(0, 40 + i * 26), C.Dim, 15);
        int row = 0;
        foreach (PlanetSurface.Skies.Orbiter m in model.Under(model.Home).OrderBy(m => m.Orbit).Take(3))
            g.Text($"{Charts.Tidy(m.Name)}: {Round(m, days).Length() / 1000f:F0} km", side.Position + new Vector2(0, 150 + row++ * 24), m == model.FuelMoon ? C.Holo : C.Text, 14);
        if (g.Button("map:orbit-home", new Rect2(side.Position + new Vector2(0, 250), new Vector2(150, 34)), "ON ME", C.Accent2)) (_orbitX, _orbitZ) = (ux, uz);
    }

    // ---- The galaxy -----------------------------------------------------------------------------

    bool _dragged;
    Vector2 _mapMiddle;
    float _mapFocal;

    /// <summary>Where a star is on the galaxy map just now, on the screen, or null.</summary>
    public Vector2? ScreenOf(int star)
    {
        if (Sky == null || !Sky.Project(Catalogue.Stars[star].Position, _mapFocal, out N.Vector2 at, out _)) return null;
        return _mapMiddle + new Vector2(at.X, at.Y);
    }

    /// <summary>Soft torn wisps: every nebula on the map is built of these, drawn at points of its own density.</summary>
    ImageTexture[] Wisps()
    {
        if (_wisps != null) return _wisps;
        const int Size = 128;
        _wisps = new ImageTexture[3];
        for (int t = 0; t < _wisps.Length; t++)
        {
            var bytes = new byte[Size * Size * 2];
            for (int y = 0; y < Size; y++)
            for (int x = 0; x < Size; x++)
            {
                float dx = (x + 0.5f) / Size * 2f - 1f, dy = (y + 0.5f) / Size * 2f - 1f;
                float k = MathF.Max(1f - (dx * dx + dy * dy), 0f);
                float n = LatticeNoise.Fractal(new N.Vector3(dx * 2.3f + t * 5.1f, dy * 2.3f, t * 3.7f), 4);
                float torn = Math.Clamp((n - 0.36f) / 0.3f, 0f, 1f);
                bytes[(x + y * Size) * 2] = 255;
                bytes[(x + y * Size) * 2 + 1] = (byte)(255f * k * k * torn);
            }
            _wisps[t] = ImageTexture.CreateFromImage(Image.CreateFromData(Size, Size, false, Image.Format.La8, bytes));
        }
        return _wisps;
    }

    ImageTexture[] _wisps;
    Task<(N.Vector3 at, float size, Color colour, int wisp)[][]> _clouds;

    /// <summary>
    /// The galaxy in 3D, turned by dragging, zoomed by the wheel. The disc's plane is drawn as rings,
    /// and each star charted hangs over it on a thin line, so its depth reads at a glance. Nebulae
    /// stand at their real place and size. The sphere round you is a jump's true 3D reach. A click
    /// picks a star, the sky marks it, and the wheel zooms in on it and on down into its system.
    /// </summary>
    /// <summary>Galaxy map names: off but for the picked star; these add the stars near you or the route's.</summary>
    public bool LabelNear, LabelRoute = true;

    void GalaxyLevel(Gui g, Rect2 panel, Rect2 side)
    {
        Galaxy galaxy = Catalogue;
        Charts charts = _game.Spaceflight.Charts;
        g.Caption("GALAXY MAP", side.Position, side.Size.X);
        if (galaxy == null || Trail.You < 0)
        {
            g.TextIn("CHARTING…", panel, C.Dim, 18, HorizontalAlignment.Center);
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
        Lanes lanes = Network(galaxy, you, charts);
        List<int> shown = lanes.Stars;
        if (!g.Drawing)
        {
            int wheel = Steps(g, panel);
            if (wheel != 0)
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
            Vector2 slide = MiddleDrag(g, panel);
            if (slide != Vector2.Zero) Sky.Slide(slide.X, slide.Y, focal);
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
                    for (int id = 0; id < galaxy.Stars.Length; id++)
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
            DrawCloud(g, panel, galaxy, At, labels);
            // The jump's reach: the sphere's outline, and its ring through you, level with the disc.
            if (At(you.Position, out Vector2 home, out float homeDepth))
            {
                float reach = Lanes.Range * focal / homeDepth;
                if (reach > 6f) g.Canvas.DrawArc(home, reach, 0, Mathf.Tau, 96, new Color(C.Accent, 0.6f), 1.5f);
                Ring(g, panel, you.Position, Lanes.Range, new Color(C.Accent, 0.28f), 1f, At);
            }
            // The lanes: a thin dim line between every two stars in jump range of each other, brighter for the picked or hovered star's, and the route to the picked star lit.
            int hovered = _hovered;
            if (Sky.Distance < 450)
            foreach (var (a, b) in lanes.Edges)
            {
                if (!At(galaxy.Stars[a].Position, out Vector2 qa, out _) || !At(galaxy.Stars[b].Position, out Vector2 qb, out _)) continue;
                if (!panel.HasPoint(qa) && !panel.HasPoint(qb)) continue;
                bool lit = a == hovered || b == hovered || a == charts.ChosenStar || b == charts.ChosenStar;
                bool live = lanes.Connected.Contains(a);
                g.Canvas.DrawLine(qa, qb, new Color(C.Accent, lit ? 0.5f : live ? 0.2f : 0.1f), 1f);
            }
            if (_route != null)
                for (int i = 1; i < _route.Count; i++)
                    if (At(galaxy.Stars[_route[i - 1]].Position, out Vector2 ra, out _) && At(galaxy.Stars[_route[i]].Position, out Vector2 rb, out _))
                        g.Canvas.DrawLine(ra, rb, new Color(C.Holo, 0.95f), 2.5f);
            foreach (int id in shown.OrderByDescending(id => { At(galaxy.Stars[id].Position, out _, out float d); return d; }))
            {
                Star s = galaxy.Stars[id];
                if (!At(s.Position, out Vector2 q, out float depth) || !panel.HasPoint(q)) continue;
                float r = Math.Clamp((3.2f + 0.8f * MathF.Log10(s.Light + 0.01f)) * MathF.Sqrt(96f / depth), 2.4f, 8f);
                g.Canvas.DrawCircle(q, r, StarColour(id));
                if (!lanes.Connected.Contains(id)) g.Canvas.DrawArc(q, r + 3f, 0, Mathf.Tau, 12, new Color(C.Dim, 0.6f), 1f);
                // Names stay off but for the picked star (and you); the toggles add the near ones or the route's.
                bool named = LabelNear && depth < 220f || LabelRoute && _route != null && _route.Contains(id);
                if (id != Trail.You && id != charts.ChosenStar) labels.Add(q, r, named ? galaxy.Name(id).ToUpperInvariant() : null, C.Text, 5);
            }
            if (At(you.Position, out Vector2 me, out _) && panel.HasPoint(me))
            {
                g.Canvas.DrawArc(me, 9f, 0, Mathf.Tau, 24, C.Accent, 2f);
                labels.Add(me, 9f, "YOU", C.Accent, 0);
            }
            if (_hovered >= 0 && _hovered != charts.ChosenStar && _hovered != Trail.You && At(galaxy.Stars[_hovered].Position, out Vector2 over, out _))
                labels.Add(over, 6f, galaxy.Name(_hovered).ToUpperInvariant(), C.Text, 1);
            if (charts.ChosenStar >= 0 && At(galaxy.Stars[charts.ChosenStar].Position, out Vector2 chosen, out _) && panel.HasPoint(chosen))
            {
                g.Canvas.DrawArc(chosen, 12f, 0, Mathf.Tau, 24, C.Holo, 2f);
                labels.Add(chosen, 12f, galaxy.Name(charts.ChosenStar).ToUpperInvariant(), C.Holo, 1);
            }
            if (LabelNear && At(N.Vector3.Zero, out Vector2 core, out _) && panel.HasPoint(core)) labels.Add(core, 4f, "CORE", C.Dim, 3);
            labels.Flush(g, panel);
        }
        int facts = charts.ChosenStar >= 0 ? charts.ChosenStar : Trail.You;
        StarFacts(g, side.Position + new Vector2(0, 40), facts);
        RouteFacts(g, side.Position + new Vector2(0, 524), galaxy, charts.ChosenStar);
        string[] held = Profile.Lines(facts);
        for (int i = 0; i < held.Length && i < 10; i++) g.Text(held[i], side.Position + new Vector2(0, 580 + i * 20), i == 0 ? C.Text : C.Dim, 13);
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
        if (g.Button("map:names-near", new Rect2(side.Position + new Vector2(0, 288), new Vector2(150, 30)), "NEAR NAMES", C.Holo, lit: LabelNear, size: 14)) LabelNear = !LabelNear;
        if (g.Button("map:names-route", new Rect2(side.Position + new Vector2(156, 288), new Vector2(150, 30)), "ROUTE NAMES", C.Holo, enabled: _route != null, lit: LabelRoute && _route != null, size: 14)) LabelRoute = !LabelRoute;
    }

    Lanes _lanes;
    int _lanesYou = -1, _lanesVisited = -1, _lanesChosen = -2;
    List<int> _route;

    /// <summary>The lane network round you, rebuilt only when you move or chart more; and the route to the picked star.</summary>
    Lanes Network(Galaxy galaxy, Star you, Charts charts)
    {
        if (_lanes == null || _lanesYou != Trail.You || _lanesVisited != charts.Visited.Count)
        {
            _lanes = new Lanes(galaxy, Trail.You, charts.Shown(galaxy));
            (_lanesYou, _lanesVisited, _lanesChosen) = (Trail.You, charts.Visited.Count, -2);
        }
        if (_lanesChosen != charts.ChosenStar)
        {
            _lanesChosen = charts.ChosenStar;
            _route = _lanesChosen >= 0 && _lanesChosen != Trail.You ? Lanes.Route(galaxy, Trail.You, _lanesChosen, Lanes.Range) : null;
        }
        return _lanes;
    }

    /// <summary>The picked star's route in words: jumps, light years, fold crystal.</summary>
    void RouteFacts(Gui g, Vector2 at, Galaxy galaxy, int id)
    {
        if (id == Trail.You || id < 0) return;
        if (_route == null) { g.Text("NO ROUTE", at, C.Warn, 15); return; }
        float ly = Lanes.Length(galaxy, _route);
        g.Text($"{_route.Count - 1} JUMP{(_route.Count == 2 ? "" : "S")} · {ly:F1} LY", at, C.Holo, 15, UiTheme.Mono);
        g.Text($"FOLD CRYSTAL {Lanes.Crystal(galaxy, _route)}", at + new Vector2(0, 24), C.Holo, 15, UiTheme.Mono);
    }

    Vector2[] _cloudAt;
    Color[] _cloudColour;
    Color[] _kindColour;

    /// <summary>Every star of the catalogue as a point (one batched draw), so the whole galaxy shows its arms and core at a cost that doesn't grow with zoom.</summary>
    int _hovered = -1;

    void DrawCloud(Gui g, Rect2 panel, Galaxy galaxy, Projects at, MapLabels labels)
    {
        int n = galaxy.Stars.Length;
        if (_cloudAt == null || _cloudAt.Length != n * 2)
        {
            _cloudAt = new Vector2[n * 2];
            _cloudColour = new Color[n];
            _kindColour = new Color[Enum.GetValues<StarKind>().Max(k => (int)k) + 1];
            foreach (StarKind k in Enum.GetValues<StarKind>())
            {
                N.Vector3 t = SkyView.Colour(k);
                _kindColour[(int)k] = new Color(t.X, t.Y, t.Z, 0.55f);
            }
        }
        int count = 0, near = -1, named = 0;
        float nearest = 14f * 14f;
        bool close = Sky.Distance < 70;
        for (int i = 0; i < n; i++)
        {
            if (!at(galaxy.Stars[i].Position, out Vector2 q, out float depth) || !panel.HasPoint(q)) continue;
            _cloudAt[count * 2] = q;
            _cloudAt[count * 2 + 1] = q + new Vector2(1.3f, 0f);
            _cloudColour[count++] = _kindColour[(int)galaxy.Stars[i].Kind];
            float d = q.DistanceSquaredTo(g.Mouse);
            if (d < nearest) (nearest, near) = (d, i);
            // Names as the zoom allows: only once close in, and only so many.
            if (LabelNear && close && depth < 60f && named < 40) { named++; labels.Add(q, 2f, galaxy.Name(i).ToUpperInvariant(), C.Dim, 6); }
        }
        _hovered = near;
        if (count == 0) return;
        g.Canvas.DrawMultilineColors(_cloudAt[..(count * 2)], _cloudColour[..count], 1.4f);
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

    /// <summary>
    /// Every nebula at its real place and size, as a cloud of wisps drawn at points of its own density,
    /// so it has the shape it has in the sky and turns with the map. Those with charted stars in them are named.
    /// </summary>
    void DrawNebulae(Gui g, Rect2 panel, Galaxy galaxy, List<int> shown, Projects at)
    {
        _clouds ??= Task.Run(() => galaxy.Nebulae.Select(n => n.Wisps(34).Select((w, i) =>
        {
            Color c = n.Kind == NebulaKind.Dark ? new Color(0.13f, 0.1f, 0.09f, 0.3f) : new Color(w.colour.X, w.colour.Y, w.colour.Z, 0.05f + 0.13f * MathF.Min(w.density * 1.6f, 1f));
            return (w.at, n.Radius * (0.2f + 0.26f * w.size), c, i % 3);
        }).ToArray()).ToArray());
        if (!_clouds.IsCompletedSuccessfully) return;
        ImageTexture[] wisps = Wisps();
        foreach ((N.Vector3 place, float size, Color colour, int wisp)[] cloud in _clouds.Result)
            foreach (var (place, size, colour, wisp) in cloud)
            {
                if (!at(place, out Vector2 q, out float depth)) continue;
                float r = size * _mapFocal / depth;
                var box = new Rect2(q - new Vector2(r, r), new Vector2(r * 2f, r * 2f));
                Rect2 seen = box.Intersection(panel);
                if (seen.Size.X <= 0f || seen.Size.Y <= 0f) continue;
                var source = new Rect2((seen.Position - box.Position) / box.Size * 128f, seen.Size / box.Size * 128f);
                g.Canvas.DrawTextureRectRegion(wisps[wisp], seen, source, colour);
            }
        var known = new HashSet<int>(shown.Select(id => (int)galaxy.Stars[id].Nebula));
        foreach (Nebula n in galaxy.Nebulae)
            if (LabelNear && known.Contains(n.Id) && at(n.Centre, out Vector2 middle, out _) && panel.HasPoint(middle)) g.Text(n.Name.ToUpperInvariant(), middle, new Color(n.Colour.X, n.Colour.Y, n.Colour.Z), 12);
    }
}
