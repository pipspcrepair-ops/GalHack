using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using GalHack.Physics.Engine;
using Godot;
using Planets.Gen;
using PlanetSurface.Poi;

namespace Slice.Tests;

/// <summary>
/// The performance audit (--audit=1): the same measures in each of the scenes the frame budget is held in,
/// on a fresh world with a city on it. Open country looking at the horizon; a town, from its edge; a city's
/// middle at eye height looking along its street of the tallest towers; and the same city from over its edge.
/// Each scene is reached by a jump there, and how long each kind of thing takes to come in after the jump
/// is printed (ground, places' merged blocks, places stood up, people), then its frame (PERF line), what is in
/// the tree by class, the lights, and (--profile=1, in the city) what each kind of node costs the main thread.
/// </summary>
public partial class PlacePerf
{
    IEnumerable<int> Audit()
    {
        var zone = _game.Zone;
        var world = zone.Planet;
        var city = world.Infra?.City is { Sites.Count: > 0 } c ? c : null;
        GD.Print($"PERF audit on {_game.World}: {world.Pois.Count} places, city {(city != null ? $"{city.Sites.Count} buildings, radius {city.Radius:F0} m" : $"none ({world.Infra?.City?.Why})")}");
        Vector3 start = _game.Survivor.WorldFeet;
        var rng = new Random();

        // Open country: the spot round the start furthest from any place, looking at the horizon.
        Vector3 country = start;
        float far = -1f;
        for (int i = 0; i < 200; i++)
        {
            float a = (float)(rng.NextDouble() * Math.Tau), r = 400f + (float)rng.NextDouble() * 3000f;
            Vector3 p = start + new Vector3(MathF.Cos(a), 0f, MathF.Sin(a)) * r;
            if (Ground(p) < 1f) continue;
            float near = world.Pois.Select(s => _game.Places.DistanceTo(s, p)).DefaultIfEmpty(9999f).Min();
            if (near > far) (far, country) = (near, p);
        }
        string only = _args.GetValueOrDefault("scenes", "country,town,city,air");
        GD.Print($"PERF country spot {far:F0} m from the nearest place");
        if (only.Contains("country")) foreach (int x in Scene("country", country, country + new Vector3(500f, 0f, 120f), 1.7f)) yield return x;

        // A town: a place with two others within 400 m, from 30 m off its edge, looking at its middle.
        var places = world.Pois.Where(s => s.Instance != null && s.Siting != "city").ToList();
        PoiSite town = places.Where(s => places.Count(o => o != s && Flat(o.Origin, s.Origin) < 400f) >= 2).OrderBy(_ => rng.Next()).FirstOrDefault() ?? places.OrderBy(_ => rng.Next()).FirstOrDefault();
        if (town != null && only.Contains("town"))
        {
            Vector3 mid = Middle(town);
            Vector3 way = new Vector3(MathF.Cos(rng.NextSingle() * MathF.Tau), 0f, MathF.Sin(rng.NextSingle() * MathF.Tau));
            float half = MathF.Max(town.Footprint.Max.X - town.Footprint.Min.X, town.Footprint.Max.Z - town.Footprint.Min.Z) * 0.5f;
            GD.Print($"PERF town {town.Name} ({town.Family.Id}), {places.Count(o => o != town && Flat(o.Origin, town.Origin) < 400f)} more places within 400 m");
            foreach (int x in Scene("town", mid + way * (half + 30f), mid + Vector3.Up * 6f, 1.7f)) yield return x;
        }
        else GD.Print("PERF town: none on this world");

        if (city == null || !only.Contains("city")) yield break;
        // The city's middle: the street point within half its radius with the most tower along it, looking along it.
        Vector3 centre = world.NearestCopy(new Vector3((float)city.CenterX, 0f, (float)city.CenterZ), start);
        var towers = city.Sites.Select(s => (At: Middle(s), Tall: (float)s.Instance.Blocks.Size.Y)).ToList();
        (Vector3 At, Vector3 Way, float Score) best = (centre, Vector3.Forward, -1f);
        foreach (var street in city.Streets)
            for (int i = 0; i + 1 < street.Count; i++)
            {
                Vector3 p0 = world.NearestCopy(new Vector3((float)street.X[i], 0f, (float)street.Z[i]), centre);
                Vector3 p1 = p0 + new Vector3((float)(street.X[i + 1] - street.X[i]), 0f, (float)(street.Z[i + 1] - street.Z[i]));
                float span = Flat(p0, p1);
                if (span < 1f) continue;
                for (float s = 0f; s < span; s += 16f)
                foreach (Vector3 dir in new[] { (p1 - p0).Normalized(), (p0 - p1).Normalized() })
                {
                    Vector3 a = p0 + (p1 - p0) * (s / span);
                    if (Flat(a, centre) > city.Radius * 0.5f) continue;
                    float score = 0f;
                    foreach (var t in towers)
                    {
                        Vector3 d = t.At - a;
                        float along = d.X * dir.X + d.Z * dir.Z, side = MathF.Abs(d.X * dir.Z - d.Z * dir.X);
                        if (along > 10f && along < 300f && side < 70f) score += t.Tall;
                    }
                    if (score > best.Score) best = (a, dir, score);
                }
            }
        GD.Print($"PERF city street at {best.At.X:F0},{best.At.Z:F0}: {best.Score:F0} m of tower along 300 m of it, the tallest in the city {towers.Max(t => t.Tall):F0} m");
        foreach (int x in Scene("city street", best.At, best.At + best.Way * 200f + Vector3.Up * 30f, 1.7f, _args.ContainsKey("profile"))) yield return x;
        // Over its edge, looking in across it.
        if (!only.Contains("air")) yield break;
        Vector3 edge = centre + (start - centre).Normalized() * (float)city.Radius;
        foreach (int x in Scene("city air", edge, centre + Vector3.Up * 20f, 120f)) yield return x;
    }

