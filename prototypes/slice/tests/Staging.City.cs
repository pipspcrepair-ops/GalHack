using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using GalHack.Physics.Engine;
using Planets.Gen;
using WeatherKind = Weather.Model.WeatherKind;

namespace Slice.Tests;

/// <summary>
/// A city in the game itself (--view=city; Screenshot asks for one on any lived-in world): the castaway set down
/// at each of --at metres off its edge (2000,300 by default) on the line from the city's middle toward the start, looking
/// at it from --up metres over the ground (90 at 2 km, 20 nearer), with the place layer's far tiles and merged
/// blocks as they are at that distance, and what stands there printed. Frames go beside --out.
/// </summary>
public sealed partial class Staging
{
    IEnumerable<int> CityView()
    {
        _game.Zone.Weather.Sim.Snap(WeatherKind.Clear);
        _game.Visor.Visible = false;
        var world = _game.Surface.Planet;
        if (world.Infra?.City is not { Sites.Count: > 0 } city)
        {
            GD.Print($"(city: none on this world: {world.Infra?.City?.Why ?? "not planned"})");
            yield break;
        }
        float[] marks = Floats("at", "2000,300");
        Vector3 from = _game.Survivor.WorldFeet;
        var middle = world.NearestCopy(new Vector3((float)city.CenterX, 0f, (float)city.CenterZ), from);
        middle.Y = world.GroundHeight(Mathx.WrapX(middle.X, world.Width), Mathx.Wrap(middle.Z, world.Height));
        Vector3 away = new Vector3(from.X - middle.X, 0f, from.Z - middle.Z);
        away = away.LengthSquared() > 1f ? away.Normalized() : Vector3.Back;
        GD.Print($"(city at {city.CenterX:F0},{city.CenterZ:F0}, radius {city.Radius:F0}, {city.Sites.Count} buildings)");
        foreach (float mark in marks)
        {
            float off = mark + (float)city.Radius;
            float up0 = (float)Num("up", mark > 1000f ? 90 : 20);
            // The nearest heading round from the start's side that leaves a clear line to the middle.
            Vector3 dir = away;
            for (int k = 0; k < 24; k++)
            {
                Vector3 d = away.Rotated(Vector3.Up, Mathf.DegToRad(15f * ((k + 1) / 2) * (k % 2 == 0 ? 1 : -1)));
                Vector3 e = middle + d * off;
                e.Y = world.GroundHeight(Mathx.WrapX(e.X, world.Width), Mathx.Wrap(e.Z, world.Height)) + up0;
                Vector3 target = middle + Vector3.Up * 30f;
                bool clear = true;
                for (float t = 0.05f; t < 0.8f && clear; t += 0.025f)
                {
                    Vector3 p = e.Lerp(target, t);
                    clear = world.GroundHeight(Mathx.WrapX(p.X, world.Width), Mathx.Wrap(p.Z, world.Height)) < p.Y - 4f;
                }
                if (clear) { dir = d; break; }
            }
            Vector3 feet = middle + dir * off;
            feet.Y = world.GroundHeight(Mathx.WrapX(feet.X, world.Width), Mathx.Wrap(feet.Z, world.Height)) + 0.5f;
            _game.Survivor.PlaceOutside(feet.ToNumerics());
            float up = up0;
            Vector3 eye = feet + Vector3.Up * up;
            Vector3 look = middle + Vector3.Up * (mark > 1000f ? 60f : 25f);
            double day = _game.Zone.Sky.TimeOfDay;
            for (int i = 0; i < 1200; i++)
            {
                _game.FixedCamera = new Transform3D(Basis.LookingAt(look - eye, Vector3.Up), eye);
                if (i % 120 == 2) day = LocalDay();
                _game.Zone.Sky.TimeOfDay = day;
                yield return 0;
                if (i > 120 && _game.Zone.Streamer.Settled && !_game.Zone.Pois.MidPending) break;
            }
            for (int i = 0; i < 30; i++) yield return 0;
            Snap($"{mark:0}m");
            GD.Print($"(city {mark:0} m: eye {eye}, settled {_game.Zone.Streamer.Settled}, merged blocks pending {_game.Zone.Pois.MidPending}, places standing {_game.Places.Standing.Count})");
        }
        // From the air over its edge, looking down across it: the ground between its buildings.
        if (_args.GetValueOrDefault("air", "1") != "0")
        {
            Vector3 feet = middle + away * ((float)city.Radius * 0.9f);
            feet.Y = world.GroundHeight(Mathx.WrapX(feet.X, world.Width), Mathx.Wrap(feet.Z, world.Height)) + 0.5f;
            _game.Survivor.PlaceOutside(feet.ToNumerics());
            Vector3 eye = middle + away * ((float)city.Radius * 0.9f + 250f) + Vector3.Up * (middle.Y + 260f - middle.Y);
            double day = _game.Zone.Sky.TimeOfDay;
            for (int i = 0; i < 1200; i++)
            {
                _game.FixedCamera = new Transform3D(Basis.LookingAt(middle + away * (float)city.Radius * 0.5f - eye, Vector3.Up), eye);
                if (i % 120 == 2) day = LocalDay();
                _game.Zone.Sky.TimeOfDay = day;
                yield return 0;
                if (i > 120 && _game.Zone.Streamer.Settled && !_game.Zone.Pois.MidPending) break;
            }
            for (int i = 0; i < 30; i++) yield return 0;
            Snap("air");
        }
        _game.FixedCamera = null;
    }

    /// <summary>
    /// The time that is --day (0.42 by default: late morning) where the eye stands now: the sky is the real one,
    /// so a city a few kilometres east or west of the start has its own hour.
    /// </summary>
    double LocalDay()
    {
        var sky = _game.Zone.Sky;
        double noon = 0.5;
        float top = -2f;
        for (int k = 0; k < 240; k++)
        {
            float y = sky.SunAt(k / 240.0).Y;
            if (y > top) { top = y; noon = k / 240.0; }
        }
        double t = noon + (Num("day", 0.42) - 0.5);
        return t - Math.Floor(t);
    }
}
