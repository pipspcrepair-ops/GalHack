using System;
using System.Collections.Generic;
using System.Linq;
using GalHack.Physics;
using GalHack.Physics.Engine;
using GalHack.Structures;
using Godot;
using PhysicsSandbox.Bridges;
using PlanetSurface.Skies;
using Slice.Orbit;
using Slice.Vessels;
using N = System.Numerics;

namespace Slice.Tests;

/// <summary>
/// Phase 2's screenshots: the climb out, the globe from orbit, a wreck field, floating across to
/// a derelict, a derelict's dark inside, and landing back on the surface. Each is played through
/// the game's controls (the Skiff built from its blueprint, flown up and over). The one liberty:
/// the climbs and long flights run at a raised time-scale, printed, so a screenshot doesn't take
/// minutes; nothing is moved by hand.
/// </summary>
public sealed partial class Staging
{
    Pilot _pilot;
    string _said;

    IEnumerable<int> OrbitViews(string view) => view switch
    {
        "climbout" => ClimbOutView(),
        "globe" => GlobeView(),
        "wrecks" => WreckFieldView(),
        "eva" => EvaView(),
        "derelict" => DerelictView(),
        "landing" => LandingView(),
        "seam" => SeamView(),
        "ascent" => ClimbView(),
        "rings" => RingsView(),
        "bodies" => BodiesView(),
        "moon" => MoonView(),
        "orbitweather" => OrbitWeatherView(),
        "map-world" => MapView(Slice.Ui.MapLevel.World),
        "map-system" => MapView(Slice.Ui.MapLevel.System),
        "map-stars" => MapView(Slice.Ui.MapLevel.Galaxy),
        _ => GalaxyViews(view),
    };

    Vessel Skiff => BuiltSkiff();

    /// <summary>Builds the Skiff and takes its seat.</summary>
    IEnumerable<int> Seated()
    {
        _pilot = new Pilot(_game, _drive);
        _game.Said += (text, _) => _said = text;
        foreach (int x in Build(half: false)) yield return x;
        Vessel v = Skiff;
        foreach (int x in _pilot.Board(v)) yield return x;
        GD.Print($"(boarding: seated {_game.Survivor.Seated?.Name}, said: {_said})");
    }

    /// <summary>Faster than life, printed: for long climbs and flights in screenshots only.</summary>
    void Hurry(double scale)
    {
        Engine.TimeScale = scale;
        if (scale != 1) GD.Print($"(screenshot: game time runs {scale}x for this stretch)");
    }

    /// <summary>Straight up and out into orbit.</summary>
    IEnumerable<int> ToOrbit()
    {
        foreach (int x in Seated()) yield return x;
        Hurry(4);
        _game.Controls.Hold(Key.Space);
        for (int i = 0; i < 60 * 150 && !_game.InOrbit; i++) yield return 0;
        _game.Controls.ReleaseAll();
        Hurry(1);
        GD.Print($"(screenshot: {(_game.InOrbit ? "in orbit" : "still on the surface")}, the Skiff at {Skiff.Body.Position.ToGodot()})");
        if (!_game.InOrbit) yield break;
        for (int i = 0; i < 30; i++) yield return 0;
    }

    /// <summary>A level of the Tab menu's map, after a walk round the start (and, for the system's wrecks, a look from orbit).</summary>
    IEnumerable<int> MapView(Slice.Ui.MapLevel level)
    {
        if (level == Slice.Ui.MapLevel.System)
        {
            foreach (int x in ToOrbit()) yield return x;
            Hurry(3);
            Vessel wreck = NearestWreck(_ => true);
            Vector3 at = wreck.Body.Position.ToGodot();
            Vector3 up = (at - _game.Orbit.ToWorld(N.Vector3.Zero).ToGodot()).Normalized();
            foreach (int x in FlyRound(Skiff, at + up * 200f, 200f, 90f)) yield return x;
            Hurry(1);
            for (int i = 0; i < 70; i++) yield return 0;
            _game.Spaceflight.Charts.Destination = _game.Spaceflight.Charts.Wrecks.FirstOrDefault()?.Key;
        }
        else
        {
            _game.Spaceflight.Charts.Mark(_game.Start.X + 900, _game.Start.Z - 500);
            for (int i = 0; i < 70; i++) yield return 0;
        }
        foreach (int x in _drive.Tap(Key.Tab)) yield return x;
        _game.Menu.OpenMap(level);
        for (int i = 0; i < 60; i++) yield return 0;
    }

