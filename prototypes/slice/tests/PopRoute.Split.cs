using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Godot;
using PlanetSurface.Terrain;

namespace Slice.Tests;

/// <summary>
/// --split=1: where each route's main thread goes, frame by frame (<see cref="FrameSplit"/>), by system (<see cref="SystemSplit"/>
/// over the game, the zone, the wildlife and the places), and what the creatures, the streamer, the places and the engine's
/// physics had on their hands meanwhile. --live=1 lets the wildlife go about its business (the routes hold it still otherwise).
/// </summary>
public partial class PopRoute
{
    FrameSplit _frameSplit;
    SystemSplit _systemSplit;
    double[] _sections0;
    int _rays0, _paths0, _gc0, _gc1, _gc2;
    double _physicsObjects, _pairs, _creatures, _stepped, _creaturesDrawn, _siteCreatures, _siteStepped, _siteDrawn, _people, _peopleDrawn, _townMs;
    int _samplesTaken;
    double _stepMs0, _viewMs0, _think0, _move0, _rig0;

    void SplitStart()
    {
        if (!_args.ContainsKey("split")) return;
        _frameSplit ??= new FrameSplit(GetTree().Root, GetViewport().GetViewportRid());
        _systemSplit ??= new SystemSplit(_game, _game.Zone, _game.Zone.Wildlife, _game.Places);
        _frameSplit.Systems = _systemSplit;
        _frameSplit.Doing = Doing;
        _frameSplit.Reset();
        if (_args.ContainsKey("allocs")) (_allocTypes ??= new AllocCensus { On = true }).Take();
        _systemSplit.Reset();
        var zone = _game.Zone;
        _sections0 = (double[])zone.Streamer.SectionTotals.Clone();
        _rays0 = zone.Wildlife.SightRays;
        _paths0 = zone.Wildlife.Sim.Paths + zone.Wildlife.Sites.Sum(s => s.Sim.Paths);
        (_gc0, _gc1, _gc2) = (GC.CollectionCount(0), GC.CollectionCount(1), GC.CollectionCount(2));
        _physicsObjects = _pairs = _creatures = _stepped = _creaturesDrawn = _siteCreatures = _siteStepped = _siteDrawn = _people = _peopleDrawn = _townMs = 0;
        _samplesTaken = 0;
        var sim = zone.Wildlife.Sim;
        sim.Profile = true;
        (_stepMs0, _viewMs0, _think0, _move0, _rig0) = (zone.Wildlife.StepMs, zone.Wildlife.ViewMs, sim.ThinkMs, sim.MoveMs, sim.RigMs);
        _game.Places.ResetWorst();
        _game.Places.StageWorst.Clear();
        _game.Places.SlowSteps.Clear();
    }

    /// <summary>A sample of what is live (every third tick of a route).</summary>
    void SplitSample()
    {
        if (_frameSplit == null) return;
        var w = _game.Zone.Wildlife;
        _samplesTaken++;
        _physicsObjects += Performance.GetMonitor(Performance.Monitor.Physics3DActiveObjects);
        _pairs += Performance.GetMonitor(Performance.Monitor.Physics3DCollisionPairs);
        _creatures += w.Sim.Creatures.Count(c => !c.Removed);
        _stepped += w.Sim.Stepped;
        _creaturesDrawn += w.View.DrawnCount;
        foreach (var s in w.Sites)
        {
            _siteCreatures += s.Sim.Creatures.Count(c => !c.Removed);
            _siteStepped += s.Sim.Stepped;
            _siteDrawn += s.DrawnCount;
        }
        foreach (var p in _game.Places.Standing)
            if (p.Memory.Town is { } town)
            {
                _people += town.People.Count;
                _peopleDrawn += town.People.Count(q => q.View is { Visible: true });
                _townMs += town.LastTickMs;
            }
    }

    /// <summary>What was going on, for a hitch's note: the place standing up, the slowest site and town tick, the patch, the streamer.</summary>
    string Doing()
    {
        var w = _game.Zone.Wildlife;
        var site = w.Sites.MaxBy(s => s.LastStepMs);
        var town = _game.Places.Standing.Select(p => p.Memory.Town).Where(t => t != null).MaxBy(t => t.LastTickMs);
        return string.Create(CultureInfo.InvariantCulture, $"building {_game.Places.Building ?? "nothing"}, slowest site step {site?.LastStepMs ?? 0:F1} ms ({site?.Name}, {site?.Sim.Creatures.Count} creatures), slowest town tick {town?.LastTickMs ?? 0:F1} ms, patches {w.Patches}, nodes {Performance.GetMonitor(Performance.Monitor.ObjectNodeCount)}, streamer {_game.Zone.Streamer.LastFrameMs:F1} ms");
    }

    AllocCensus _allocTypes;

