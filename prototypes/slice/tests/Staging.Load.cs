using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using GalHack.Physics;
using Godot;
using PlanetSurface.Poi;

namespace Slice.Tests;

/// <summary>
/// How far ahead the world loads, in real time: --mode=foot, bike or vessel goes flat out for
/// --seconds from place to place at the speed of each (--speed=m/s to set it; a vessel 200 m up),
/// printing what the queues hold and a summary at the end. Run windowed, without --fixed-fps: a frame
/// budget means nothing on a fixed clock. The sleeper is moved along the route by hand, so every run
/// holds its speed through whatever is in the way: all that's measured is what loads, and when.
/// </summary>
public sealed partial class Staging
{
    IEnumerable<int> LoadRun()
    {
        string mode = _args.GetValueOrDefault("mode", "bike");
        double seconds = Num("seconds", 40);
        float speed = (float)Num("speed", mode switch { "foot" => 7, "bike" => 38, _ => 70 }), over = mode == "vessel" ? 200f : 1.2f;
        _game.Zone.Sky.TimeOfDay = 0.42;
        _game.Zone.Weather.Sim.Snap(Weather.Model.WeatherKind.Clear);
        var places = _game.Places;
        var world = _game.Surface.Planet;
        // The route: place to place, each the nearest not yet passed.
        Vector3 at = _game.Survivor.WorldFeet;
        var route = new List<Vector3>();
        var left = world.Pois.Where(s => s.Instance != null).ToList();
        for (Vector3 from = at; route.Count < 24 && left.Count > 0;)
        {
            Vector3 here = from;
            PoiSite next = left.Where(s => places.DistanceTo(s, here) > 300f).OrderBy(s => places.DistanceTo(s, here)).FirstOrDefault();
            if (next == null) break;
            left.Remove(next);
            from = world.NearestCopy(new Vector3(next.Origin.X, 0f, next.Origin.Z), here);
            route.Add(from);
        }
        var stood = new List<(string Name, float Distance, double Seconds)>();
        var clock = Stopwatch.StartNew();
        places.Stood += p => stood.Add((p.Site.Name, places.DistanceTo(p.Site, at), places.BuildLog.LastOrDefault().Seconds));
        Vector3 Focus() => at;

        var s0 = _game.Zone.Streamer;
        var infra = _game.Zone.Infra;
        s0.ResetStats();
        infra.WorstMainMs = infra.WorstStepMs = 0;
        places.ResetWorst();
        infra.TileLog.Clear();
        double nearestInfra = double.MaxValue, nextPrint = 0, worstGap = 0, gapSum = 0, farBusy = 0;
        int nearestFar = int.MaxValue, leastVoxels = int.MaxValue, frames = 0, ticks = (int)(seconds * 60), mostFar = 0, mostTiles = 0;
        long stamp = Stopwatch.GetTimestamp();
        int frameBefore = Engine.GetFramesDrawn();
        for (int tick = 0; tick < ticks && clock.Elapsed.TotalSeconds < seconds * 2; tick++)
        {
            if (route.Count > 0 && (route[0] - at with { Y = 0f }).Length() < 40f) route.RemoveAt(0);
            Vector3 way = route.Count > 0 ? route[0] - at with { Y = 0f } : Vector3.Right;
            at += way.Normalized() * speed / 60f;
            at.Y = world.GroundHeight(Planets.Gen.Mathx.WrapX(at.X, world.Width), Planets.Gen.Mathx.Wrap(at.Z, world.Height)) + over;
            _game.Survivor.PlaceOutside(new System.Numerics.Vector3(at.X, at.Y, at.Z));
            _drive.Face(at + way);
            yield return 0;
            int drawn = Engine.GetFramesDrawn();
            if (drawn == frameBefore) continue;
            frameBefore = drawn;
            double gap = Stopwatch.GetElapsedTime(stamp).TotalMilliseconds;
            stamp = Stopwatch.GetTimestamp();
            worstGap = Math.Max(worstGap, gap);
            gapSum += gap;
            frames++;
            nearestInfra = Math.Min(nearestInfra, infra.NearestBehind);
            nearestFar = Math.Min(nearestFar, s0.FarBehindNearest);
            mostFar = Math.Max(mostFar, s0.FarBehind);
            mostTiles = Math.Max(mostTiles, infra.TilesBehind);
            leastVoxels = Math.Min(leastVoxels, s0.CompleteRadius);
            if (s0.FarBehind > 0) farBusy++;
            if (clock.Elapsed.TotalSeconds >= nextPrint)
            {
                nextPrint += 5;
                GD.Print($"[load {clock.Elapsed.TotalSeconds:F0} s] at {at.X:F0},{at.Y:F0},{at.Z:F0}, voxels to {s0.CompleteRadius}, far behind {s0.FarBehind} (nearest ring {(s0.FarBehindNearest == int.MaxValue ? "-" : s0.FarBehindNearest.ToString())}), tiles behind {infra.TilesBehind} (nearest {infra.NearestBehind:F0} m), places standing {places.Standing.Count}, build {(places.Building ?? "none")}");
            }
        }
        double avgTile = infra.TileLog.Count > 0 ? infra.TileLog.Average(t => t.Seconds) : 0, worstTile = infra.TileLog.Count > 0 ? infra.TileLog.Max(t => t.Seconds) : 0, closest = infra.TileLog.Count > 0 ? infra.TileLog.Min(t => t.Distance) : 0;
        GD.Print($"[load summary {mode}] {_game.World}; {speed:F0} m/s, {frames} frames, mean {gapSum / Math.Max(frames, 1):F1} ms, worst gap {worstGap:F0} ms");
        GD.Print($"[load summary {mode}] voxels least complete radius {leastVoxels}; far: {farBusy / Math.Max(frames, 1):P0} of frames behind, most behind {mostFar}, nearest ring ever behind {(nearestFar == int.MaxValue ? "-" : nearestFar.ToString())}");
        GD.Print($"[load summary {mode}] tiles: {infra.TileLog.Count} done, {avgTile:F2} s average, {worstTile:F2} s worst, nearest unfinished {nearestInfra:F0} m, closest finish {closest:F0} m, most behind {mostTiles}");
        GD.Print($"[load summary {mode}] places stood: {string.Join("; ", stood.Select(s => $"{s.Name} {s.Seconds:F2} s, {s.Distance:F0} m off"))}");
        GD.Print($"[load summary {mode}] main thread worst: streamer {s0.WorstFrameMs:F1} ms ({s0.WorstBreakdown}), tiles {infra.WorstMainMs:F1} ms (longest step {infra.WorstStepMs:F1}: {infra.WorstStep}), place build {places.WorstFrameMs:F1} ms; far builds by level {string.Join(", ", s0.FarBuilds.Select((n, i) => $"{n} at {(n > 0 ? s0.FarBuildMs[i] / n : 0):F0} ms"))}");
    }
}