    /// <summary>Saves what was last drawn, beside the screenshot: --out=a.png, tag "x" gives a-x.png.</summary>
    void Snap(string tag)
    {
        string o = _args.GetValueOrDefault("out", "seam.png");
        string path = System.IO.Path.ChangeExtension(o, null) + "-" + tag + ".png";
        Error e = _game.GetViewport().GetTexture().GetImage().SavePng(path);
        GD.Print($"(screenshot: {tag}, band {_game.Spaceflight.Band:F2}, air gone {_game.Spaceflight.AirGone:F2}, {(_game.InOrbit ? "orbit" : "surface")}: {e})");
    }

    /// <summary>
    /// The seam: a climb through the crossing, a frame just before it and the first frame after it
    /// (a pair to lay side by side), and frames up the overlap band.
    /// </summary>
    IEnumerable<int> SeamView()
    {
        foreach (int x in Seated()) yield return x;
        Vessel v = Skiff;
        float ceiling = _game.Surface.Physics.Zone.Ceiling;
        _game.Visor.Visible = _args.GetValueOrDefault("hud") == "1";
        _game.Survivor.Pitch = (float)Num("pitch", -0.3);
        bool down = _args.GetValueOrDefault("look") == "down"; // from under the Skiff, straight down past it
        void Follow()
        {
            if (!down) return;
            Vector3 at = v.Body.Position.ToGodot();
            Vector3 eye = at + new Vector3(0f, -3f, 12f);
            _game.FixedCamera = new Transform3D(Basis.LookingAt(new Vector3(at.X, at.Y - 1000f, at.Z - 700f) - eye, Vector3.Forward), eye);
        }
        if (_args.TryGetValue("weather", out string sky))
        {
            _game.Zone.Weather.Sim.Snap(Enum.Parse<Weather.Model.WeatherKind>(sky));
            for (int i = 0; i < 30; i++) yield return 0;
        }
        float seatPitch = _game.Survivor.Pitch;
        _game.Survivor.Pitch = (float)Num("skypitch", 0.45);
        for (int i = 0; i < 6; i++) yield return 0;
        Snap("ground");
        _game.Survivor.Pitch = seatPitch;
        Hurry(3);
        _game.Controls.Hold(Key.Space);
        foreach (float height in _args.TryGetValue("heights", out string hs) ? hs.Split(',').Select(float.Parse).ToArray() : new[] { 400f, 650f, 800f, 900f, 1000f })
        {
            for (int i = 0; i < 60 * 60 && v.Body.Position.Y < height; i++)
            {
                Follow();
                yield return 0;
            }
            Hurry(1);
            yield return 0;
            yield return 0;
            Snap($"alt{height:0}");
            Hurry(3);
        }
        Hurry(1);
        while (v.Body.Position.Y < ceiling - 3f)
        {
            Follow();
            yield return 0;
        }
        yield return 0;
        Snap("before");
        for (int i = 0; i < 300 && !_game.InOrbit; i++)
        {
            Follow();
            yield return 0;
        }
        yield return 0;
        Snap("after");
        foreach (float height in new[] { 1200f, 1400f, 1600f, 1800f, 2000f, 2200f })
        {
            for (int i = 0; i < 60 * 40 && v.Body.Position.Y < height; i++)
            {
                Follow();
                yield return 0;
            }
            yield return 0;
            Snap($"alt{height:0}");
        }
        for (int i = 0; i < 60; i++)
        {
            Follow();
            yield return 0;
        }
    }

