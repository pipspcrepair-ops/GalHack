using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using GalHack.Physics;
using GalHack.Physics.Engine;
using Godot;
using PlanetSurface.Poi;
using PoiGenerator.Gen;
using Slice.Game;
using Slice.Places;
using Int3 = PoiGenerator.Gen.Int3;

namespace Slice.Tests;

/// <summary>
/// Frame times at a large place, as a player would see them. Run windowed, never headless: nothing
/// else measures what a frame really costs. Vsync is off and the cap lifted, so a frame takes what
/// it takes.
///   godot --path prototypes/slice res://tests/PlacePerf.tscn -- --family=sewage_works [--seed=N] [--planet=temperate] [--variant=0]
/// One PERF line per stage: far off, riding in from a kilometre out, at the gate, inside. Prints each
/// frame's visible time (average, 95th, 99th, worst), the renderer's own CPU and GPU time, and what
/// was drawn.
/// </summary>
public partial class PlacePerf : Node
{
    const int Frames = 240;
    Game.Game _game;
    Driver _drive;
    IEnumerator<int> _run;
    readonly Dictionary<string, string> _args = new();
    readonly Stopwatch _clock = new();
    long _last;
    List<double> _times;
    readonly List<double> _cpu = new(), _gpu = new();
    long _prims, _draws, _objs, _shadowPrims, _shadowDraws;
    PlanetSurface.FrameProbe _probe;
    readonly List<double> _script = new();
    readonly List<string> _hitches = new();
    int _gc0, _gc2;

    public override void _Ready()
    {
        foreach (string arg in OS.GetCmdlineUserArgs())
        {
            int eq = arg.IndexOf('=');
            if (arg.StartsWith("--") && eq > 2) _args[arg[2..eq]] = arg[(eq + 1)..];
        }
        DisplayServer.WindowSetVsyncMode(DisplayServer.VSyncMode.Disabled);
        Engine.MaxFps = 0;
        if (_args.TryGetValue("family", out string family)) PoiSiting.ForceFamily = family;
        if (_args.TryGetValue("variant", out string v)) PoiSiting.ForceVariant = int.Parse(v);
        if (_args.TryGetValue("state", out string st)) PoiSiting.ForceState = Enum.Parse<PoiState>(st, true);
        ProcessPhysicsPriority = 100;
        ProcessPriority = 1_000_000;
        _game = new Game.Game { Interactive = false, SkipIntro = true, World = WorldChoice.FromArgs(OS.GetCmdlineUserArgs(), WorldChoice.Tested) };
        AddChild(_game);
        _drive = new Driver(_game);
        RenderingServer.ViewportSetMeasureRenderTime(GetViewport().GetViewportRid(), true);
        _clock.Start();
        _probe = new PlanetSurface.FrameProbe(this);
        _run = Scenario().GetEnumerator();
    }

    public override void _PhysicsProcess(double delta)
    {
        bool more;
        try { more = _run.MoveNext(); }
        catch (Exception e)
        {
            GD.PrintErr($"FAIL perf: {e}");
            more = false;
        }
        if (more) return;
        SetPhysicsProcess(false);
        Wildlife.Shutdown.Quit(this, 0);
    }

