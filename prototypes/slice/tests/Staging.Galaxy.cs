using System;
using System.Collections.Generic;
using System.Linq;
using GalHack.Physics.Engine;
using GalHack.Stars;
using Godot;
using Slice.Game;
using Slice.Orbit;
using Slice.Ui;
using Slice.Vessels;
using Weather.Model;
using N = System.Numerics;

namespace Slice.Tests;

/// <summary>
/// The galaxy's proof, from the real game on a world picked with --seed: the night sky from the
/// ground and the same direction from space with a chosen star bracketed in both, the map beside
/// it, the sky after a jump and from inside a nebula, the map's trail down and back up, and the
/// sky's cost on the graphics card.
/// </summary>
public sealed partial class Staging
{
    IEnumerable<int> GalaxyViews(string view) => view switch
    {
        "galaxy-climb" => GalaxyClimb(),
        "night-ground" => NightGround(),
        "night-space" => NightSpace(),
        "night-core" => NightCore(space: false),
        "night-core-space" => NightCore(space: true),
        "sun-climb" => SunClimb(),
        "ringed-giant" => RingedGiant(),
        "sun-look" => SunLook(),
        "eclipse" => EclipseView(),
        "galaxy-map" => GalaxyMap(),
        "world-map" => WorldMapShots(),
        "galaxy-lanes" => GalaxyLanes(),
        "galaxy-core" => GalaxyCore(),
        "galaxy-jump" => GalaxyJump(),
        "galaxy-nebula" => GalaxyNebula(),
        "galaxy-trail" => GalaxyTrail(),
        "galaxy-seam" => GalaxySeam(),
        "galaxy-perf" => GalaxyPerf(),
        _ => throw new ArgumentException($"no view '{view}'"),
    };

    GalaxySky Stars => _game.Surface.Sky.Stars;

    /// <summary>Waits for the sky, then holds the night still and clear.</summary>
    IEnumerable<int> Midnight()
    {
        Stars.Finish();
        _game.Zone.Sky.TimeOfDay = Num("time", 0.0);
        _game.Zone.Sky.Paused = true;
        _game.Zone.Weather.Sim.Snap(WeatherKind.Clear);
        foreach (int x in _drive.Wait(30)) yield return x;
    }

    /// <summary>The nearest neighbour standing well up in this sky, or failing that the nearest. It is the star all the views look at.</summary>
    int ChosenStar()
    {
        List<int> near = Stars.Galaxy.Neighbours(Stars.Here);
        foreach (int id in near)
            if (_game.Spaceflight.LookToward(id).Y > 0.35f) return id;
        return near[0];
    }

    void Choose(int id)
    {
        _game.Spaceflight.Charts.ChosenStar = id;
        Vector3 d = _game.Spaceflight.LookToward(id);
        GD.Print($"(galaxy: chosen star {id} {Stars.Galaxy.Name(id)}, {Stars.Galaxy.Stars[id].Kind}, {Vector3.Zero.DistanceTo(ToGodot(Stars.Galaxy.Stars[id].Position - Stars.Galaxy.Stars[Stars.Here].Position)):F1} LY, world direction {d}, home {Stars.Home} {Stars.Galaxy.Name(Stars.Home)} {Stars.Galaxy.Stars[Stars.Home].Kind}, {Stars.Galaxy.Stars.Length} stars)");
    }

    static Vector3 ToGodot(N.Vector3 v) => new(v.X, v.Y, v.Z);

    void Face(Vector3 dir)
    {
        Vector3 eye = _game.Camera.GlobalPosition;
        _game.FixedCamera = new Transform3D(Basis.LookingAt(dir, MathF.Abs(dir.Y) > 0.95f ? Vector3.Forward : Vector3.Up), eye);
    }

    IEnumerable<int> GalaxyClimb()
    {
        foreach (int x in Seated()) yield return x;
        foreach (int x in UpAndOut()) yield return x;
    }

    /// <summary>From the seat, out of the air and on up past the band, into the orbit's own sky.</summary>
    IEnumerable<int> UpAndOut()
    {
        Hurry(4);
        _game.Controls.Hold(Key.Space);
        for (int i = 0; i < 60 * 240 && _game.Spaceflight.Band < 1f; i++) yield return 0;
        _game.Controls.ReleaseAll();
        Hurry(1);
        GD.Print($"(galaxy: climbed, {(_game.InOrbit ? "in orbit" : "still on the surface")}, band {_game.Spaceflight.Band:F2})");
        for (int i = 0; i < 30; i++) yield return 0;
    }

    IEnumerable<int> NightGround()
    {
        foreach (int x in Midnight()) yield return x;
        int id = ChosenStar();
        Choose(id);
        Face(_game.Spaceflight.LookToward(id));
        foreach (int x in _drive.Wait(40)) yield return x; // the charts catch up
        foreach (int x in _drive.UntilSettled()) yield return x;
        foreach (int x in _drive.Wait(30)) yield return x;
    }