    /// <summary>
    /// The whole climb and back, frames at fixed heights from a real flight: --cam=cabin (the seat's
    /// own view) or --cam=out (over the hull, --pitch degrees below level, one frame for each of
    /// --yaws, degrees from north, and with --straight=1 one more straight down, from under the
    /// hull). --body=1 stops the clock at a moment with the biggest body well up, and every frame
    /// looks one way, at it; --side=1 or -1 picks a moment with the eye on that side of its ring plane,
    /// 0 one nearly in the plane. --down=1 flies back down through them. Prints what each frame cost to draw.
    /// </summary>
    IEnumerable<int> ClimbView()
    {
        foreach (int x in Seated()) yield return x;
        Vessel v = Skiff;
        _game.Visor.Visible = false;
        if (_args.TryGetValue("time", out string clock)) _game.Zone.Sky.TimeOfDay = double.Parse(clock, System.Globalization.CultureInfo.InvariantCulture);
        bool cabin = _args.GetValueOrDefault("cam", "cabin") == "cabin";
        float pitch = (float)Num("pitch", 8) * Mathf.Pi / 180f;
        float[] yaws = _args.GetValueOrDefault("yaws", "0").Split(',').Select(y => float.Parse(y, System.Globalization.CultureInfo.InvariantCulture)).ToArray();
        _game.Survivor.Pitch = (float)Num("seatpitch", -0.3);
        Vector3? atBody = null;
        if (_args.GetValueOrDefault("body") == "1")
        {
            var sky = _game.Zone.Sky;
            sky.Paused = true;
            float best = -2f;
            double when = sky.TimeOfDay;
            int target = 0;
            double from = sky.Days;
            bool sided = _args.TryGetValue("side", out string sideText);
            int side = sided ? int.Parse(sideText) : 0;
            for (double t = 0.0; t < (sided ? 40.0 : 1.0); t += 0.01)
            {
                sky.Days = from + t;
                sky.Track();
                if (sky.Drawn.Count == 0) continue;
                // Which side of the ring plane the eye is on: the sign of the way to the giant along its normal.
                // --target=moon: the biggest moon there is, not the biggest thing.
                var moons = sky.Drawn.Where(d => d.Body.Kind == OrbiterKind.Moon).OrderByDescending(d => d.Angular).ToList(); target = _args.GetValueOrDefault("target") == "moon" && moons.Count > 0 ? sky.Drawn.ToList().IndexOf(moons[0]) : 0;
                float across = -sky.Drawn[target].Direction.Dot(sky.NeighbourRing);
                bool fits = !sided || (side == 0 ? MathF.Abs(across) < 0.06f : across * side > 0.25f);
                float up = fits && sky.Drawn[target].Direction.Y > 0.15f ? sky.Drawn[target].Direction.Y - MathF.Abs(sky.ToSun.Y - 0.35f) : -2f;
                if (up > best) (best, when) = (up, from + t);
            }
            sky.Days = when;
            sky.Track();
            atBody = sky.Drawn[target].Direction;
            float over = -atBody.Value.Dot(sky.NeighbourRing);
            // The ring's nearest point, as it falls against the giant's centre on the screen.
            Vector3 toward = -(atBody.Value - sky.NeighbourRing * atBody.Value.Dot(sky.NeighbourRing)).Normalized();
            float nearArc = (toward - atBody.Value * toward.Dot(atBody.Value)).Dot((Vector3.Up - atBody.Value * atBody.Value.Y).Normalized());
            GD.Print($"(sky: looking at {sky.Drawn[target].Body.Name}, radius {Mathf.RadToDeg(sky.Drawn[target].Angular):F2} degrees, day {when:F2}; direction {atBody}; eye {over:F2} along the ring's normal, the near arc falls {(nearArc > 0f ? "above" : "below")} the centre)");
        }
        if (atBody != null) foreach (int x in UntilSurfaces()) yield return x;
        Rid viewport = _game.GetViewport().GetViewportRid();
        RenderingServer.ViewportSetMeasureRenderTime(viewport, true);
        void Look(float yaw)
        {
            if (cabin) return;
            Vector3 at = v.Body.Position.ToGodot();
            if (float.IsNaN(yaw))
            {
                _game.FixedCamera = new Transform3D(Basis.LookingAt(Vector3.Down, Vector3.Forward), at + new Vector3(0f, -3f, 12f));
                return;
            }
            Vector3 eye = at + Vector3.Up * 5f;
            Vector3 ahead = atBody ?? new Vector3(MathF.Sin(yaw), 0f, -MathF.Cos(yaw)) * MathF.Cos(pitch) + Vector3.Down * MathF.Sin(pitch);
            _game.FixedCamera = new Transform3D(Basis.LookingAt(ahead, Vector3.Up), eye);
        }
        IEnumerable<int> Frames(string tag)
        {
            foreach (float yaw in _args.GetValueOrDefault("straight") == "1" ? yaws.Append(float.NaN) : yaws)
            {
                Look(yaw * Mathf.Pi / 180f);
                for (int i = 0; i < 7; i++)
                {
                    Look(yaw * Mathf.Pi / 180f);
                    yield return 0;
                }
                string name = float.IsNaN(yaw) ? $"{tag}-down" : yaws.Length > 1 || !cabin ? $"{tag}-y{yaw:0}" : tag;
                Snap(name);
                if (_game.InOrbit && _game.Surface.Planet.StarSystem.Home.Rings) GD.Print($"(ring fit {name}: {_game.Orbit.Globe.RingFit.Off:F1} m off the sheet's centre, inner edge {_game.Orbit.Globe.RingFit.Inner:F2} radii of the sheet)");
                GD.Print($"(frame {name}: y {v.Body.Position.Y:F1}, {RenderingServer.ViewportGetMeasuredRenderTimeCpu(viewport):F2} ms cpu, {RenderingServer.ViewportGetMeasuredRenderTimeGpu(viewport):F2} ms gpu, shroud {_game.Spaceflight.Shrouded:F2}, screen {(_game.InOrbit ? "orbit" : "surface")})");
            }
        }
        float[] heights = _args.GetValueOrDefault("heights", "500,700,850,950,1000,1040,1058,1062,1100,1160,1250,1405,1600,1800,2100,2600,3300,4300,5600,7500")
            .Split(',').Select(h => float.Parse(h, System.Globalization.CultureInfo.InvariantCulture)).ToArray();
        float Up() => _game.InOrbit ? _game.Orbit.SurfaceHeight(_game.Orbit.ToGlobe(v.Body.Position).Length()) : v.Body.Position.Y;
        _game.Controls.Hold(Key.Space);
        foreach (float height in heights)
        {
            Hurry(height > 1900 || height < 900 ? 3 : 1);
            for (int i = 0; i < 60 * 90 && Up() < height; i++)
            {
                Look(yaws[0] * Mathf.Pi / 180f);
                yield return 0;
            }
            Hurry(1);
            foreach (int x in Frames($"up{height:0}")) yield return x;
        }
        _game.Controls.ReleaseAll();
        Hurry(1);
        if (_args.GetValueOrDefault("down") != "1")
        {
            Wildlife.Shutdown.Quit(_game, 0);
            yield break;
        }
        _game.Controls.Hold(Key.Ctrl);
        foreach (float height in heights.Reverse())
        {
            if (height > 3300) continue;
            Hurry(height > 1900 || height < 950 ? 3 : 1);
            for (int i = 0; i < 60 * 90 && Up() > height; i++)
            {
                Look(yaws[0] * Mathf.Pi / 180f);
                yield return 0;
            }
            Hurry(1);
            foreach (int x in Frames($"dn{height:0}")) yield return x;
            GD.Print($"(descent at {height:0}: {(_game.InOrbit ? "orbit" : "surface")})");
        }
        _game.Controls.ReleaseAll();
        Wildlife.Shutdown.Quit(_game, 0);
    }

