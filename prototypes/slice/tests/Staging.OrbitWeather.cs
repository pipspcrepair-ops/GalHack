using System;
using System.Collections.Generic;
using System.Linq;
using GalHack.Physics;
using GalHack.Physics.Engine;
using Godot;
using Slice.Orbit;
using Slice.Vessels;
using Weather.Model;
using N = System.Numerics;

namespace Slice.Tests;

/// <summary>
/// The live weather from orbit: a front (--weather=Thunderstorm, or DustStorm on a dry world; any
/// other weather has none) set coming over the start by the weather itself, seen from the ground
/// first, then the climb, and frames from high over it: the front and its storms by day, the same
/// view a minute on, a look along its storms, the equator straight on, the night side with its
/// lightning, and back down under it (--down=0 stops in orbit). --layer=1 also saves the weather
/// layer and a frame of the globe without its clouds. The liberties: the front is started (its
/// weather is the world's own), the clock is set for day and night, and long stretches run at a
/// raised time-scale, printed.
/// </summary>
public sealed partial class Staging
{
    IEnumerable<int> OrbitWeatherView()
    {
        var sim = _game.Zone.Weather.Sim;
        var sky = _game.Zone.Sky;
        WeatherKind kind = Enum.Parse<WeatherKind>(_args.GetValueOrDefault("weather", "Thunderstorm"));
        FrontKind frontKind = kind == WeatherKind.DustStorm ? FrontKind.Dust : FrontKind.Storm;
        // In the Skiff's seat first (building it clears the sky), then the weather comes in.
        foreach (int x in Seated()) yield return x;
        sim.Snap(kind);
        sim.ClearTornadoes();
        Vector3 start = _game.Survivor.WorldFeet;
        if (Num("front", 1) != 0 && kind is WeatherKind.Thunderstorm or WeatherKind.Tornado or WeatherKind.DustStorm) sim.StartFront(frontKind, start, (float)Num("dist", 2600), null);
        else sim.FrontsOff = Num("front", 1) == 0; // loose cells, the weather's own
        _game.Visor.Visible = false;
        _game.Hands.Visible = false;
        sky.TimeOfDay = Num("time", 0.40);
        Front f = sim.Front;
        // From the ground: the front coming, its deck along the horizon.
        if (f != null)
        {
            Vector3 toward = new Vector3(-f.Heading.X, 0f, -f.Heading.Y);
            Vector3 eye = Skiff.Body.Position.ToGodot() + Vector3.Up * 4f - toward * 6f;
            _game.FixedCamera = new Transform3D(Basis.LookingAt(toward * MathF.Cos(0.12f) + Vector3.Up * MathF.Sin(0.12f), Vector3.Up), eye);
            for (int i = 0; i < 40; i++) yield return 0;
            Snap("ground");
            Report("ground");
            _game.FixedCamera = null;
        }
        // Straight up and out.
        Hurry(4);
        _game.Controls.Hold(Key.Space);
        for (int i = 0; i < 60 * 150 && !_game.InOrbit; i++) yield return 0;
        _game.Controls.ReleaseAll();
        Hurry(1);
        if (!_game.InOrbit) yield break;
        for (int i = 0; i < 30; i++) yield return 0;
        Vessel v = Skiff;
        OrbitZone orbit = _game.Orbit;
        Vector3 centre = orbit.ToWorld(N.Vector3.Zero).ToGodot();
        Vector3 up = (v.Body.Position.ToGodot() - centre).Normalized();
        Hurry(3);
        foreach (int x in _pilot.FlyTo(v, () => centre + up * (orbit.ShellRadius + (float)Num("away", 3200)), 40f, 90f)) yield return x;
        Hurry(1);
        _game.Controls.ReleaseAll();
        sky.Paused = true;
        Vector3 at = v.Body.Position.ToGodot();
        up = (at - centre).Normalized();
        SunOver(up, (float)Num("sun", 0.55));
        // Straight down at the world from beside the Skiff, clear of its hull.
        Vector3 side = up.Cross(MathF.Abs(up.Y) < 0.9f ? Vector3.Up : Vector3.Right).Normalized();
        Vector3 eyeHigh = at + up * 25f + side * 50f;
        Transform3D high = new(Basis.LookingAt(centre - eyeHigh, side.Cross(up)), eyeHigh);
        _game.FixedCamera = high;
        for (int i = 0; i < 60 * 8; i++) yield return 0; // a pass or two of the weather layer
        Snap("orbit");
        Report("orbit");
        if (_args.GetValueOrDefault("layer") == "1")
        {
            // Looking into it: the weather layer itself, and the globe without its clouds.
            string o = _args.GetValueOrDefault("out", "seam.png");
            orbit.Globe.Weather.Picture().SavePng(System.IO.Path.ChangeExtension(o, null) + "-layer.png");
            var sheet = orbit.Globe.GetNode<MeshInstance3D>("Clouds");
            sheet.Visible = false;
            for (int i = 0; i < 4; i++) yield return 0;
            Snap("orbit-noclouds");
            sheet.Visible = true;
        }
        // A minute on, the same view.
        Hurry(3);
        for (int i = 0; i < 60 * 20; i++) yield return 0;
        Hurry(1);
        for (int i = 0; i < 30; i++) yield return 0;
        Snap("orbit-later");
        Report("orbit-later");
        // Across the place under you toward the front, lower in the view.
        f = sim.Front;
        if (f != null)
        {
            Vector3 ahead = new Vector3(f.Heading.X, 0f, f.Heading.Y);
            ahead = (ahead - up * ahead.Dot(up)).Normalized();
            Vector3 look = centre + up * orbit.GlobeRadius - ahead * 2000f;
            _game.FixedCamera = new Transform3D(Basis.LookingAt(look - eyeHigh, up), eyeHigh);
            for (int i = 0; i < 20; i++) yield return 0;
            Snap("orbit-storms");
        }
        // Straight at the equator, north up, from as high, over the same longitude.
        {
            var (ex, _) = orbit.Under(at);
            var dir = orbit.Map.Direction(ex, orbit.Below.Height * 0.5);
            Vector3 toEquator = (orbit.ToWorld(dir * orbit.GlobeRadius).ToGodot() - centre).Normalized();
            Vector3 north = (orbit.ToWorld(N.Vector3.UnitY * orbit.GlobeRadius).ToGodot() - centre).Normalized();
            Vector3 eye = centre + toEquator * (at - centre).Length();
            SunOver(toEquator, (float)Num("sun", 0.55));
            _game.FixedCamera = new Transform3D(Basis.LookingAt(centre - eye, north), eye);
            for (int i = 0; i < 30; i++) yield return 0;
            Snap("equator");
            Report("equator");
        }
        // The night side, the storms flickering, a strike or two coming down.
        SunOver(up, (float)Num("night", -0.95));
        _game.FixedCamera = high;
        for (int i = 0; i < 60; i++) yield return 0;
        var (mx, mz) = orbit.Under(at);
        Vector3 under = _game.Surface.Planet.NearestCopy(new Vector3((float)mx, 0f, (float)mz), start);
        for (int k = 0; k < 4; k++)
        {
            f = sim.Front;
            if (f is { Kind: FrontKind.Storm }) sim.StartStrike(HazardKind.Lightning, f.EdgeAt(f.Along(under) + (k - 1.5f) * 700f, under) - new Vector3(f.Heading.X, 0f, f.Heading.Y) * 500f);
            for (int i = 0; i < 200 && !sim.Events.Any(e => e.Kind == WeatherEventKind.LightningStrike); i++) yield return 0;
            yield return 0;
            Snap($"night{k}");
            for (int i = 0; i < 25; i++) yield return 0;
        }
        _game.FixedCamera = null;
        sky.Paused = false;
        SunOver(up, (float)Num("sun", 0.55));
        if (Num("down", 1) == 0) yield break;
        // Back down, under it: over the crossing again, level, and down on the thrusters.
        Hurry(3);
        foreach (int x in _pilot.FlyTo(v, () => centre + up * (orbit.ShellRadius + 250f), 40f, 90f)) yield return x;
        Vector3 level = side.Cross(up);
        foreach (int x in _pilot.TurnTo(v, level, up, 3f, 20f)) yield return x;
        _game.Controls.Hold(Key.Ctrl);
        for (int i = 0; i < 60 * 120 && (_game.InOrbit || v.Body.Position.Y > Num("below", 700)); i++) yield return 0;
        _game.Controls.ReleaseAll();
        Hurry(1);
        for (int i = 0; i < 30; i++) yield return 0;
        f = sim.Front;
        if (f != null)
        {
            Vector3 p = v.Body.Position.ToGodot();
            // Toward the edge: at its deck over you, or at its wall coming.
            Vector3 toward = f.Ahead(p) > 0f ? new Vector3(-f.Heading.X, 0f, -f.Heading.Y) : new Vector3(f.Heading.X, 0f, f.Heading.Y);
            Vector3 eye = p + Vector3.Up * 6f + toward * 10f;
            _game.FixedCamera = new Transform3D(Basis.LookingAt(toward * MathF.Cos(0.1f) + Vector3.Up * MathF.Sin(0.1f), Vector3.Up), eye);
            for (int i = 0; i < 30; i++) yield return 0;
            Snap("below");
            Report("below");
        }
    }

