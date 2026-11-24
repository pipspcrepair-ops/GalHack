using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using GalHack.Physics.Engine;
using Godot;
using PlanetSurface.Poi;
using PlanetSurface.Terrain;

namespace Slice.Tests;

/// <summary>
/// -- --standing [--city]: how soon places stand, from the frame the world first shows. Every place should stand as
/// it is drawn at its distance: within the structures' reach as its structure, within its merged blocks' reach as
/// those (dissolved in), past that as its far mesh, and nowhere a plain block. Timed out to 300 m, 1 km and the far
/// plane; then a flight at a craft's top speed in air straight into a city (or the largest place) from 5 km, which
/// says how far off each place there stood. The worst frame of each and the load to standing are printed with them.
/// </summary>
public partial class PlacePerf
{
    Stopwatch _loading;

    /// <summary>A craft's top speed in air, m/s (ship handling's lightest band).</summary>
    const float FlySpeed = 70f;

    IEnumerable<int> Standing()
    {
        foreach (int x in _drive.UntilPlaying(300)) yield return x;
        // (UntilPlaying waits ten ticks past the world showing: the clock below starts at that frame.)
        double load = _loading.Elapsed.TotalSeconds;
        var zone = _game.Zone;
        var places = _game.Places;
        zone.Weather.Bridge.Calm = true;
        zone.Sky.TimeOfDay = 0.4;
        zone.Weather.Sim.Snap(Weather.Model.WeatherKind.Clear);
        zone.Wildlife.Frozen = true;
        PoiBuilds builds = zone.Pois.Builds;
        bool old = PlanetSurface.World.PlanetWorld.BuildPlacesAtLoad;
        Vector3 eye = _game.Survivor.WorldFeet;
        float[] rings = { 300f, 1000f, builds.Range };
        var at = new double[rings.Length];
        var atShow = rings.Select(r => Unstood(r, eye)).ToArray();
        Array.Fill(at, -1);
        var since = Stopwatch.StartNew();
        Begin();
        int gc = GC.CollectionCount(0);
        while (at.Any(a => a < 0) && since.Elapsed.TotalSeconds < 90)
        {
            for (int i = 0; i < rings.Length; i++)
                if (at[i] < 0 && Unstood(rings[i], eye) == 0) at[i] = since.Elapsed.TotalSeconds;
            yield return 0;
        }
        string T(double s) => s < 0 ? "never" : s.ToString("F1", CultureInfo.InvariantCulture) + " s";
        GD.Print($"PERF standing {(old ? "(places built at load)" : "")}: load {load:F1} s to the world showing; {zone.Planet.Pois.Count} places ({zone.Planet.Pois.Count(s => s.Siting == "city")} in cities); " +
                 $"all standing within 300 m {T(at[0])}, 1 km {T(at[1])}, {rings[2]:F0} m {T(at[2])}; at the world showing {atShow[0]}/{atShow[1]}/{atShow[2]} not standing; " +
                 $"{builds.Built} built by the builder (building {builds.BusyMs / 1000:F1} s, held {builds.HeldMs / 1000:F1} s; all in range at {builds.AllInRangeMs / 1000:F1} s from its start); " +
                 $"{places.BuildLog.Count} structures stood (median {places.BuildLog.Select(b => b.Seconds).DefaultIfEmpty().OrderBy(v => v).ElementAt(places.BuildLog.Count / 2):F1} s each); {GC.CollectionCount(0) - gc} gen0 collections");
        End(old ? "standing (old)" : "standing");

        // The flight in: the nearest city's middle (or the largest place), from 5 km, at the craft's top speed, 60 m up.
        PoiSite goal = zone.Planet.Pois.Where(s => s.Siting == "city").OrderBy(s => places.DistanceTo(s, eye)).FirstOrDefault()
                       ?? zone.Planet.Pois.OrderByDescending(s => (s.Footprint.Max.X - s.Footprint.Min.X) * (s.Footprint.Max.Z - s.Footprint.Min.Z)).First();
        Vector3 Mid(PoiSite s) => zone.Planet.NearestCopy(new Vector3((s.Footprint.Min.X + s.Footprint.Max.X) * 0.5f, 0f, (s.Footprint.Min.Z + s.Footprint.Max.Z) * 0.5f), _game.Survivor.WorldFeet);
        Vector3 target = Mid(goal);
        Vector3 back = new Vector3(eye.X - target.X, 0f, eye.Z - target.Z);
        back = back.LengthSquared() > 1f ? back.Normalized() : Vector3.Right;
        Vector3 Up(Vector3 p) => p with { Y = zone.GroundAt(p.X, p.Z).Y + 60f };
        Vector3 from = Up(target + back * 5000f);
        _game.Survivor.PlaceOutside(from.ToNumerics());
        foreach (int x in _drive.Wait(60)) yield return x;
        foreach (int x in _drive.UntilSettled(30)) yield return x;
        foreach (int x in _drive.Wait(180)) yield return x;
        // The places there: for each, how it stood when the craft first came within 500 m of it, and how far off it first stood.
        var near = zone.Planet.Pois.Where(s => places.DistanceTo(s, target) < 1200f).ToList();
        var merged = new Dictionary<PoiSite, float>();
        var structure = new Dictionary<PoiSite, float>();
        var at500 = new Dictionary<PoiSite, int>();
        Begin();
        float length = new Vector2(target.X - from.X, target.Z - from.Z).Length(), along = 0f;
        while (along < length)
        {
            along += FlySpeed / Engine.PhysicsTicksPerSecond;
            Vector3 p = Up(from.Lerp(target with { Y = from.Y }, along / length));
            _game.Survivor.PlaceOutside(p.ToNumerics());
            _drive.Face(target with { Y = p.Y - 20f });
            foreach (PoiSite s in near)
            {
                float d = places.DistanceTo(s, p);
                if (!merged.ContainsKey(s) && zone.Pois.Shows(s) == 2) merged[s] = d;
                if (!structure.ContainsKey(s) && places.PlaceOf(s) is { Ready: true }) structure[s] = d;
                if (!at500.ContainsKey(s) && d < 500f) at500[s] = structure.ContainsKey(s) ? 3 : zone.Pois.Shows(s);
            }
            yield return 0;
        }
        _drive.Controls.Look = Vector2.Zero;
        End(old ? "fly-in (old)" : "fly-in");
        string Dist(Dictionary<PoiSite, float> got)
        {
            if (got.Count == 0) return "none";
            var d = got.Values.OrderBy(v => v).ToList();
            return string.Create(CultureInfo.InvariantCulture, $"{got.Count} of {near.Count}, nearest {d[0]:F0} m, median {d[d.Count / 2]:F0} m, furthest {d[^1]:F0} m");
        }
        int[] by = Enumerable.Range(0, 4).Select(k => at500.Values.Count(v => v == k)).ToArray();
        GD.Print($"PERF fly-in {(old ? "(places built at load)" : "")} to {goal.Family.Id} ({(goal.Siting == "city" ? "a city" : "the largest place")}) from 5 km at {FlySpeed:F0} m/s: " +
                 $"merged blocks in at {Dist(merged)}; structures stood at {Dist(structure)}; " +
                 $"at 500 m: {by[0]} plain or missing, {by[1]} far mesh, {by[2]} merged blocks, {by[3]} structures ({at500.Count} came within 500 m)");
    }

    /// <summary>Places within <paramref name="range"/> of a point not standing as they are drawn at their distance.</summary>
    int Unstood(float range, Vector3 from)
    {
        var zone = _game.Zone;
        var places = _game.Places;
        float stand = places.BuildRadius * DetailReach.StandScale;
        float mid = DetailReach.Toward(zone.Pois.MidRadius), city = DetailReach.Toward(zone.Pois.CityMidRadius);
        int count = 0;
        foreach (PoiSite s in zone.Planet.Pois)
        {
            float d = places.DistanceTo(s, from);
            if (d >= range) continue;
            bool ok = d < stand ? places.PlaceOf(s) is { Ready: true }
                : d < (s.Siting == "city" ? city : mid) ? zone.Pois.Shows(s) == 2
                : zone.Pois.Shows(s) >= 1;
            if (!ok) count++;
        }
        return count;
    }
}