    /// <summary>Waits for every other body's surface to be painted, and prints what they cost.</summary>
    IEnumerable<int> UntilSurfaces()
    {
        var surfaces = _game.Surface.Sky.Surfaces;
        for (int i = 0; i < 60 * 90 && !surfaces.Finished; i++) yield return 0;
        GD.Print($"(surfaces: {surfaces.Done} of {surfaces.Wanted} painted, {surfaces.TotalMs / Math.Max(surfaces.Done, 1):F0} ms each on their own thread, {surfaces.WorstMs:F0} ms the worst, {BodySurfaces.MemoryBytes / 1048576.0:F1} MB held whatever the system)");
    }

    /// <summary>
    /// A moon, a neighbouring planet and a giant from several distances, from the true place in the
    /// system. The one liberty: the eye is put there, not flown, and the world you left is not drawn.
    /// </summary>
    IEnumerable<int> BodiesView()
    {
        foreach (int x in ToOrbit()) yield return x;
        foreach (int x in UntilSurfaces()) yield return x;
        // Out of the air, and clear of the hull.
        Vector3 world = _game.Orbit.ToWorld(N.Vector3.Zero).ToGodot(), out0 = (Skiff.Body.Position.ToGodot() - world).Normalized();
        Hurry(3);
        foreach (int x in _pilot.FlyTo(Skiff, () => world + out0 * (_game.Orbit.ShellRadius + 2500f), 60f, 120f)) yield return x;
        Hurry(1);
        _game.Controls.ReleaseAll();
        _game.Visor.Visible = false;
        var sky = _game.Surface.Sky;
        sky.Paused = true;
        OrbitZone o = _game.Orbit;
        o.Globe.Hidden = true;
        StarSystem model = _game.Surface.Planet.StarSystem;
        double days = sky.Days;
        StarSystem.Orientation turned = model.Orient(days);
        Basis turn = o.Turn.ToBasis();
        foreach (Orbiter b in model.Bodies)
            GD.Print($"(body: {b.Name}, {b.Kind}, {b.Radius / 1000:F1} km, zone {b.Zone}, {b.Spec?.Archetype.ToString() ?? "-"}{(b.Spec != null ? " " + b.Spec.Variant + " on " + b.Spec.WidthKm + " km" : "")}, layer {sky.Surfaces.LayerOf(b)})");
        Orbiter moon = model.FuelMoon, giant = model.Bodies.FirstOrDefault(b => b.Kind == OrbiterKind.GasGiant);
        Orbiter planet = model.Bodies.Where(b => b.Kind == OrbiterKind.Planet && b.Parent == model.Star && b != model.Home).OrderBy(b => (model.Position(b, days) - model.Position(model.Home, days)).Length).First();
        foreach (var (tag, body, radii) in new (string, Orbiter, double[])[]
        {
            ("moon", moon, new[] { 12.0, 5.0, 2.2 }), ("planet", planet, new[] { 40.0, 8.0, 2.2 }), ("giant", giant, new[] { 12.0, 4.0 }),
        })
        {
            if (body == null) continue;
            D3 toStar = (model.Position(model.Star, days) - model.Position(body, days)).Normalized;
            D3 side = toStar.Cross(new D3(0, 1, 0)).Normalized;
            D3 at = model.Position(body, days) - model.Position(model.Home, days);
            foreach (double r in radii)
            {
                D3 from = at + (toStar * 0.8 + side * 0.6 + new D3(0, 0.2, 0)).Normalized * (body.Radius * r);
                _game.Spaceflight.ViewFrom = turned.ToGlobe(from);
                Sighting s = model.See(body, days, turned.ToGlobe(from), turned);
                Vector3 look = turn * new Vector3((float)s.Direction.X, (float)s.Direction.Y, (float)s.Direction.Z);
                _game.FixedCamera = new Transform3D(Basis.LookingAt(look, Mathf.Abs(look.Y) > 0.95f ? Vector3.Forward : Vector3.Up), Skiff.Body.Position.ToGodot() + look * 30f);
                for (int i = 0; i < 8; i++) yield return 0;
                Snap($"{tag}-{r:0.0}r");
                GD.Print($"(frame {tag}: {body.Name} from {s.Distance / 1000:F0} km, {Mathf.RadToDeg(s.Angular):F1} degrees radius)");
            }
        }
    }