    public override void _Process(double delta)
    {
        long now = _clock.ElapsedTicks;
        double ms = (now - _last) * 1000.0 / Stopwatch.Frequency;
        _last = now;
        if (_times == null) return;
        _times.Add(ms);
        _script.Add(_probe.LastMs);
        int gc = GC.CollectionCount(0), gc2 = GC.CollectionCount(2);
        if (ms > 33.4 && _hitches.Count < 40) _hitches.Add($"{ms:F0}ms script {_probe.LastMs:F0} frame {_times.Count} gc0 +{gc - _gc0} gc2 +{gc2 - _gc2} at {_game.Survivor?.WorldFeet.X:F0},{_game.Survivor?.WorldFeet.Z:F0} objs {Performance.GetMonitor(Performance.Monitor.ObjectNodeCount)}");
        _gc0 = gc;
        _gc2 = gc2;
        Rid vp = GetViewport().GetViewportRid();
        _cpu.Add(RenderingServer.ViewportGetMeasuredRenderTimeCpu(vp));
        _gpu.Add(RenderingServer.ViewportGetMeasuredRenderTimeGpu(vp));
        Viewport v = GetViewport();
        _prims = Math.Max(_prims, v.GetRenderInfo(Viewport.RenderInfoType.Visible, Viewport.RenderInfo.PrimitivesInFrame));
        _draws = Math.Max(_draws, v.GetRenderInfo(Viewport.RenderInfoType.Visible, Viewport.RenderInfo.DrawCallsInFrame));
        _objs = Math.Max(_objs, v.GetRenderInfo(Viewport.RenderInfoType.Visible, Viewport.RenderInfo.ObjectsInFrame));
        _shadowPrims = Math.Max(_shadowPrims, v.GetRenderInfo(Viewport.RenderInfoType.Shadow, Viewport.RenderInfo.PrimitivesInFrame));
        _shadowDraws = Math.Max(_shadowDraws, v.GetRenderInfo(Viewport.RenderInfoType.Shadow, Viewport.RenderInfo.DrawCallsInFrame));
    }

    void Begin()
    {
        _times = new List<double>();
        _script.Clear();
        _cpu.Clear();
        _gpu.Clear();
        _prims = _draws = _objs = _shadowPrims = _shadowDraws = 0;
    }

    void End(string stage)
    {
        List<double> t = _times;
        _times = null;
        if (t.Count == 0) { GD.Print($"PERF {stage}: no frames"); return; }
        t.Sort();
        double P(double q) => t[Math.Min(t.Count - 1, (int)(t.Count * q))];
        int hitches = t.Count(x => x > 33.4);
        string line = string.Create(CultureInfo.InvariantCulture,
            $"PERF {stage,-16} frames {t.Count,4} avg {t.Average(),6:F1} p50 {P(0.5),6:F1} p95 {P(0.95),6:F1} p99 {P(0.99),6:F1} worst {t[^1],7:F1} over33 {hitches,3} | cpu {_cpu.DefaultIfEmpty(0).Average(),5:F1} gpu {_gpu.DefaultIfEmpty(0).Average(),5:F1} | prims {_prims / 1000,6}k draws {_draws,5} objs {_objs,5} shadow {_shadowPrims / 1000,6}k/{_shadowDraws,5} | script {_script.DefaultIfEmpty(0).Average(),5:F1} nodes {Performance.GetMonitor(Performance.Monitor.ObjectNodeCount),6} active {Performance.GetMonitor(Performance.Monitor.Physics3DActiveObjects),4} pairs {Performance.GetMonitor(Performance.Monitor.Physics3DCollisionPairs),5}");
        GD.Print(line);
        foreach (string h in _hitches) GD.Print($"PERF hitch {h}");
        _hitches.Clear();
    }

    IEnumerable<int> Measure(string stage, int frames = Frames)
    {
        Begin();
        while (_times.Count < frames) yield return 0;
        End(stage);
    }

    IEnumerable<int> Scenario()
    {
        foreach (int x in _drive.UntilPlaying(300)) yield return x;
        var zone = _game.Zone;
        zone.Weather.Bridge.Calm = true;
        zone.Sky.TimeOfDay = 0.4;
        zone.Weather.Sim.Snap(Weather.Model.WeatherKind.Clear);
        foreach (int x in _drive.ClimbOut(_game.Bunker)) yield return x;
        GD.Print($"PERF world {_game.World}; places {zone.Planet.Pois.Count}, grand {zone.Planet.Pois.Count(s => s.Family.Tags.Contains("grand"))}");
        foreach (var s in zone.Planet.Pois.OrderBy(s => _game.Places.DistanceTo(s, _game.Survivor.WorldFeet)))
            GD.Print($"PERF site {s.Family.Id} '{s.Name}' {s.Instance.State} {_game.Places.DistanceTo(s, _game.Survivor.WorldFeet):F0} m, footprint {s.Footprint.Max.X - s.Footprint.Min.X}x{s.Footprint.Max.Z - s.Footprint.Min.Z}, blocks {s.Instance.Blocks.CountBlocks()}");
        foreach (int x in Measure("start", 120)) yield return x;

        string family = _args.GetValueOrDefault("family");
        var sites = zone.Planet.Pois.Where(s => family != null ? s.Family.Id == family : s.Family.Tags.Contains("grand")).OrderBy(s => _game.Places.DistanceTo(s, _game.Survivor.WorldFeet)).Take(int.Parse(_args.GetValueOrDefault("sites", "1")));
        foreach (PoiSite site in sites.ToList())
            foreach (int x in OnePlace(site)) yield return x;
    }

