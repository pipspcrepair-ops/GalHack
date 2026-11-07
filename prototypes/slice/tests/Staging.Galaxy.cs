using System;
using System.Collections.Generic;
using System.Linq;
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
        "galaxy-map" => GalaxyMap(),
        "world-map" => WorldMapShots(),
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
    IEnumerable<int> GalaxyCore()
    {
        foreach (int x in Seated()) yield return x;
        foreach (int x in Midnight()) yield return x;
        foreach (int x in UpAndOut()) yield return x;
        Face(_game.Spaceflight.LookAlong(N.Vector3.Normalize(-Stars.Galaxy.Stars[Stars.Here].Position)));
        foreach (int x in _drive.Wait(30)) yield return x;
    }

    /// <summary>
    /// The sky over the map's north and south seam: the castaway set down row by row across it
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