    /// <summary>
    /// A ringed world from a flight round it, frames looking at the globe's centre: inside the ring
    /// (--ringed=1 picks a world that has one), then well out, north of the ring's plane, south of
    /// it, and in it, and from far off.
    /// </summary>
    IEnumerable<int> RingsView()
    {
        foreach (int x in ToOrbit()) yield return x;
        Vessel v = Skiff;
        OrbitZone o = _game.Orbit;
        _game.Visor.Visible = false;
        Rid viewport = _game.GetViewport().GetViewportRid();
        RenderingServer.ViewportSetMeasureRenderTime(viewport, true);
        Vector3 centre = o.ToWorld(N.Vector3.Zero).ToGodot(), north = (o.ToWorld(N.Vector3.UnitY).ToGodot() - centre).Normalized();
        Vector3 out0 = v.Body.Position.ToGodot() - centre;
        Vector3 east = (out0 - north * out0.Dot(north)).Normalized();
        foreach (var (tag, height, latitude) in new (string, float, float)[]
        {
            ("inside", 3000f, 0f), ("north", 9000f, 32f), ("south", 9000f, -32f), ("cross", 9000f, 9f), ("under", 9000f, -9f), ("edge", 9000f, 0f), ("edge-near", 7600f, 1.5f), ("far", 30000f, 18f),
        })
        {
            float lat = Mathf.DegToRad(latitude);
            Vector3 over = centre + (east * MathF.Cos(lat) + north * MathF.Sin(lat)) * (o.GlobeRadius + height);
            Hurry(3);
            foreach (int x in FlyRound(v, over, 150f, 120f)) yield return x;
            Hurry(1);
            Vector3 at = v.Body.Position.ToGodot(), eye = at + (centre - at).Normalized() * 12f;
            _game.FixedCamera = new Transform3D(Basis.LookingAt(centre - eye, north), eye);
            for (int i = 0; i < 12; i++) yield return 0;
            Snap(tag);
            GD.Print($"(frame {tag}: {at.DistanceTo(centre) - o.GlobeRadius:F0} m up, {RenderingServer.ViewportGetMeasuredRenderTimeCpu(viewport):F2} ms cpu, {RenderingServer.ViewportGetMeasuredRenderTimeGpu(viewport):F2} ms gpu)");
            _game.FixedCamera = null;
        }
    }