    /// <summary>
    /// What stands drawn at the route's end, by where it hangs (the zone's or the game's child, a level further for the
    /// wildlife) and its kind: instances visible in the tree and within their visibility range of the eye, those of them in
    /// view, how many cast shadows, and the instances their multimeshes hold.
    /// </summary>
    string DrawnCensus()
    {
        Camera3D cam = _game.Camera;
        Vector3 eye = cam.GlobalPosition;
        var groups = new Dictionary<string, (int Nodes, int InView, int Casting, long Multi)>();
        void Walk(Node n, string group)
        {
            if (n is Node3D { Visible: false }) return;
            if (n is GeometryInstance3D g)
            {
                Aabb box = g.GlobalTransform * g.GetAabb();
                float d = (box.GetCenter() - eye).Length();
                if (g.VisibilityRangeEnd <= 0f || d <= g.VisibilityRangeEnd + g.VisibilityRangeEndMargin)
                {
                    string key = group + " " + g.GetClass();
                    var e = groups.GetValueOrDefault(key);
                    e.Nodes++;
                    if (cam.IsPositionInFrustum(box.GetCenter()) || box.HasPoint(eye) || d < box.Size.Length()) e.InView++;
                    if (g.CastShadow != GeometryInstance3D.ShadowCastingSetting.Off) e.Casting++;
                    if (g is MultiMeshInstance3D { Multimesh: { } mm }) e.Multi += mm.VisibleInstanceCount >= 0 ? mm.VisibleInstanceCount : mm.InstanceCount;
                    groups[key] = e;
                }
            }
            // Split at the game's children, the zone's and the wildlife's.
            foreach (Node child in n.GetChildren())
                Walk(child, group ?? (child == _game.Zone || child == _game.Zone.Wildlife ? null : child.Name.ToString()));
        }
        Walk(_game, null);
        return string.Join(", ", groups.OrderByDescending(kv => kv.Value.Nodes).Take(16).Select(kv => $"{kv.Key} {kv.Value.Nodes} ({kv.Value.InView} in view, {kv.Value.Casting} casting{(kv.Value.Multi > 0 ? $", {kv.Value.Multi} in multimeshes" : "")})"));
    }

    void SplitReport(string name, int ticks)
    {
        if (_frameSplit == null) return;
        var zone = _game.Zone;
        TerrainStreamer s = zone.Streamer;
        var w = zone.Wildlife;
        double n = Math.Max(1, _samplesTaken), t = Math.Max(1, ticks);
        string sections = string.Join(", ", TerrainStreamer.SectionNames.Select((sec, i) => string.Create(CultureInfo.InvariantCulture, $"{sec} {(s.SectionTotals[i] - _sections0[i]) / Math.Max(1, _frameSplit.Frames.Count):F2}")));
        int paths = w.Sim.Paths + w.Sites.Sum(q => q.Sim.Paths) - _paths0;
        var places = _game.Places;
        GD.Print($"SPLIT {name} frames: {_frameSplit.Report()}");
        foreach (string h in _frameSplit.Hitches) GD.Print($"SPLIT {name} hitch {h}");
        double frames = Math.Max(1, _frameSplit.Frames.Count);
        GD.Print(string.Create(CultureInfo.InvariantCulture, $"SPLIT {name} the patch's creatures, ms a frame: sim {(w.StepMs - _stepMs0) / frames:F2} (brains {(w.Sim.ThinkMs - _think0) / frames:F2}, moving {(w.Sim.MoveMs - _move0) / frames:F2}, rigs {(w.Sim.RigMs - _rig0) / frames:F2}), bodies {(w.ViewMs - _viewMs0) / frames:F2}"));
        GD.Print($"SPLIT {name} by system (cpu ms a frame): {_systemSplit.Report(18)}");
        GD.Print($"SPLIT {name} drawn at the end: {DrawnCensus()}");
        if (_allocTypes != null) GD.Print($"SPLIT {name} allocated, by type (main: the main thread's): {_allocTypes.Take(16)}");
        GD.Print(string.Create(CultureInfo.InvariantCulture,
            $"SPLIT {name} live (means): creatures in the patch {_creatures / n:F0}, stepped {_stepped / n:F0}, drawn {_creaturesDrawn / n:F0}; in {w.Sites.Count} places and caves {_siteCreatures / n:F0}, stepped {_siteStepped / n:F0}, drawn {_siteDrawn / n:F0}; " +
            $"people {_people / n:F0}, drawn {_peopleDrawn / n:F0}, their towns' ticks {_townMs / n:F2} ms; sight rays {(w.SightRays - _rays0) / t:F1} a tick, path searches {paths / t:F2} a tick; " +
            $"physics: active objects {_physicsObjects / n:F0}, collision pairs {_pairs / n:F0}, ground bodies {s.CollisionBodies}, solid flora {s.SolidColliders} in {s.SolidBodies} bodies; " +
            $"streaming ms a frame by section: {sections}; places standing {places.Standing.Count}, worst stand-up frame {places.WorstFrameMs:F1} ms, worst step {places.WorstStepMs:F1} ms " +
            $"({string.Join(", ", places.StageWorst.OrderByDescending(kv => kv.Value).Take(4).Select(kv => string.Create(CultureInfo.InvariantCulture, $"{kv.Key} {kv.Value:F1}")))}); " +
            $"collections gen0 {GC.CollectionCount(0) - _gc0}, gen1 {GC.CollectionCount(1) - _gc1}, gen2 {GC.CollectionCount(2) - _gc2}"));
    }
}