    /// <summary>What's in the tree, by the first levels it hangs from: where the nodes and meshes are.</summary>
    void Census(Node root)
    {
        var nodes = new Dictionary<string, int>();
        var meshes = new Dictionary<string, int>();
        void Walk(Node n, string path, int depth)
        {
            string name = depth <= 5 ? new string(n.Name.ToString().TakeWhile(c => !char.IsDigit(c) && c != '(').ToArray()).Trim() : "";
            if (depth <= 5) path = path + "/" + name;
            nodes[path] = nodes.GetValueOrDefault(path) + 1;
            if (n is MeshInstance3D { Visible: true }) meshes[path] = meshes.GetValueOrDefault(path) + 1;
            foreach (Node c in n.GetChildren()) Walk(c, path, depth + 1);
        }
        Walk(root, "", 0);
        foreach (var (k, v) in nodes.OrderByDescending(kv => kv.Value).Take(24)) GD.Print($"PERF census {v,6} nodes, {meshes.GetValueOrDefault(k),5} meshes {k}");
    }

    IEnumerable<int> OnePlace(PoiSite site)
    {
        var zone = _game.Zone;
        var places = _game.Places;
        GD.Print($"PERF place {site.Family.Id} '{site.Name}' {site.Instance.State}");
        Vector3 Centre() => zone.Planet.NearestCopy(new Vector3((site.Footprint.Min.X + site.Footprint.Max.X) * 0.5f, site.Origin.Y + 8f, (site.Footprint.Min.Z + site.Footprint.Max.Z) * 0.5f), _game.Survivor.WorldFeet);
        Vector3 Gate() => zone.Planet.NearestCopy(new Vector3(site.Entrance.X + 0.5f, site.Entrance.Y, site.Entrance.Z + 0.5f), _game.Survivor.WorldFeet);
        Int3 f = site.Instance.Layout.EntranceFacing.Vec();
        Vector3 outward = new Vector3(f.X, 0f, f.Z);
        if (outward.LengthSquared() < 0.1f) outward = Vector3.Right;

        Vector3 Out(float metres)
        {
            Vector3 p = Gate() + outward * metres;
            p.Y = zone.GroundAt(p.X, p.Z).Y + 0.3f;
            return p;
        }

        foreach (float d in new[] { 1000f, 500f })
        {
            _game.Survivor.PlaceOutside(Out(d).ToNumerics());
            foreach (int x in _drive.Wait(60)) yield return x;
            foreach (int x in _drive.UntilSettled(30)) yield return x;
            foreach (int x in _drive.Wait(120)) yield return x;
            foreach (int x in _drive.LookAt(Centre(), 6)) yield return x;
            foreach (int x in Measure($"far {d:F0}")) yield return x;
        }

        // Riding in from a kilometre out at a hover bike's speed: what the streaming costs.
        const float Speed = 22f;
        Vector3 from = Out(1000f), to = Out(8f);
        _game.Survivor.PlaceOutside(from.ToNumerics());
        foreach (int x in _drive.Wait(60)) yield return x;
        foreach (int x in _drive.UntilSettled(30)) yield return x;
        Begin();
        float length = from.DistanceTo(to), along = 0f;
        while (along < length)
        {
            along += Speed / Engine.PhysicsTicksPerSecond;
            Vector3 p = from.Lerp(to, along / length);
            p.Y = zone.GroundAt(p.X, p.Z).Y + 1f;
            _game.Survivor.PlaceOutside(p.ToNumerics());
            _drive.Face(Centre());
            yield return 0;
        }
        End("ride in");
        for (int i = 0; i < 1800 && places.PlaceOf(site) is not { Ready: true }; i++) yield return 0;

        _game.Survivor.PlaceOutside(Out(26f).ToNumerics());
        foreach (int x in _drive.Wait(30)) yield return x;
        foreach (int x in _drive.UntilSettled(30)) yield return x;
        foreach (int x in _drive.LookAt(Gate() + Vector3.Up * 3f, 6)) yield return x;
        foreach (int x in Measure("gate")) yield return x;
        if (_args.ContainsKey("toggles"))
        {
            Node creatures = places.PlaceOf(site)?.GetNodeOrNull("Creatures");
            if (creatures != null) creatures.ProcessMode = ProcessModeEnum.Disabled;
            GD.Print($"PERF creatures node {creatures != null}");
            foreach (int x in Measure("gate-creatures", 120)) yield return x;
            zone.Wildlife.Frozen = true;
            foreach (int x in Measure("gate-wildlife", 120)) yield return x;
            int anim = 0;
            void Walk(Node n)
            {
                foreach (Node c in n.GetChildren())
                {
                    if (c is GalHack.Deco.Engine.DecoNode d) { if (d.IsProcessing()) anim++; d.SetProcess(false); }
                    Walk(c);
                }
            }
            Walk(places);
            GD.Print($"PERF animated deco {anim}");
            foreach (int x in Measure("gate-deco anim", 120)) yield return x;
            zone.Infra.ProcessMode = ProcessModeEnum.Disabled;
            foreach (int x in Measure("gate-infra", 120)) yield return x;
            zone.Pois.ProcessMode = ProcessModeEnum.Disabled;
            foreach (int x in Measure("gate-pois", 120)) yield return x;
            zone.Streamer.ProcessMode = ProcessModeEnum.Disabled;
            foreach (int x in Measure("gate-terrain", 120)) yield return x;
            places.ProcessMode = ProcessModeEnum.Disabled;
            foreach (int x in Measure("gate-places", 120)) yield return x;
            zone.ProcessMode = ProcessModeEnum.Disabled;
            foreach (int x in Measure("gate-zone", 120)) yield return x;
            yield break;
        }

        Place place = places.PlaceOf(site);
        if (place == null) { GD.Print("PERF place never stood"); yield break; }
        GD.Print($"PERF built in {places.BuildLog.LastOrDefault().Seconds:F1} s, {place.ChunkCount} chunks, {place.Groups} cubes, {place.NearGroups} near, {place.Doors.Count} doors, {place.Memory.Fauna?.Sim?.Creatures.Count} creatures, {place.DecoPieces} pieces of {place.Memory.Rooms?.Values.Sum(r => r.Plan.Placements.Count)}; worst build step {places.WorstStepMs:F1} ms, worst build frame {places.WorstFrameMs:F1} ms");
        // A roofed spot well inside: the way a player would walk there.
        var reach = place.Nav().Reachable(site.Instance.Layout.EntranceCell);
        var inside = reach.Where(c => place.Roofed(place.WorldOf(c, 0.2f))).OrderBy(c => c.X + c.Z * 3).ToList();
        GD.Print($"PERF reachable {reach.Count}, roofed {inside.Count}");
        if (inside.Count > 0)
        {
            Int3 cell = inside[inside.Count / 2];
            _game.Survivor.PlaceOutside(place.WorldOf(cell, 0.1f).ToNumerics());
            foreach (int x in _drive.Wait(30)) yield return x;
            Begin();
            for (int i = 0; i < 360; i++)
            {
                _game.Controls.Look = new Vector2(40f, 0f);
                yield return 0;
            }
            _game.Controls.Look = Vector2.Zero;
            End("inside");
        }
        _game.Survivor.PlaceOutside(Out(-40f).ToNumerics());
        foreach (int x in _drive.Wait(30)) yield return x;
        foreach (int x in _drive.LookAt(Centre(), 6)) yield return x;
        foreach (int x in Measure("in the yard")) yield return x;
        Census(_game);
    }
}