    /// <summary>The Skiff climbing, the land far below.</summary>
    IEnumerable<int> ClimbOutView()
    {
        foreach (int x in Seated()) yield return x;
        Hurry(4);
        _game.Controls.Hold(Key.Space);
        float stop = (float)Num("alt", 480);
        Vessel v = Skiff;
        for (int i = 0; i < 60 * 60 && v.Body.Position.Y < stop; i++) yield return 0;
        Hurry(1);
        _game.Visor.Visible = _args.GetValueOrDefault("hud") == "1";
        for (int i = 0; i < 10; i++) yield return 0;
        Vector3 at = v.Body.Position.ToGodot();
        float heading = (float)Num("heading", 200) * Mathf.Pi / 180f;
        Vector3 eye = at + new Vector3(MathF.Sin(heading) * 14f, -5f, MathF.Cos(heading) * 14f);
        _game.FixedCamera = new Transform3D(Basis.LookingAt(at + Vector3.Down * 3f - eye, Vector3.Up), eye);
        for (int i = 0; i < 6; i++) yield return 0;
    }

    /// <summary>The globe from orbit, the Skiff in front of it.</summary>
    IEnumerable<int> GlobeView()
    {
        foreach (int x in ToOrbit()) yield return x;
        Vessel v = Skiff;
        OrbitZone orbit = _game.Orbit;
        _game.Visor.Visible = _args.GetValueOrDefault("hud") == "1";
        // Out a way, to see the globe whole: fly straight out from it.
        Vector3 centre = orbit.ToWorld(N.Vector3.Zero).ToGodot();
        Vector3 up = (v.Body.Position.ToGodot() - centre).Normalized();
        Hurry(3);
        foreach (int x in _pilot.FlyTo(v, () => centre + up * (orbit.ShellRadius + (float)Num("away", 3000)), 40f, 90f)) yield return x;
        Hurry(1);
        Vector3 at = v.Body.Position.ToGodot();
        Vector3 side = up.Cross(Vector3.Up).Normalized();
        if (side.LengthSquared() < 0.01f) side = Vector3.Right;
        Vector3 ahead = side.Cross(up);
        // From over the Skiff, looking down past it at the whole world.
        Vector3 eye = at + up * 15f + ahead * 7f + side * 5f;
        Vector3 look = centre + ahead * 900f;
        _game.FixedCamera = new Transform3D(Basis.LookingAt(look - eye, up), eye);
        for (int i = 0; i < 12; i++) yield return 0;
    }