    static float Flat(TerrainDigging.Terrain.Int3 a, TerrainDigging.Terrain.Int3 b) => MathF.Sqrt((float)(a.X - b.X) * (a.X - b.X) + (float)(a.Z - b.Z) * (a.Z - b.Z));
    static float Flat(Vector3 a, Vector3 b) => new Vector2(a.X - b.X, a.Z - b.Z).Length();

    float Ground(Vector3 p)
    {
        var w = _game.Zone.Planet;
        return w.GroundHeight(Mathx.WrapX(p.X, w.Width), Mathx.Wrap(p.Z, w.Height));
    }

    Vector3 Middle(PoiSite s)
    {
        var lo = s.Footprint.Min;
        var hi = s.Footprint.Max;
        Vector3 m = _game.Zone.Planet.NearestCopy(new Vector3((lo.X + hi.X + 1) * 0.5f, 0f, (lo.Z + hi.Z + 1) * 0.5f), _game.Survivor.WorldFeet);
        m.Y = Ground(m);
        return m;
    }

    /// <summary>
    /// One scene: jumps there, looking at a point from a height over the ground; prints how long each kind took to
    /// come in, then the frame and the tree.
    /// </summary>
    IEnumerable<int> Scene(string name, Vector3 at, Vector3 look, float height, bool profile = false)
    {
        var zone = _game.Zone;
        at.Y = MathF.Max(Ground(at), 0f);
        _game.Survivor.PlaceOutside(new Vector3(at.X, at.Y + (height > 3f ? height : 0.1f), at.Z).ToNumerics());
        Vector3 eye = at + Vector3.Up * height;
        look.Y = MathF.Max(look.Y, Ground(look) + 2f);
        _game.FixedCamera = new Transform3D(Basis.LookingAt(look - eye, Vector3.Up), eye);
        // How long each kind of thing takes to come in after the jump.
        var clock = Stopwatch.StartNew();
        double ground = -1, merged = -1, stood = -1;
        int quiet = 0;
        while (clock.Elapsed.TotalSeconds < 90)
        {
            yield return 0;
            double t = clock.Elapsed.TotalSeconds;
            if (ground < 0 && zone.Streamer.Settled) ground = t;
            if (merged < 0 && t > 1 && !zone.Pois.MidPending) merged = t;
            quiet = _game.Places.Idle ? quiet + 1 : 0;
            if (stood < 0 && quiet > 60) stood = t - 1;
            if (ground >= 0 && merged >= 0 && stood >= 0) break;
        }
        var log = _game.Places.BuildLog;
        GD.Print(string.Create(CultureInfo.InvariantCulture, $"PERF load {name,-12} ground {ground,5:F1} s, places' merged blocks {merged,5:F1} s, places stood up {stood,5:F1} s ({_game.Places.Standing.Count} standing; each took {(log.Count > 0 ? $"{log.Average(b => b.Seconds):F1} s on average, at worst {log.Max(b => b.Seconds):F1} s" : "-")}); worst build step {_game.Places.WorstStepMs:F1} ms ({string.Join(", ", _game.Places.StageWorst.OrderByDescending(k => k.Value).Take(3).Select(k => $"{k.Key} {k.Value:F1}"))})"));
        log.Clear();
        _game.Places.ResetWorst();
        _game.Places.StageWorst.Clear();
        foreach (int x in _drive.Wait(60)) yield return x;
        foreach (int x in Measure(name)) yield return x;
        Census(_game);
        Lights();
        // --toggle=occlusion,loadbudget: the same scene with a fix switched off, then on again (the order evens out what still loads).
        foreach (string ab in _args.GetValueOrDefault("toggle", "").Split(',', StringSplitOptions.RemoveEmptyEntries))
        {
            Action<bool> set = ab switch
            {
                "occlusion" => on => GetViewport().UseOcclusionCulling = on,
                _ => null,
            };
            if (set == null) continue;
            foreach (bool on in new[] { false, true })
            {
                set(on);
                foreach (int x in _drive.Wait(10)) yield return x;
                foreach (int x in Measure($"{name} {ab} {(on ? "on" : "off")}", 120)) yield return x;
            }
        }
        if (!profile) yield break;
        var profiler = new ProcessProfiler(_probe, GetTree().Root, 40, this);
        while (!profiler.Done)
        {
            profiler.Step();
            yield return 0;
        }
        profiler.Restore();
        foreach (string line in profiler.Report().Take(16)) GD.Print($"PERF profile {line}");
    }

    /// <summary>What lights the scene: lights by kind, how many cast shadows, and the sun's shadow reach and splits.</summary>
    void Lights()
    {
        int omni = 0, spot = 0, shadowed = 0, visible = 0;
        string sun = "";
        void Walk(Node n)
        {
            if (n is Light3D l && l.IsVisibleInTree())
            {
                visible++;
                if (l is OmniLight3D) omni++;
                else if (l is SpotLight3D) spot++;
                else if (l is DirectionalLight3D d && l.ShadowEnabled) sun = $"sun shadow {d.DirectionalShadowMode} to {d.DirectionalShadowMaxDistance:F0} m";
                if (l.ShadowEnabled && l is not DirectionalLight3D) shadowed++;
            }
            foreach (Node c in n.GetChildren()) Walk(c);
        }
        Walk(GetTree().Root);
        GD.Print($"PERF lights {visible} visible ({omni} omni, {spot} spot), {shadowed} of them with shadows; {sun}");
    }
}