    /// <summary>The clock set so the sun stands this high (-1 to 1) over a direction from the globe's centre.</summary>
    void SunOver(Vector3 up, float height)
    {
        var sky = _game.Surface.Sky;
        double best = sky.TimeOfDay;
        float bestOff = float.MaxValue;
        for (double t = 0; t < 1; t += 0.002)
        {
            sky.TimeOfDay = t;
            sky.Track();
            float off = MathF.Abs(sky.ToSun.Dot(up) - height);
            if (off < bestOff) (bestOff, best) = (off, t);
        }
        sky.TimeOfDay = best;
        sky.Track();
        GD.Print($"(weather shot: the sun {sky.ToSun.Dot(up):F2} over the place under you at {best:F3} of the day)");
    }

    void Report(string tag)
    {
        var sim = _game.Surface.Weather.Sim;
        Front f = sim.Front;
        GlobeWeather layer = _game.Spaceflight.Orbit?.Globe.Weather;
        Vector3 eye = _game.Camera.GlobalPosition;
        string where = _game.InOrbit ? $"over map {_game.Spaceflight.Orbit.Under(eye)}" : $"at {eye}";
        string front = f == null ? "none" : $"{f.Kind} {f.Strength:F2}, heading {f.Heading}, {f.Speed:F1} m/s, {f.Depth:F0} m deep";
        GD.Print($"(weather shot {tag}: {sim.Dominant}, front {front}, {sim.Cells.Count} cells ({string.Join(", ", sim.Cells.Select(c => $"{c.Strength:F2}"))}), " +
            $"weather time {sim.Time:F0} s, layer passes {layer?.Passes}, every {layer?.Period:F1} s, worst {layer?.WorstFrameMs:F2} ms, {where})");
    }
}