    /// <summary>The nearest wreck of a kind (by its site's kind in the POI data).</summary>
    Vessel NearestWreck(Func<Vessel, bool> which)
    {
        Vector3 from = _game.Survivor.WorldEye();
        return _game.Orbit.Wrecks.Where(which).OrderBy(w => w.Body.Position.ToGodot().DistanceTo(from)).FirstOrDefault();
    }

    /// <summary>A wreck field: fragments and cargo boxes adrift, the globe beyond.</summary>
    IEnumerable<int> WreckFieldView()
    {
        foreach (int x in ToOrbit()) yield return x;
        Vessel v = Skiff;
        Vessel piece = NearestWreck(w => w.Derelict.Site.Kind == "field" && !w.Derelict.Site.Id.Contains("pod"));
        if (piece == null) yield break;
        Vector3 centre = piece.Body.Position.ToGodot();
        // Its own pieces (a site's kind can stand in more than one place): "site3-debris-1" and so on.
        string field = piece.Derelict.Id[..piece.Derelict.Id.LastIndexOf('-')];
        foreach (Vessel w in _game.Orbit.Wrecks.Where(w => w.Derelict.Id.StartsWith(field + "-"))) centre += w.Body.Position.ToGodot();
        centre /= 1 + _game.Orbit.Wrecks.Count(w => w.Derelict.Id.StartsWith(field + "-"));
        Vector3 world = _game.Orbit.ToWorld(N.Vector3.Zero).ToGodot();
        Vector3 up = (centre - world).Normalized();
        Hurry(3);
        foreach (int x in FlyRound(v, centre + up * 130f, 40f, 170f)) yield return x;
        Hurry(1);
        _game.Visor.Visible = _args.GetValueOrDefault("hud") == "1";
        // From the Skiff's bow, looking down over the field to the globe.
        Vector3 at = v.Body.Position.ToGodot();
        Vector3 eye = at + (centre - at).Normalized() * 14f;
        _game.FixedCamera = new Transform3D(Basis.LookingAt(centre - up * 30f - eye, up), eye);
        for (int i = 0; i < 12; i++) yield return 0;
    }

    /// <summary>
    /// Flies round the globe to a point high over it: out and around as a pilot does, not along the
    /// straight line, which dips back into the air (and a dip under the crossing is a descent).
    /// </summary>
    IEnumerable<int> FlyRound(Vessel v, Vector3 over, float within, float seconds)
    {
        Vector3 world = _game.Orbit.ToWorld(N.Vector3.Zero).ToGodot();
        Vector3 there = (over - world).Normalized();
        float radius = (over - world).Length();
        Vector3 Along()
        {
            Vector3 here = (v.Body.Position.ToGodot() - world).Normalized();
            float left = here.AngleTo(there);
            return world + (left < 0.15f ? there : here.Slerp(there, 0.15f / left)) * radius;
        }
        foreach (int x in _pilot.FlyTo(v, Along, within, seconds)) yield return x;
    }

    /// <summary>The derelict ship nearest, and its hull door in the world.</summary>
    (Vessel ship, Vector3 door, Vector3 outward) Derelict()
    {
        Vessel ship = NearestWreck(w => w.Derelict.Site.Kind == "wreck" && w.Derelict.Door != null);
        if (ship == null) return default;
        Int3 cell = ship.Derelict.Door.Value;
        Vector3 door = ship.ToWorld(StructureBridge.CellCenter(ship.Structure, cell));
        Vector3 outward = MathX.Rotate(ship.Body.Rotation, N.Vector3.UnitX * Math.Sign(cell.X)).ToGodot();
        return (ship, door, outward);
    }

    /// <summary>Flies the Skiff to a stop off a derelict's hull door, and steps out onto its deck.</summary>
    IEnumerable<int> ToDerelict()
    {
        foreach (int x in ToOrbit()) yield return x;
        Vessel v = Skiff;
        var (ship, door, outward) = Derelict();
        if (ship == null) yield break;
        Hurry(3);
        foreach (int x in _pilot.FlyTo(v, () => door + outward * 9f, 2.5f, 150f)) yield return x;
        Hurry(1);
        foreach (int x in _drive.Tap(Key.F)) yield return x; // up from the seat
        foreach (int x in _drive.Tap(Key.Key1)) yield return x; // the tool in hand
        for (int i = 0; i < 20; i++) yield return 0;
    }