    IEnumerable<int> NightSpace()
    {
        foreach (int x in Seated()) yield return x;
        foreach (int x in Midnight()) yield return x;
        int id = ChosenStar();
        Choose(id);
        foreach (int x in UpAndOut()) yield return x;
        Face(_game.Spaceflight.LookToward(id));
        Choose(id);
        foreach (int x in _drive.Wait(30)) yield return x;
    }

    /// <summary>The lane network round you, a picked star several jumps off with its route, and the whole galaxy.</summary>
    IEnumerable<int> GalaxyLanes()
    {
        foreach (int x in Midnight()) yield return x;
        foreach (int x in _drive.Tap(Key.Tab)) yield return x;
        _game.Menu.OpenMap(MapLevel.Galaxy);
        MapLevels levels = _game.Menu.Root.Map.Levels;
        var lanes = new Lanes(Stars.Galaxy, Stars.Here);
        for (int i = 0; i < 90; i++) yield return 0;
        levels.Sky.Distance = 260;
        for (int i = 0; i < 30; i++) yield return 0;
        Snap("a-network");
        int pick = -1;
        foreach (int id in lanes.Connected)
        {
            var r = Lanes.Route(Stars.Galaxy, Stars.Here, id, Lanes.Range);
            if (r != null && r.Count == 5 && (pick < 0 || id % 7 == 0)) pick = id;
        }
        if (pick < 0) pick = lanes.Connected.Skip(5).FirstOrDefault();
        _game.Spaceflight.Charts.ChosenStar = pick;
        levels.Trail.Star = pick;
        for (int i = 0; i < 40; i++) yield return 0;
        Snap("b-route");
        foreach (int x in _drive.ClickWidget("map:whole")) yield return x;
        for (int i = 0; i < 40; i++) yield return 0;
        Snap("c-whole");
        foreach (int x in _drive.ClickWidget("map:on-me")) yield return x;
        levels.Trail.OpenStar(pick);
        for (int i = 0; i < 40; i++) yield return 0;
        Snap("d-their-system");
        levels.Trail.Open(MapLevel.Galaxy);
        for (int i = 0; i < 20; i++) yield return 0;
        Snap("e-picked-details");
    }

    /// <summary>The World map opened from a start put at --lat (0 north pole .. 1 south pole): poles top and bottom; then a close look at a sea ice edge.</summary>
    IEnumerable<int> WorldMapShots()
    {
        var planet = _game.Surface.Planet;
        double lat = Num("lat", 0.5);
        Vector3 at = _game.Survivor.WorldFeet;
        _game.Survivor.PlaceOutside(new System.Numerics.Vector3(at.X, at.Y + 5f, (float)(planet.Height * lat)));
        foreach (int x in _drive.Wait(20)) yield return x;
        MapLevels levels = _game.Menu.Root.Map.Levels;
        _game.Menu.OpenMap(MapLevel.World);
        for (int i = 0; i < 150; i++) yield return 0;
        Snap("world-lat" + (int)(lat * 100));
        // A sea ice edge: a frozen cell beside open water.
        var map = planet.Map;
        int best = -1;
        for (int k = 0; k < map.Count && best < 0; k++)
        {
            if (map.Biome[k] != (byte)Planets.Gen.Biome.FrozenSea) continue;
            int i = k % map.Width, j = k / map.Width;
            if (j > 2 && j < map.Height - 3 && map.Biome[map.Index(i + 1, j)] != (byte)Planets.Gen.Biome.FrozenSea && map.Elevation[map.Index(i + 1, j)] < 0f) best = k;
        }
        if (best >= 0 && levels.View != null)
        {
            levels.View.CentreX = map.CellX(best % map.Width);
            levels.View.CentreZ = map.CellY(best / map.Width);
            levels.View.MetresPerPixel = 6;
            levels.View.Settle();
            for (int i = 0; i < 40; i++) yield return 0;
            Snap("ice-edge");
        }
        else GD.Print("(no sea ice edge on this world)");
    }

    /// <summary>The 3D map through the game's own mouse: turned by a drag, a star picked by a click.</summary>
    IEnumerable<int> GalaxyMap()
    {
        foreach (int x in Midnight()) yield return x;
        foreach (int x in _drive.Tap(Key.Tab)) yield return x;
        _game.Menu.OpenMap(MapLevel.Galaxy);
        for (int i = 0; i < 90; i++) yield return 0;
        Snap("a-opened");
        foreach (int x in Drag(new Vector2(420, 620), new Vector2(760, 380))) yield return x;
        for (int i = 0; i < 40; i++) yield return 0;
        Snap("b-turned");
        MapLevels levels = _game.Menu.Root.Map.Levels;
        int id = ChosenStar();
        Vector2 at = levels.ScreenOf(id).Value;
        foreach (int x in MouseClick(at)) yield return x;
        for (int i = 0; i < 40; i++) yield return 0;
        GD.Print($"(galaxy: clicked {at}, chosen {_game.Spaceflight.Charts.ChosenStar}, wanted {id}, {Stars.Galaxy.Name(id)})");
        Snap("c-picked");
        foreach (int x in _drive.ClickWidget("map:whole")) yield return x;
        for (int i = 0; i < 40; i++) yield return 0;
        Snap("d-whole");
        foreach (int x in _drive.ClickWidget("map:on-me")) yield return x;
        for (int i = 0; i < 40; i++) yield return 0;
    }

