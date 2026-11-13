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
        if (_args.TryGetValue("occlcheck", out string shots))
        {
            // Nothing wrongly hidden: the street four ways round (facades, windows), then inside a standing place looking out at its way in.
            Vector3 eye = best.At + Vector3.Up * (Ground(best.At) - best.At.Y + 1.7f);
            for (int k = 0; k < 4; k++)
                foreach (int x in OcclusionCheck(shots, $"street{k}", new Transform3D(Basis.LookingAt(best.Way.Rotated(Vector3.Up, k * MathF.PI / 2f) + Vector3.Up * 0.1f, Vector3.Up), eye))) yield return x;
            var inside = _game.Places.Standing.OrderBy(p => Flat(p.WorldOrigin, best.At)).Select(p =>
            {
                var reach = p.Nav().Reachable(p.Inst.Layout.EntranceCell);
                var roofed = reach.Where(c => p.Roofed(p.WorldOf(c, 0.2f))).ToList();
                return (Place: p, Cell: roofed.Count > 0 ? roofed[roofed.Count / 2] : (PoiGenerator.Gen.Int3?)null);
            }).FirstOrDefault(t => t.Cell != null);
            if (inside.Place != null)
            {
                Vector3 at = inside.Place.WorldOf(inside.Cell.Value, 1.6f), door = inside.Place.WorldOf(inside.Place.Inst.Layout.EntranceCell, 1.2f);
                _game.Survivor.PlaceOutside(inside.Place.WorldOf(inside.Cell.Value, 0.1f).ToNumerics());
                foreach (int x in OcclusionCheck(shots, "inside", new Transform3D(Basis.LookingAt(door - at, Vector3.Up), at))) yield return x;
            }
            // From outside: through a ground-floor window and through the way in, into the rooms behind.
            var near = _game.Places.Standing.OrderBy(p => Flat(p.WorldOrigin, best.At)).ToList();
            foreach (var p in near)
            {
                var g = p.Inst.Blocks;
                var window = g.Blocks().Where(cb => cb.Item2.Role == PoiGenerator.Gen.Role.Window && cb.Item1.Y >= 0 && cb.Item1.Y <= 3)
                    .SelectMany(cb => PoiGenerator.Gen.Dirs.Horizontal.Select(d => (Cell: cb.Item1, Out: PoiGenerator.Gen.Dirs.Vec(d))))
                    .FirstOrDefault(w => g.Get(w.Cell + w.Out).IsAir && !p.Inst.Layout.Occupied[w.Cell + w.Out]);
                if (window.Out == default) continue;
                Vector3 pane = p.WorldOf(window.Cell), from = pane + new Vector3(window.Out.X, 0f, window.Out.Z) * 4f;
                foreach (int x in OcclusionCheck(shots, "window", new Transform3D(Basis.LookingAt(pane - from, Vector3.Up), from))) yield return x;
                break;
            }
            if (near.Count > 0)
            {
                var p = near[0];
                var f = PoiGenerator.Gen.Dirs.Vec(p.Inst.Layout.EntranceFacing);
                Vector3 gate = p.WorldOf(p.Inst.Layout.EntranceCell, 1.2f), from = gate + new Vector3(f.X, 0f, f.Z) * 5f;
                foreach (int x in OcclusionCheck(shots, "doorway", new Transform3D(Basis.LookingAt(gate - from, Vector3.Up), from))) yield return x;
            }
            // A half-ruined building: its holes in the walls are the risk.
            var ruin = world.Pois.Where(s => s.Instance?.State == PoiGenerator.Gen.PoiState.Ruined).OrderBy(s => Flat(Middle(s), best.At)).FirstOrDefault();
            if (ruin != null)
            {
                Vector3 mid = Middle(ruin), way = (best.At - mid).Normalized();
                float half = MathF.Max(ruin.Footprint.Max.X - ruin.Footprint.Min.X, ruin.Footprint.Max.Z - ruin.Footprint.Min.Z) * 0.5f;
                Vector3 from = mid + way * (half + 20f);
                from.Y = Ground(from) + 1.7f;
                _game.Survivor.PlaceOutside((from - Vector3.Up * 1.6f).ToNumerics());
                foreach (int x in _drive.Wait(300)) yield return x; // it stands up near
                GD.Print($"PERF occlusion check ruin: {ruin.Name}, {Flat(mid, best.At):F0} m from the street");
                foreach (int x in OcclusionCheck(shots, "ruin", new Transform3D(Basis.LookingAt(mid + Vector3.Up * 6f - from, Vector3.Up), from))) yield return x;
            }
        }
        // Over its edge, looking in across it.
        if (!only.Contains("air")) yield break;
        Vector3 edge = centre + (start - centre).Normalized() * (float)city.Radius;
        foreach (int x in Scene("city air", edge, centre + Vector3.Up * 20f, 120f)) yield return x;
    }

    /// <summary>
    /// One view drawn with the occluders off, off again (what moves between frames anyway) and on: the share of
    /// pixels that differ each way, and the frames and a map of the differences (red: off and on differ) beside --occlcheck.
    /// </summary>
    IEnumerable<int> OcclusionCheck(string dir, string tag, Transform3D view)
    {
        System.IO.Directory.CreateDirectory(dir);
        _game.FixedCamera = view;
        _game.Zone.Wildlife.Frozen = true;
        _game.Visor.Visible = false;
        // Noon where the eye is, in clear weather: a wall wrongly hidden must show, not be black on black.
        var sky = _game.Zone.Sky;
        double noon = 0.5;
        float top = -2f;
        for (int k = 0; k < 240; k++)
            if (sky.SunAt(k / 240.0).Y is float y && y > top) (top, noon) = (y, k / 240.0);
        _game.Zone.Weather.Bridge.Calm = true;
        _game.Zone.Weather.Sim.Snap(Weather.Model.WeatherKind.Clear);
        for (int i = 0; i < 60; i++)
        {
            sky.TimeOfDay = noon;
            yield return 0;
        }
        Image Shot() { sky.TimeOfDay = noon; return GetViewport().GetTexture().GetImage(); }
        GetViewport().UseOcclusionCulling = false;
        foreach (int x in _drive.Wait(10)) yield return x;
        Image off1 = Shot();
        foreach (int x in _drive.Wait(10)) yield return x;
        Image off2 = Shot();
        GetViewport().UseOcclusionCulling = true;
        foreach (int x in _drive.Wait(10)) yield return x;
        Image on = Shot();
        var map = Image.CreateEmpty(on.GetWidth(), on.GetHeight(), false, Image.Format.Rgb8);
        int noise = 0, differ = 0, n = on.GetWidth() * on.GetHeight();
        for (int y = 0; y < on.GetHeight(); y++)
            for (int x = 0; x < on.GetWidth(); x++)
            {
                Color a = off1.GetPixel(x, y), b = off2.GetPixel(x, y), c = on.GetPixel(x, y);
                bool moved = Differ(a, b), changed = Differ(b, c);
                if (moved) noise++;
                if (changed) differ++;
                map.SetPixel(x, y, changed && !moved ? new Color(1f, 0f, 0f) : c.Darkened(0.6f));
            }
        off2.SavePng(System.IO.Path.Combine(dir, $"{tag}-off.png"));
        on.SavePng(System.IO.Path.Combine(dir, $"{tag}-on.png"));
        map.SavePng(System.IO.Path.Combine(dir, $"{tag}-diff.png"));
        GD.Print($"PERF occlusion check {tag}: {differ * 100.0 / n:F3}% of pixels differ off against on, {noise * 100.0 / n:F3}% off against off; draws on {GetViewport().GetRenderInfo(Viewport.RenderInfoType.Visible, Viewport.RenderInfo.DrawCallsInFrame)}");
        _game.Zone.Wildlife.Frozen = false;
        static bool Differ(Color a, Color b) => MathF.Max(MathF.Abs(a.R - b.R), MathF.Max(MathF.Abs(a.G - b.G), MathF.Abs(a.B - b.B))) > 0.06f;
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
        // --ticks=8,3,2: the same scene with each cap on physics ticks a frame (the engine's max_physics_steps_per_frame).
        int cap = Engine.MaxPhysicsStepsPerFrame;
        foreach (string n in _args.GetValueOrDefault("ticks", "").Split(',', StringSplitOptions.RemoveEmptyEntries))
        {
            Engine.MaxPhysicsStepsPerFrame = int.Parse(n);
            foreach (int x in _drive.Wait(10)) yield return x;
            foreach (int x in Measure($"{name} at most {n} ticks", 120)) yield return x;
        }
        Engine.MaxPhysicsStepsPerFrame = cap;
        // --toggle=occlusion,loadbudget: the same scene with a fix switched off, then on again (the order evens out what still loads).
        foreach (string ab in _args.GetValueOrDefault("toggle", "").Split(',', StringSplitOptions.RemoveEmptyEntries))
        {
            Action<bool> set = ab switch
            {
                "occlusion" => on => GetViewport().UseOcclusionCulling = on,
                "loadbudget" => on => PlanetSurface.Fx.LoadBudget.FrameMs = on ? 4.0 : double.MaxValue,
                "towns" => on => Slice.Places.Town.Spaced = on,
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
