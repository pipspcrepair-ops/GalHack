using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Godot;
using PlanetSurface.Poi;

namespace Slice.Tests;

/// <summary>
/// A place coming into view at hover-bike speed: the sleeper is carried toward the nearest place
/// past 900 m at --speed m/s (38 by default) a metre and a half over the ground, looking at it, and
/// a frame is kept at each of --at metres off (700,500,350,250,150), with what stands there printed.
/// Run windowed without --fixed-fps, so the loading runs on its real clock.
/// </summary>
public sealed partial class Staging
{
    IEnumerable<int> ApproachView()
    {
        _game.Zone.Sky.TimeOfDay = Num("time", 0.42);
        _game.Zone.Weather.Sim.Snap(Weather.Model.WeatherKind.Clear);
        _game.Visor.Visible = false;
        float speed = (float)Num("speed", 38);
        float[] marks = _args.GetValueOrDefault("at", "700,500,350,250,150").Split(',').Select(s => float.Parse(s, CultureInfo.InvariantCulture)).ToArray();
        var places = _game.Places;
        var world = _game.Surface.Planet;
        Vector3 at = _game.Survivor.WorldFeet;
        PoiSite site = world.Pois.Where(s => s.Instance != null && places.DistanceTo(s, at) > 900f).OrderBy(s => places.DistanceTo(s, at)).FirstOrDefault();
        if (site == null) yield break;
        Vector3 middle = world.NearestCopy(new Vector3(site.Origin.X, site.Origin.Y, site.Origin.Z), at);
        // Start 900 m out on the line from here, so the run is the same length whatever the start.
        Vector3 away = new Vector3(at.X - middle.X, 0f, at.Z - middle.Z).Normalized();
        at = middle + away * (900f + places.DistanceTo(site, middle));
        double started = Time.GetTicksMsec() / 1000.0;
        int next = 0;
        for (int tick = 0; tick < 60 * 120 && next < marks.Length; tick++)
        {
            Vector3 way = new Vector3(middle.X - at.X, 0f, middle.Z - at.Z);
            at += way.Normalized() * speed / 60f;
            at.Y = world.GroundHeight(Planets.Gen.Mathx.WrapX(at.X, world.Width), Planets.Gen.Mathx.Wrap(at.Z, world.Height)) + 1.5f;
            _game.Survivor.PlaceOutside(new System.Numerics.Vector3(at.X, at.Y, at.Z));
            Vector3 eye = at + Vector3.Up * 1.6f;
            _game.FixedCamera = new Transform3D(Basis.LookingAt(middle + Vector3.Up * 6f - eye, Vector3.Up), eye);
            yield return 0;
            float d = places.DistanceTo(site, at);
            if (d > marks[next]) continue;
            Snap($"{marks[next]:0}m");
            GD.Print($"(approach {marks[next]:0} m: {site.Name}, {Time.GetTicksMsec() / 1000.0 - started:F1} s in at {speed:F0} m/s; standing {places.PlaceOf(site)?.Ready == true}; places standing {places.Standing.Count})");
            next++;
        }
        _game.FixedCamera = null;
    }
}