    /// <summary>Over the map with the mouse held: moves in small steps, as a hand does.</summary>
    IEnumerable<int> Drag(Vector2 from, Vector2 to)
    {
        Controls c = _game.Controls;
        c.Mouse = from;
        yield return 0;
        c.PrimaryPressed = true;
        c.Primary = true;
        yield return 0;
        for (int i = 1; i <= 20; i++)
        {
            c.Mouse = from.Lerp(to, i / 20f);
            yield return 0;
        }
        c.Primary = false;
        c.PrimaryReleased = true;
        yield return 0;
    }

    IEnumerable<int> MouseClick(Vector2 at) => Drag(at, at);

    /// <summary>One notch of the wheel over the map, in (-1) or out (+1).</summary>
    IEnumerable<int> Wheel(int notch)
    {
        _game.Controls.Mouse = new Vector2(620, 480);
        yield return 0;
        _game.Controls.Wheel = notch;
        yield return 0;
    }

    /// <summary>The way in: the galaxy's band and its core, from space, where no ground is in the way.</summary>
    /// <summary>
    /// The galaxy's heart, the central black hole, where the sky really has it: the night held at
    /// the hour it stands about 30 degrees up with the sun well down (--time picks another), seen
    /// from the ground over the land, or from orbit straight at it, with nothing on the screen but
    /// the view. Prints how far off it is and how big it looks.
    /// </summary>
    IEnumerable<int> NightCore(bool space)
    {
        if (space) foreach (int x in Seated()) yield return x;
        Stars.Finish();
        PlanetSurface.Skies.SkyRig sky = _game.Surface.Sky;
        sky.Paused = true;
        _game.Zone.Weather.Sim.Snap(WeatherKind.Clear);
        double best = 0.0;
        float bestScore = float.MaxValue;
        for (int k = 0; k < 288; k++)
        {
            double t = k / 288.0;
            sky.TimeOfDay = t;
            sky.Track();
            float up = Mathf.RadToDeg(MathF.Asin(Math.Clamp(_game.Spaceflight.LookAlong(Stars.ToHole).Y, -1f, 1f)));
            float sun = sky.SunAt(t).Y;
            float score = MathF.Abs(up - 30f) + (sun < -0.3f ? 0f : 200f + sun * 100f);
            if (score < bestScore) (bestScore, best) = (score, t);
        }
        sky.TimeOfDay = Num("time", best);
        sky.Track();
        foreach (int x in _drive.Wait(30)) yield return x;
        Galaxy g = Stars.Galaxy;
        Heart hole = g.Hole;
        float far = N.Vector3.Distance(hole.Position, Stars.View.Eye);
        GD.Print($"(core: home {g.Name(Stars.Here)}, depth {g.DepthOf(Stars.Here):0.000}, {far:F1} LY from the hole; its disc {2f * Mathf.RadToDeg(MathF.Atan(hole.Outer / far)):F2} degrees across; " +
                 $"the bulge's light in front of it {Stars.View.HoleFront:F2}; time {sky.TimeOfDay:F3}, sun {Mathf.RadToDeg(MathF.Asin(sky.SunAt(sky.TimeOfDay).Y)):F0} degrees up)");
        if (space) foreach (int x in UpAndOut()) yield return x;
        Vector3 to = _game.Spaceflight.LookAlong(Stars.ToHole);
        GD.Print($"(core: the hole is {Mathf.RadToDeg(MathF.Asin(Math.Clamp(to.Y, -1f, 1f))):F1} degrees up, {(space ? "from orbit" : "from the ground")})");
        if (space) Face(to);
        else
        {
            // Under it, with the land along the bottom of the picture: the hole a little over the
            // middle, from a little over the ground (--lift, m) so no tree stands in front.
            var flat = new Vector3(to.X, 0f, to.Z).Normalized();
            float pitch = Math.Clamp(MathF.Asin(Math.Clamp(to.Y, -1f, 1f)) - Mathf.DegToRad(12f), Mathf.DegToRad(4f), Mathf.DegToRad(40f));
            Vector3 dir = flat * MathF.Cos(pitch) + Vector3.Up * MathF.Sin(pitch);
            Vector3 eye = _game.Camera.GlobalPosition + Vector3.Up * (float)Num("lift", 24);
            _game.FixedCamera = new Transform3D(Basis.LookingAt(dir, Vector3.Up), eye);
            foreach (int x in _drive.UntilSettled()) yield return x;
        }
        foreach (int x in _drive.Wait(40)) yield return x;
        foreach (Node n in _game.GetTree().Root.FindChildren("*", nameof(CanvasLayer), true, false)) ((CanvasLayer)n).Visible = false;
        foreach (int x in _drive.Wait(4)) yield return x;
    }

