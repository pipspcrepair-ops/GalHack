using System.Collections.Generic;
using System.Linq;
using Godot;
using Slice.Ui;
using WeatherKind = Weather.Model.WeatherKind;

namespace Slice.Tests;

/// <summary>
/// Markers on the things themselves: <c>--view=markers [--markers=compass] --out=FILE.png</c>, out of the bunker in daylight with a map
/// mark set 90 m off and the scanner just pinged, looking toward the place in view from the way out: its marker on
/// the place, the mark's and the pings' on their spots, and whatever is behind pinned to the edge.
/// </summary>
public sealed partial class Staging
{
    IEnumerable<int> MarkersShot()
    {
        if (_args.GetValueOrDefault("markers", "world") == "compass") { _game.Settings.MarkersInWorld = false; _game.Settings.Changed(); } // --markers=compass: the old marks and labels on the compass strip
        _game.Zone.Sky.TimeOfDay = Num("time", 0.36);
        _game.Zone.Weather.Sim.Snap(WeatherKind.Clear);
        _game.Zone.Wildlife.Frozen = true;
        foreach (int x in _drive.Wait(30)) yield return x;
        Vector3 eye = _game.Survivor.WorldEye();
        Visor.Marker place = _game.Visor.Markers().FirstOrDefault(m => m.Kind == "place");
        Vector3 toward = place.Kind == "place" ? place.At : eye + _game.Survivor.WorldLook() * 100f;
        Vector3 flat = new Vector3(toward.X - eye.X, 0f, toward.Z - eye.Z).Normalized();
        Vector3 side = new(-flat.Z, 0f, flat.X);
        Vector3 mark = eye + flat * 80f + side * 35f;
        _game.Spaceflight.Charts.Mark(mark.X, mark.Z);
        // More marks ahead, at different distances and bearings, so the view has several to show.
        foreach (var (turn, range) in new[] { (-0.5f, 45f), (-0.2f, 120f), (0.15f, 70f), (0.45f, 200f), (0.7f, 35f) })
        {
            Vector3 way = flat.Rotated(Vector3.Up, turn) * range;
            _game.Spaceflight.Charts.Mark(eye.X + way.X, eye.Z + way.Z);
        }
        _game.Scan();
        // Toward the middle of what is marked out there, so most of it is in view.
        foreach (int x in _drive.Wait(2)) yield return x;
        Vector3 sum = Vector3.Zero;
        foreach (Visor.Marker m in _game.Visor.Markers().Where(m => m.Kind != "bunker"))
            sum += new Vector3(m.At.X - eye.X, 0f, m.At.Z - eye.Z).Normalized();
        Vector3 look = sum.LengthSquared() > 0.01f ? sum.Normalized() : flat;
        foreach (int x in _drive.LookAt(eye + look * 60f + Vector3.Down * 4f, 12)) yield return x;
        _game.Controls.Mouse = new Vector2(-100, -100);
        foreach (int x in _drive.Wait(20)) yield return x;
        foreach (var p in _game.Visor.LayoutMarkers(_game.GetViewport().GetVisibleRect().Size, _game.Visor.UiScale))
            GD.Print($"marker {p.Mark.Kind} {p.Mark.Tag} at {p.At} {(p.Pinned ? "pinned" : "")} {(p.Labelled ? "labelled" : "icon")} {Visor.Distance(p.Distance)}");
        foreach (int x in Shot(_args.GetValueOrDefault("out", "user://markers.png"))) yield return x;
    }

    /// <summary>
    /// The scanner's place marks: <c>--view=placescan --out=FILE.png</c>, in daylight with the scanner in hand, stood
    /// 350 m off the place with the most others within the scan's range, just scanned, looking toward the marks.
    /// </summary>
    IEnumerable<int> PlaceScanShot()
    {
        _game.Zone.Weather.Sim.Snap(WeatherKind.Clear);
        _game.Zone.Wildlife.Frozen = true;
        _game.Pack.Add("scanner", 1);
        foreach (int x in CoreScanStage.Hold(Play, "scanner", _ => { })) yield return x;
        var planet = _game.Zone.Planet;
        float range = _game.PlaceScanRange;
        var sites = _game.PlacesInScan(Vector3.Zero, float.MaxValue, 0).Select(p => p.At with { Y = 0f }).ToList();
        Vector3 best = sites.OrderByDescending(a => sites.Count(b => planet.NearestCopy(b, a).DistanceTo(a) <= range * 0.8f)).First();
        Vector3 stand = best + new Vector3(350f, 0f, 0f); // beyond a place's reveal, so the marks are the scan's, not found places'
        _game.Survivor.PlaceOutside(new System.Numerics.Vector3(stand.X, _game.Zone.GroundAt(stand.X, stand.Z).Y + 1f, stand.Z));
        foreach (int x in _drive.UntilSettled()) yield return x;
        // The sun goes down as you go east: find the hour here with the sun well up.
        double day = 0, high = -2;
        for (double t = 0; t < 1; t += 0.02)
        {
            _game.Zone.Sky.TimeOfDay = t;
            yield return 0;
            if (_game.Zone.Sky.ToSun.Y > high) (high, day) = (_game.Zone.Sky.ToSun.Y, t);
        }
        _game.Zone.Sky.TimeOfDay = day - 0.08;
        foreach (int x in _drive.Wait(60)) yield return x;
        _game.Scan();
        foreach (int x in _drive.Wait(2)) yield return x;
        Vector3 eye = _game.Survivor.WorldEye(), sum = Vector3.Zero;
        foreach (Visor.Marker m in _game.Visor.Markers().Where(m => m.Tag == ""))
            sum += new Vector3(m.At.X - eye.X, 0f, m.At.Z - eye.Z).Normalized();
        Vector3 look = sum.LengthSquared() > 0.01f ? sum.Normalized() : Vector3.Forward;
        foreach (int x in _drive.LookAt(eye + look * 60f + Vector3.Up * 2f, 12)) yield return x;
        _game.Controls.Mouse = new Vector2(-100, -100);
        foreach (int x in _drive.Wait(20)) yield return x;
        foreach (var p in _game.Visor.LayoutMarkers(_game.GetViewport().GetVisibleRect().Size, _game.Visor.UiScale))
            GD.Print($"marker {p.Mark.Kind} {p.Mark.Tag} at {p.At} {(p.Pinned ? "pinned" : "")} {(p.Labelled ? "labelled" : "icon")} {Visor.Distance(p.Distance)}");
        foreach (int x in Shot(_args.GetValueOrDefault("out", "user://placescan.png"))) yield return x;
    }
}