    /// <summary>Off the deck: a jump clear of the Skiff's gravity, then afloat.</summary>
    IEnumerable<int> PushOff()
    {
        _game.Controls.Hold(Key.Space);
        for (int i = 0; i < 8; i++) yield return 0;
        _game.Controls.ReleaseAll();
        for (int i = 0; i < 40; i++) yield return 0;
    }

    /// <summary>Floating across to a derelict's hull door, the Skiff behind.</summary>
    IEnumerable<int> EvaView()
    {
        foreach (int x in ToDerelict()) yield return x;
        var (ship, door, outward) = Derelict();
        if (ship == null) yield break;
        foreach (int x in PushOff()) yield return x;
        foreach (int x in _pilot.FloatTo(() => door + outward * 3.5f, 1.0f, 30f)) yield return x;
        _game.Visor.Visible = _args.GetValueOrDefault("hud") != "0";
        _game.ThirdPerson = true;
        foreach (int x in _drive.LookAt(door, 8)) yield return x;
    }

    /// <summary>Inside a derelict, dark but for the head torch.</summary>
    IEnumerable<int> DerelictView()
    {
        foreach (int x in ToDerelict()) yield return x;
        var (ship, door, outward) = Derelict();
        if (ship == null) yield break;
        foreach (int x in PushOff()) yield return x;
        foreach (int x in _pilot.FloatTo(() => door + outward * 2.2f, 0.8f, 30f)) yield return x;
        foreach (int x in _drive.LookAt(door, 4)) yield return x;
        foreach (int x in _drive.Tap(Key.E)) yield return x;
        Int3 c = ship.Derelict.Door.Value;
        int s = Math.Sign(c.X);
        Vector3 Cell(int x, int y, int z) => ship.ToWorld(StructureBridge.CellCenter(ship.Structure, new Int3(x, y, z)));
        foreach (int x in _pilot.FloatTo(() => Cell(c.X - 2 * s, 1, c.Z), 0.6f, 20f)) yield return x;
        foreach (int x in _drive.Tap(Key.L)) yield return x; // the head torch
        _game.Visor.Visible = _args.GetValueOrDefault("hud") != "0";
        var (min, max) = ship.Structure.Bounds();
        foreach (int x in _drive.LookAt(Cell(0, 1, max.Z - 2), 20)) yield return x;
    }

    /// <summary>Back down from orbit where it left, landing on the surface it left, seen from the ground.</summary>
    IEnumerable<int> LandingView()
    {
        foreach (int x in ToOrbit()) yield return x;
        Vessel v = Skiff;
        // Straight back down its own legs through the seam, and on down on the gear.
        Hurry(3);
        _game.Controls.Hold(Key.Ctrl);
        for (int i = 0; i < 60 * 60 && _game.InOrbit; i++) yield return 0;
        for (int i = 0; i < 60 * 60 && !v.Controller.Frozen; i++)
        {
            float agl = v.AboveGround, vy = v.Body.Velocity.Y;
            float wanted = agl > 260f ? -60f : agl > 70f ? -20f : agl > 25f ? -8f : agl > 6f ? -3f : -1.2f;
            bool onGear = agl < 3f && MathF.Abs(vy) < 0.4f;
            _game.Controls.Hold(Key.Ctrl, !onGear && vy > wanted + 0.5f);
            _game.Controls.Hold(Key.Space, !onGear && vy < wanted - 0.5f);
            if (agl < (float)Num("agl", 9)) break;
            yield return 0;
        }
        Hurry(1);
        _game.Visor.Visible = _args.GetValueOrDefault("hud") == "1";
        Vector3 at = v.Body.Position.ToGodot();
        float heading = (float)Num("heading", 215) * Mathf.Pi / 180f;
        Vector3 eye = at + new Vector3(MathF.Sin(heading) * 15f, 0f, MathF.Cos(heading) * 15f);
        eye.Y = _game.Zone.GroundAt(eye.X, eye.Z).Y + 2f;
        _game.FixedCamera = new Transform3D(Basis.LookingAt(at + Vector3.Up * 1.5f - eye, Vector3.Up), eye);
        for (int i = 0; i < 4; i++) yield return 0;
    }
}
