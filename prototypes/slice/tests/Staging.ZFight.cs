using System;
using System.Collections.Generic;
using System.Linq;
using GalHack.Physics.Engine;
using GalHack.Testing;
using GalHack.Geometry;
using Godot;
using PlanetSurface.Poi;
using PoiGenerator.Gen;
using Slice.Places;
using WeatherKind = Weather.Model.WeatherKind;

namespace Slice.Tests;

/// <summary>
/// Z-fighting in the drawn game (--view=zfight; Screenshot forces a city and a capital hulk): the castaway stands
/// at the start's place, a city lot, a flavour ruin, the hulk, places of other families and beside the hover bike,
/// and at each every drawn opaque face within --radius metres (30) goes through <see cref="Coplanar"/>. The pairs
/// are printed by source, worst first ("ZFIGHT" lines), and the worst --shots (3) pairs over all stops are framed
/// from a metre and a half by day into --dir, led by --tag.
/// </summary>
public sealed partial class Staging
{
    IEnumerable<int> ZFightView()
    {
        _game.Zone.Sky.TimeOfDay = Num("time", 0.4);
        _game.Zone.Weather.Sim.Snap(WeatherKind.Clear);
        _game.Visor.Visible = false;
        string dir = _args.GetValueOrDefault("dir", "."), tag = _args.GetValueOrDefault("tag", "zf");
        System.IO.Directory.CreateDirectory(dir);
        float radius = (float)Num("radius", 30);
        var totals = new Dictionary<string, int>();
        var worst = new List<(string Source, int Count, Vector3 At, Vector3 Normal, string Stop)>();
        int all = 0;

        void Measure(string stop)
        {
            Vector3 eye = _game.GetViewport().GetCamera3D()?.GlobalPosition ?? _game.Survivor.WorldFeet;
            var scene = new CoplanarScene();
            scene.Gather(_game, eye, eye, radius);
            List<Coplanar.Pair> pairs = Coplanar.Find(scene.Faces);
            var by = Coplanar.BySource(scene.Faces, pairs, s => scene.Sources[s]);
            all += pairs.Count;
            GD.Print($"ZFIGHT {stop}: {scene.Faces.Count} faces, {pairs.Count} pairs");
            foreach (var (src, n) in by.Take(8))
            {
                GD.Print($"ZFIGHT   {n,6}  {src}");
                totals[src] = totals.GetValueOrDefault(src) + n;
            }
            foreach (var (src, n) in by.Take(3))
            {
                Coplanar.Pair p = pairs.Where(q => PairSources(scene, q) == src).MaxBy(q => q.Area);
                Coplanar.Face f = scene.Faces[p.P];
                var mid = (f.A + f.B + f.C) / 3f;
                var nrm = System.Numerics.Vector3.Normalize(System.Numerics.Vector3.Cross(f.B - f.A, f.C - f.A));
                if (System.Numerics.Vector3.Dot(nrm, f.Front) < 0) nrm = -nrm;
                worst.Add((src, n, eye + new Vector3(mid.X, mid.Y, mid.Z), new Vector3(nrm.X, nrm.Y, nrm.Z), stop));
                GD.Print($"ZFIGHT   e.g. {src}: {p.Area * 1e4f:F1} cm2, {p.Gap * 1000f:F2} mm apart at {eye + new Vector3(mid.X, mid.Y, mid.Z)}");
            }
        }

        // The start's own place, from inside its fullest room.
        PoiSite home = Named();
        foreach (int x in GoTo(home, Gate(home) + Vector3.Up * 0.1f)) yield return x;
        if (_game.Places.PlaceOf(home) is Place hp && hp.Memory.Rooms?.Count > 0)
        {
            var (_, eye, look) = RoomView(hp);
            _game.Survivor.PlaceOutside((eye + Vector3.Down * 1.55f).ToNumerics());
            foreach (int x in _drive.Wait(60)) yield return x;
            foreach (int x in _drive.LookAt(look, 10)) yield return x;
        }
        Measure($"start {home.Family.Id} {home.Instance?.State}");

        // Places: a city lot, the hulk, then the nearest of other families, one each, in whatever state they rolled.
        var planet = _game.Zone.Planet;
        Vector3 from = _game.Survivor.WorldFeet;
        var stops = new List<PoiSite>();
        if (planet.Infra?.City is { Sites.Count: > 0 } city) stops.Add(city.Sites.OrderBy(s => _game.Places.DistanceTo(s, from)).First());
        PoiSite hulk = planet.Pois.FirstOrDefault(s => s.Family.Id == "crashed_freighter" && s.Instance != null);
        if (hulk != null) stops.Add(hulk);
        foreach (var g in planet.Pois.Where(s => s.Instance != null && s != home && !stops.Contains(s)).GroupBy(s => s.Family.Id)
                     .Select(g => g.OrderBy(s => _game.Places.DistanceTo(s, from)).First()).OrderBy(s => _game.Places.DistanceTo(s, from)).Take((int)Num("families", 5)))
            stops.Add(g);
        foreach (PoiSite site in stops)
        {
            foreach (int x in GoTo(site, Gate(site) + Vector3.Up * 0.1f)) yield return x;
            if (_game.Places.PlaceOf(site) is Place p && p.Memory.Rooms?.Count > 0)
            {
                var (_, eye, look) = RoomView(p);
                _game.Survivor.PlaceOutside((eye + Vector3.Down * 1.55f).ToNumerics());
                foreach (int x in _drive.Wait(60)) yield return x;
                foreach (int x in _drive.LookAt(look, 10)) yield return x;
            }
            Measure($"{site.Family.Id} {site.Instance.State}");
        }

        // A flavour ruin, from its middle.
        var ruin = planet.Infra?.Ruins.OrderBy(r => planet.NearestCopy(new Vector3((float)r.Center.X, from.Y, (float)r.Center.Z), from).DistanceTo(from)).FirstOrDefault();
        if (ruin != null)
        {
            Vector3 c = planet.NearestCopy(new Vector3((float)ruin.Center.X, ruin.OriginY + 0.5f, (float)ruin.Center.Z), from);
            _game.Survivor.PlaceOutside(c.ToNumerics());
            foreach (int x in _drive.UntilSettled()) yield return x;
            foreach (int x in _drive.Wait(240)) yield return x;
            Measure($"ruin {ruin.Layout.Name} {ruin.State}");
        }

        // The hover bike, set down beside the castaway.
        GiveBike();
        foreach (int x in Play.Select(1)) yield return x;
        foreach (int x in _drive.LookAt(_game.Survivor.WorldFeet + new Vector3(0f, -1f, 0f) + Flat(_game.Survivor.WorldLook()) * 2.5f, 6)) yield return x;
        foreach (int x in _drive.Click()) yield return x;
        foreach (int x in _drive.Wait(30)) yield return x;
        Measure("hover bike");

        GD.Print($"ZFIGHT TOTAL {all} pairs");
        foreach (var kv in totals.OrderByDescending(k => k.Value).Take(15)) GD.Print($"ZFIGHT TOTAL {kv.Value,6}  {kv.Key}");

        // The worst cases over all stops, one per source, each framed from a metre and a half off its face.
        int shots = (int)Num("shots", 3), taken = 0;
        foreach (var w in worst.GroupBy(w => w.Source).Select(g => g.MaxBy(w => w.Count)).OrderByDescending(w => w.Count))
        {
            if (taken++ >= shots) break;
            Vector3 eye = w.At + (w.Normal + Vector3.Up * 0.6f).Normalized() * 1.5f;
            _game.Survivor.PlaceOutside((eye + Vector3.Down * 1.6f).ToNumerics());
            foreach (int x in _drive.Wait(90)) yield return x;
            for (int i = 0; i < 20; i++) { _game.FixedCamera = new Transform3D(Basis.LookingAt(w.At - eye, MathF.Abs(w.Normal.Y) > 0.9f ? Vector3.Forward : Vector3.Up), eye); yield return 0; }
            GD.Print($"ZFIGHT shot {taken}: {w.Source} at {w.Stop}, {w.At}");
            foreach (int x in Shot(System.IO.Path.Combine(dir, $"{tag}-{taken}.png"))) yield return x;
            _game.FixedCamera = null;
        }
    }

    static string PairSources(CoplanarScene scene, Coplanar.Pair p)
    {
        string a = scene.Sources[scene.Faces[p.P].Source], b = scene.Sources[scene.Faces[p.Q].Source];
        return string.CompareOrdinal(a, b) <= 0 ? (a == b ? a : $"{a} / {b}") : $"{b} / {a}";
    }
}