    /// <summary>
    /// The sun at one zoom on the way up (--fov, degrees; 8 by default), the clock held at
    /// mid-morning (--time): from the Skiff's seat on the ground, at the top of the climb with the
    /// air gone (the surface's sky still drawing it, --top m), and high in orbit (space's own sky).
    /// Its disc is the one size in all three (StarSystem.SunAngular); only the air's glow differs.
    /// Each shot from just outside the hull, toward the sun as that zone's light has it.
    /// </summary>
    IEnumerable<int> SunClimb()
    {
        foreach (int x in Seated()) yield return x;
        Vessel v = Skiff;
        PlanetSurface.Skies.SkyRig sky = _game.Surface.Sky;
        sky.TimeOfDay = Num("time", 0.40);
        sky.Paused = true;
        _game.Zone.Weather.Sim.Snap(WeatherKind.Clear);
        _game.Visor.Visible = false;
        float zoom = (float)Num("fov", 8), wide = _game.Settings.Fov;
        float angular = _game.Surface.Planet.StarSystem.SunAngular;
        GD.Print($"(sun: its disc {2f * Mathf.RadToDeg(angular):F3} degrees across, as the system has it; every shot at {zoom:F1} degrees, {2f * MathF.Tan(angular) / (2f * MathF.Tan(Mathf.DegToRad(zoom) * 0.5f)) * 900f:F0} pixels across in a 900-row picture)");
        IEnumerable<int> Shoot(string tag, bool space)
        {
            _game.Controls.ReleaseAll();
            Hurry(1);
            _game.FixedCamera = null;
            for (int i = 0; i < 3; i++) yield return 0;
            Vector3 toSun = (space ? _game.Orbit.Space.Sun : sky.Sun).GlobalBasis.Z.Normalized();
            Vector3 eye = _game.Camera.GlobalPosition + toSun * 3f + Vector3.Up * 2f;
            _game.Settings.Fov = zoom;
            _game.Camera.Fov = zoom;
            _game.FixedCamera = new Transform3D(Basis.LookingAt(toSun, MathF.Abs(toSun.Y) > 0.95f ? Vector3.Forward : Vector3.Up), eye);
            for (int i = 0; i < 20; i++) yield return 0;
            GD.Print($"(sun: {tag} at {v.Body.Position.Y:F0} m, the air {_game.Spaceflight.AirGone:P0} gone, {(_game.InOrbit ? $"in orbit, band {_game.Spaceflight.Band:F2}" : "the surface's sky")}; the sun {Mathf.RadToDeg(MathF.Asin(Math.Clamp(toSun.Y, -1f, 1f))):F0} degrees up)");
            Snap(tag);
            _game.FixedCamera = null;
            _game.Settings.Fov = wide;
            _game.Camera.Fov = wide;
        }
        foreach (int x in Shoot("a-ground", false)) yield return x;
        Hurry(3);
        _game.Controls.Hold(Key.Space);
        for (int i = 0; i < 60 * 120 && v.Body.Position.Y < (float)Num("top", 1035); i++) yield return 0;
        foreach (int x in Shoot("b-climb", false)) yield return x;
        Hurry(4);
        _game.Controls.Hold(Key.Space);
        for (int i = 0; i < 60 * 240 && _game.Spaceflight.Band < 1f; i++) yield return 0;
        for (int i = 0; i < 30; i++) yield return 0;
        foreach (int x in Shoot("c-orbit", true)) yield return x;
    }

    /// <summary>
    /// A ringed body from orbit at several distances (--ringed=1 picks a world whose giant has
    /// rings): from where you are, then from 40, 12 and 4 of its radii, each zoomed so its rings fill
    /// the middle of the picture. Its sphere is whole in every one, its ring passing in front of it
    /// and behind it. Prints how far off it is and how big it looks.
    /// </summary>
    IEnumerable<int> RingedGiant()
    {
        foreach (int x in ToOrbit()) yield return x;
        foreach (int x in UntilSurfaces()) yield return x;
        Vector3 world = _game.Orbit.ToWorld(N.Vector3.Zero).ToGodot(), out0 = (Skiff.Body.Position.ToGodot() - world).Normalized();
        Hurry(3);
        foreach (int x in _pilot.FlyTo(Skiff, () => world + out0 * (_game.Orbit.ShellRadius + 2500f), 60f, 120f)) yield return x;
        Hurry(1);
        _game.Controls.ReleaseAll();
        _game.Visor.Visible = false;
        PlanetSurface.Skies.SkyRig sky = _game.Surface.Sky;
        sky.Paused = true;
        OrbitZone o = _game.Orbit;
        PlanetSurface.Skies.StarSystem model = _game.Surface.Planet.StarSystem;
        double days = sky.Days;
        PlanetSurface.Skies.StarSystem.Orientation turned = model.Orient(days);
        Basis turn = o.Turn.ToBasis();
        PlanetSurface.Skies.Orbiter giant = model.Bodies.FirstOrDefault(b => b.Rings && b != model.Home) ?? model.Bodies.FirstOrDefault(b => b.Kind == PlanetSurface.Skies.OrbiterKind.GasGiant);
        if (giant == null)
        {
            GD.Print("(ringed: no giant in this system)");
            yield break;
        }
        PlanetSurface.Skies.D3 at = model.Position(giant, days) - model.Position(model.Home, days);
        PlanetSurface.Skies.D3 toStar = (model.Position(model.Star, days) - model.Position(giant, days)).Normalized;
        PlanetSurface.Skies.D3 side = toStar.Cross(new PlanetSurface.Skies.D3(0, 1, 0)).Normalized;
        float wide = _game.Camera.Fov;
        foreach (double r in new[] { 0.0, 40.0, 12.0, 4.0, -40.0, -12.0 })
        {
            PlanetSurface.Skies.D3 eye;
            if (r == 0.0)
            {
                _game.Spaceflight.ViewFrom = null;
                N.Vector3 g = o.ToGlobe(_game.Camera.GlobalPosition.ToNumerics());
                eye = new PlanetSurface.Skies.D3(g.X, g.Y, g.Z);
            }
            else
            {
                // A negative distance: from its night side, the star behind it.
                PlanetSurface.Skies.D3 from = r > 0.0
                    ? at + (toStar * 0.5 + side * 0.8 + new PlanetSurface.Skies.D3(0, 0.25, 0)).Normalized * (giant.Radius * r)
                    : at + (toStar * -0.85 + side * 0.45 + new PlanetSurface.Skies.D3(0, 0.25, 0)).Normalized * (giant.Radius * -r);
                eye = turned.ToGlobe(from);
                _game.Spaceflight.ViewFrom = eye;
                o.Globe.Hidden = true; // the sky as from out there: your own world (and its ring) is not where the camera is
            }
            PlanetSurface.Skies.Sighting s = model.See(giant, days, eye, turned);
            Vector3 look = (turn * new Vector3((float)s.Direction.X, (float)s.Direction.Y, (float)s.Direction.Z)).Normalized();
            float fov = Math.Clamp(2f * Mathf.RadToDeg(MathF.Atan(MathF.Tan(s.Angular) * 2.6f / 0.62f)), 0.3f, 75f);
            _game.Camera.Fov = fov;
            _game.FixedCamera = new Transform3D(Basis.LookingAt(look, Mathf.Abs(look.Y) > 0.95f ? Vector3.Forward : Vector3.Up), Skiff.Body.Position.ToGodot() + look * 30f);
            for (int i = 0; i < 10; i++) yield return 0;
            string tag = r == 0.0 ? "a-from-orbit" : r > 0.0 ? $"b-{r:00}r" : $"c-night-{-r:00}r";
            Snap(tag);
            GD.Print($"(ringed: {tag}: {giant.Name}{(giant.Rings ? ", ringed" : ", no rings")} from {s.Distance / 1000:F0} km, {Mathf.RadToDeg(s.Angular):F3} degrees radius, the view {fov:F2} degrees)");
        }
        _game.Spaceflight.ViewFrom = null;
        o.Globe.Hidden = false;
        _game.FixedCamera = null;
        _game.Camera.Fov = wide;
    }

    /// <summary>
    /// The sun from the ground at one zoom (--fov, degrees; 40 by default), the clock at the hour
    /// it stands about 12 degrees up (--up), clear, from a little over the ground (--lift): its size
    /// and look on this kind of world, the land along the bottom of the picture.
    /// </summary>
    IEnumerable<int> SunLook()
    {
        PlanetSurface.Skies.SkyRig sky = _game.Surface.Sky;
        sky.Paused = true;
        _game.Zone.Weather.Sim.Snap(WeatherKind.Clear);
        float want = Mathf.DegToRad((float)Num("up", 12));
        double best = 0.3;
        for (double t = 0.15; t < 0.5; t += 0.0025)
            if (MathF.Abs(MathF.Asin(sky.SunAt(t).Y) - want) < MathF.Abs(MathF.Asin(sky.SunAt(best).Y) - want)) best = t;
        sky.TimeOfDay = best;
        sky.Track();
        foreach (int x in _drive.Wait(30)) yield return x;
        _game.Visor.Visible = false;
        PlanetSurface.Skies.StarSystem model = _game.Surface.Planet.StarSystem;
        Vector3 toSun = sky.Sun.GlobalBasis.Z.Normalized();
        float zoom = (float)Num("fov", 40), up = MathF.Asin(Math.Clamp(toSun.Y, -1f, 1f));
        float pitch = MathF.Max(up - Mathf.DegToRad(zoom * 0.12f), Mathf.DegToRad(1f));
        Vector3 flat = new Vector3(toSun.X, 0f, toSun.Z).Normalized();
        _game.Settings.Fov = zoom;
        _game.Camera.Fov = zoom;
        _game.FixedCamera = new Transform3D(Basis.LookingAt(flat * MathF.Cos(pitch) + Vector3.Up * MathF.Sin(pitch), Vector3.Up), _game.Camera.GlobalPosition + Vector3.Up * (float)Num("lift", 20));
        foreach (int x in _drive.UntilSettled()) yield return x;
        foreach (int x in _drive.Wait(40)) yield return x;
        foreach (Node n in _game.GetTree().Root.FindChildren("*", nameof(CanvasLayer), true, false)) ((CanvasLayer)n).Visible = false;
        foreach (int x in _drive.Wait(4)) yield return x;
        GD.Print($"(sun: a {_game.Surface.Planet.Spec.Archetype} world round a {_game.Surface.Planet.Sky.Star} star, {model.Home.Habitable:F2} of its habitable distance: its disc {2f * Mathf.RadToDeg(model.SunAngular):F2} degrees across, heat {model.SunHeat:F2}; {Mathf.RadToDeg(up):F0} degrees up, the view {zoom:F0} degrees)");
    }

    /// <summary>
    /// A real eclipse from the start site (the deepest with the sun up in --days, 400 by default):
    /// the sun from the ground as it builds (nothing hidden yet, a half, nine tenths, its deepest),
    /// then from orbit at its deepest, the moon's shadow on the globe.
    /// </summary>
    IEnumerable<int> EclipseView()
    {
        PlanetSurface.Skies.SkyRig sky = _game.Surface.Sky;
        sky.Paused = true;
        _game.Zone.Weather.Sim.Snap(WeatherKind.Clear);
        _game.Visor.Visible = false;
        PlanetSurface.Skies.StarSystem model = _game.Surface.Planet.StarSystem;
        var (peak, most) = sky.NextEclipse(Num("days", 400));
        var (_, by) = model.Eclipse(peak, sky.Eye);
        GD.Print($"(eclipse: the deepest in sight hides {most:P1} of the sun, by {by?.Name ?? "nothing"}, day {peak:F3}; its disc {2f * Mathf.RadToDeg(model.SunAngular):F2} degrees across)");
        if (most <= 0.0) yield break;
        // On its way in: when it hides each share.
        double When(double share)
        {
            double t = peak;
            while (t > peak - 0.5 && model.Eclipse(t, sky.Eye).Covered > share) t -= 0.0002;
            return t;
        }
        float zoom = (float)Num("fov", 40), wide = _game.Settings.Fov;
        foreach (var (tag, at) in new[] { ("a-before", When(0.0) - 0.01), ("b-half", When(0.5)), ("c-nine-tenths", When(0.9)), ("d-deepest", peak) })
        {
            sky.Days = at;
            foreach (int x in _drive.Wait(8)) yield return x;
            _game.FixedCamera = null;
            foreach (int x in _drive.Wait(2)) yield return x;
            Vector3 toSun = sky.Sun.GlobalBasis.Z.Normalized();
            _game.Settings.Fov = zoom;
            _game.Camera.Fov = zoom;
            _game.FixedCamera = new Transform3D(Basis.LookingAt(toSun, MathF.Abs(toSun.Y) > 0.95f ? Vector3.Forward : Vector3.Up), _game.Camera.GlobalPosition + Vector3.Up * (float)Num("lift", 30));
            foreach (int x in _drive.Wait(20)) yield return x;
            GD.Print($"(eclipse: {tag}, day {at:F4}: {model.Eclipse(at, sky.Eye).Covered:P1} hidden; the sun's light {sky.Sun.LightEnergy:F3}, its share {sky.SunLit:F3}; {Mathf.RadToDeg(MathF.Asin(Math.Clamp(toSun.Y, -1f, 1f))):F0} degrees up)");
            Snap(tag);
        }
        _game.FixedCamera = null;
        _game.Settings.Fov = wide;
        _game.Camera.Fov = wide;
        foreach (int x in Seated()) yield return x;
        sky.Days = peak;
        foreach (int x in UpAndOut()) yield return x;
        // From up here, down at the globe: the moon's shadow on it.
        Vector3 centre = _game.Orbit.ToWorld(N.Vector3.Zero).ToGodot(), eye = _game.Camera.GlobalPosition;
        _game.Camera.Fov = (float)Num("orbitfov", 70);
        _game.FixedCamera = new Transform3D(Basis.LookingAt((centre - eye).Normalized(), Vector3.Up), eye);
        foreach (int x in _drive.Wait(30)) yield return x;
        GD.Print($"(eclipse: from orbit at day {sky.Days:F4}, {(_game.InOrbit ? "in orbit" : "not in orbit")}, the sun's light up here {_game.Orbit.Space.SunLit:F3} of its own)");
    }

    IEnumerable<int> GalaxyCore()
    {
        foreach (int x in Seated()) yield return x;
        foreach (int x in Midnight()) yield return x;
        foreach (int x in UpAndOut()) yield return x;
        Face(_game.Spaceflight.LookAlong(N.Vector3.Normalize(-Stars.Galaxy.Stars[Stars.Here].Position)));
        foreach (int x in _drive.Wait(30)) yield return x;
    }

    /// <summary>
    /// The sky over the map's north and south seam: the sleeper set down row by row across it
    /// (STAGING: the walk is long; the sky is what is looked at), the camera fixed on one direction.
    /// </summary>
    IEnumerable<int> GalaxySeam()
    {
        foreach (int x in Midnight()) yield return x;
        _game.Visor.Visible = false;
        var planet = _game.Surface.Planet;
        double H = planet.Height;
        float px = _game.Survivor.WorldFeet.X;
        int watch = Stars.Galaxy.Neighbours(Stars.Here)[0];
        Vector3 last = default;
        (string tag, double z)[] rows = { ("a-300-before", 300), ("b-120-before", 120), ("c-10-before", 10), ("d-10-after", H - 10), ("e-120-after", H - 120), ("f-300-after", H - 300) };
        foreach (var (tag, z) in rows)
        {
            var feet = _game.Zone.GroundAt(px, (float)z);
            _game.Survivor.PlaceOutside(new N.Vector3(px, MathF.Max(feet.Y, 0f) + 1f, (float)z));
            foreach (int x in _drive.Wait(30)) yield return x;
            Vector3 eye = _game.Survivor.WorldEye();
            _game.FixedCamera = new Transform3D(Basis.LookingAt(new Vector3(0.1f, 1f, -0.5f).Normalized(), Vector3.Forward), eye + Vector3.Up * 2f);
            foreach (int x in _drive.Wait(30)) yield return x;
            Vector3 d = _game.Spaceflight.LookToward(watch);
            GD.Print($"(galaxy: row {z:F0} of {H:F0}: a neighbour stands at {d}, {(tag[0] == 'a' ? 0f : Mathf.RadToDeg(last.AngleTo(d))):F1} degrees from the last row)");
            last = d;
            Snap(tag);
        }
    }

    /// <summary>A jump, as far as the sky goes: there is no drive yet, so the catalogue is simply asked for the sky from a neighbour.</summary>
    IEnumerable<int> GalaxyJump()
    {
        foreach (int x in Midnight()) yield return x;
        int home = Stars.Here;
        List<int> near = Stars.Galaxy.Neighbours(home);
        // A neighbour low in this sky: from there, home stands high in the same world's sky, in the opposite direction.
        int to = near.OrderBy(id => _game.Spaceflight.LookToward(id).Y).ElementAt(near.Count / 8);
        Vector3 back = -_game.Spaceflight.LookToward(to);
        Face(back);
        foreach (int x in _drive.Wait(30)) yield return x;
        Snap("before");
        Stars.MoveTo(to);
        Stars.Finish();
        GD.Print($"(galaxy: jumped from {home} to {to}, {Stars.Galaxy.Name(to)}: staged, the sky only)");
        foreach (int x in _drive.Wait(50)) yield return x;
        Choose(home);
        Face(_game.Spaceflight.LookToward(home));
        foreach (int x in _drive.Wait(30)) yield return x;
    }

    /// <summary>The sky from a star inside an emission nebula: staged the same way as the jump.</summary>
    IEnumerable<int> GalaxyNebula()
    {
        foreach (int x in Midnight()) yield return x;
        Galaxy g = Stars.Galaxy;
        Nebula n = g.Nebulae.Where(k => k.Kind == NebulaKind.Emission).OrderByDescending(k => k.Radius).First();
        int inside = g.Stars.Where(s => s.Nebula == n.Id).OrderBy(s => N.Vector3.Distance(s.Position, n.Centre)).First().Id;
        Stars.MoveTo(inside);
        Stars.Finish();
        GD.Print($"(galaxy: inside the {n.Name}, radius {n.Radius:F0} LY, at {g.Name(inside)}: staged, the sky only)");
        foreach (int x in _drive.Wait(10)) yield return x;
        Face(new Vector3(0.3f, 0.6f, -0.75f).Normalized());
        foreach (int x in _drive.Wait(30)) yield return x;
    }

    /// <summary>The wheel, all the way out from the ground under you to the galaxy, and in again, a frame of each level; and into a neighbour's system.</summary>
    IEnumerable<int> GalaxyTrail()
    {
        foreach (int x in Midnight()) yield return x;
        foreach (int x in _drive.Tap(Key.Tab)) yield return x;
        MapLevels levels = _game.Menu.Root.Map.Levels;
        _game.Menu.OpenMap(MapLevel.Local);
        for (int i = 0; i < 20; i++) yield return 0;
        IEnumerable<int> Until(MapLevel level, int notch, string tag)
        {
            for (int i = 0; i < 300 && levels.Level != level; i++)
            {
                foreach (int x in Wheel(notch)) yield return x;
                for (int k = 0; k < 4; k++) yield return 0;
            }
            for (int i = 0; i < 40; i++) yield return 0;
            GD.Print($"(galaxy: wheel to {levels.Level}{(levels.Level == level ? "" : $", wanted {level}")}, trail {string.Join(" > ", levels.Trail.Crumbs(Stars.Galaxy.Name(levels.Trail.Star)).Select(c => c.name))})");
            Snap(tag);
        }
        foreach (int x in Until(MapLevel.Local, 1, "up-0-here")) yield return x;
        foreach (int x in Until(MapLevel.World, 1, "up-1-world")) yield return x;
        foreach (int x in Until(MapLevel.HighOrbit, 1, "up-2-orbit")) yield return x;
        foreach (int x in Until(MapLevel.System, 1, "up-3-system")) yield return x;
        foreach (int x in Until(MapLevel.Galaxy, 1, "up-4-galaxy")) yield return x;
        // Down into a neighbour nobody has been to: it opens to what the catalogue knows, and goes no lower.
        int neighbour = ChosenStar();
        foreach (int x in MouseClick(levels.ScreenOf(neighbour).Value)) yield return x;
        for (int i = 0; i < 10; i++) yield return 0;
        foreach (int x in Until(MapLevel.System, -1, "down-1-neighbour")) yield return x;
        foreach (int x in Until(MapLevel.Galaxy, 1, "down-2-back-out")) yield return x;
        foreach (int x in _drive.ClickWidget("map:clear-star")) yield return x;
        for (int i = 0; i < 10; i++) yield return 0;
        // And down into your own, all the way to the ground.
        foreach (int x in Until(MapLevel.System, -1, "down-3-your-system")) yield return x;
        foreach (int x in Until(MapLevel.HighOrbit, -1, "down-4-orbit")) yield return x;
        foreach (int x in Until(MapLevel.World, -1, "down-5-world")) yield return x;
        foreach (int x in Until(MapLevel.Local, -1, "down-6-here")) yield return x;
    }

    /// <summary>
    /// What the sky costs: whole frames, with the galaxy on and off in turn, on the ground and in space.
    /// Under --fixed-fps frames run flat out, so the wall clock per frame drawn is the true cost; rounds
    /// alternate because other work shares the machine.
    /// </summary>
    IEnumerable<int> GalaxyPerf()
    {
        foreach (int x in Seated()) yield return x;
        foreach (int x in Midnight()) yield return x;
        _game.Visor.Visible = false;
        IEnumerable<int> Rounds(ShaderMaterial m, string where)
        {
            var times = new[] { new List<double>(), new List<double>() };
            for (int round = 0; round < 8; round++)
            {
                bool on = round % 2 == 0;
                m.SetShaderParameter("galaxy_on", on);
                foreach (int x in _drive.Wait(30)) yield return x;
                var watch = System.Diagnostics.Stopwatch.StartNew();
                int first = Engine.GetFramesDrawn();
                const int Frames = 150;
                while (Engine.GetFramesDrawn() < first + Frames) yield return 0;
                times[on ? 0 : 1].Add(watch.Elapsed.TotalMilliseconds / Frames);
            }
            m.SetShaderParameter("galaxy_on", true);
            double Middle(List<double> v) => v.OrderBy(t => t).ElementAt(v.Count / 2);
            GD.Print($"(perf: {where}: frame {Middle(times[0]):F2} ms with the galaxy, {Middle(times[1]):F2} ms without, cost {Middle(times[0]) - Middle(times[1]):F2} ms; rounds on {string.Join(" ", times[0].Select(t => t.ToString("F2")))}, off {string.Join(" ", times[1].Select(t => t.ToString("F2")))})");
        }
        N.Vector3 core = N.Vector3.Normalize(-Stars.Galaxy.Stars[Stars.Here].Position);
        Vector3 band = _game.Spaceflight.LookAlong(core);
        Face(band.Y > 0.1f ? band : new Vector3(0.2f, 0.8f, -0.5f).Normalized());
        foreach (int x in Rounds(_game.Surface.Sky.Environment.Sky.SkyMaterial as ShaderMaterial, "ground at night")) yield return x;
        foreach (int x in UpAndOut()) yield return x;
        Face(_game.Spaceflight.LookAlong(core));
        foreach (int x in Rounds(_game.Orbit.Space.Environment.Sky.SkyMaterial as ShaderMaterial, "space, looking at the core")) yield return x;
    }
}
