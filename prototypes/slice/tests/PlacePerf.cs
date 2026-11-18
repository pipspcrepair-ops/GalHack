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
/// --as=airport: the forced family planned as that type. --fly=1: a typed place (an airfield) is held back from the builder until a craft at top speed flies in from where it
/// first comes into view; --compare-force=1: then the same world without it, over the same routes.
/// One PERF line per stage: far off, riding in from a kilometre out, at the gate, inside. Prints each
/// frame's visible time (average, 95th, 99th, worst), the renderer's own CPU and GPU time, and what
/// was drawn.
/// </summary>
public partial class PlacePerf : Node
{
    const int Frames = 240;
    /// <summary>A place whose walkable floors span this many metres or more is timed on its middle and top floors too.</summary>
    const int ManyStoreys = 12;
    /// <summary>A frame at 60 fps, ms.</summary>
    const double Budget = 1000.0 / 60.0;
    Game.Game _game;
    Driver _drive;
    IEnumerator<int> _run;
    readonly Dictionary<string, string> _args = new();
    readonly Stopwatch _clock = new();
    long _last;
    List<double> _times;
    readonly List<double> _cpu = new(), _gpu = new();
    long _prims, _draws, _objs, _shadowPrims, _shadowDraws, _mainAlloc, _allAlloc;
    int _gcAt0, _gcAt1, _gcAt2;
    ulong _ticksAt;
    double _physics;
    PlanetSurface.FrameProbe _probe;
    SystemSplit _split;
    AllocCensus _allocs;
    readonly List<double> _script = new();
    double _townTick, _townPose;
    int _plainFrames, _plainSites;
    /// <summary>The physics pass's scripts (first to last node), and the engine's step after them, ms (measured by two edge nodes).</summary>
    double _tickMs, _stepMs;
    long _tickStart, _tickEnd;

    sealed partial class TickEdge : Node
    {
        public PlacePerf Perf;
        public bool Start;
        public override void _PhysicsProcess(double delta)
        {
            long now = Stopwatch.GetTimestamp();
            if (Start)
            {
                if (Perf._tickEnd != 0) Perf._stepMs += (now - Perf._tickEnd) * 1000.0 / Stopwatch.Frequency;
                Perf._tickStart = now;
            }
            else
            {
                Perf._tickMs += (now - Perf._tickStart) * 1000.0 / Stopwatch.Frequency;
                Perf._tickEnd = now;
            }
        }
        public override void _Process(double delta) => Perf._tickEnd = 0; // (the engine's step only counts up to the next tick in the same frame)
    }
    readonly List<string> _hitches = new();
    int _gc0, _gc2;
    bool _failed;

    public override void _Ready()
    {
        // A wall-clock limit on a thread of its own: a run that hangs still quits.
        new System.Threading.Thread(() =>
        {
            System.Threading.Thread.Sleep(TimeSpan.FromMinutes(25));
            Console.Error.WriteLine("PlacePerf took over 25 minutes of wall-clock time: watchdog");
            System.Environment.Exit(1);
        }) { IsBackground = true }.Start();
        foreach (string arg in OS.GetCmdlineUserArgs())
        {
            int eq = arg.IndexOf('=');
            if (arg.StartsWith("--") && eq > 2) _args[arg[2..eq]] = arg[(eq + 1)..];
        }
        if (_args.TryGetValue("survey", out string survey))
        {
            // Fresh start worlds, and how far each is from a large place: --survey=N.
            PoiSiting.StartWorld = true;
            for (int i = 0; i < int.Parse(survey); i++)
            {
                WorldChoice w = WorldChoice.Fresh("temperate");
                var world = PlanetSurface.World.PlanetWorld.Build(w.Spec(), Slice.Surface.SurfaceZone.PoiCatalog);
                var grand = world.Pois.Where(s => s.Family.Tags.Contains("grand")).Select(s =>
                {
                    double dx = Planets.Gen.Mathx.WrapDelta(s.Center.X - world.Landing.X, world.Width), dz = Planets.Gen.Mathx.WrapDelta(s.Center.Z - world.Landing.Y, world.Height);
                    return (s.Family.Id, Off: Math.Sqrt(dx * dx + dz * dz), s.State, s.Occupancy);
                }).OrderBy(g => g.Off).ToList();
                GD.Print($"PERF survey seed {w.Seed}: {string.Join("; ", grand.Take(4).Select(g => $"{g.Id} {g.Off:F0} m {g.State} {g.Occupancy}"))}");
                if (_args.TryGetValue("routes", out string path))
                {
                    var o = new System.Text.StringBuilder();
                    foreach (var r in world.Infra.Routes) o.AppendLine($"R {r.Kind} {r.HalfWidth} " + string.Join(" ", Enumerable.Range(0, r.Count).Where(q => q % 3 == 0 || q == r.Count - 1).Select(q => FormattableString.Invariant($"{r.X[q]:F0},{r.Z[q]:F0}"))));
                    foreach (var q in world.Pois) o.AppendLine(FormattableString.Invariant($"P {q.Family.Id} {q.Center.X:F0} {q.Center.Z:F0}"));
                    o.AppendLine(FormattableString.Invariant($"L {world.Landing.X:F0} {world.Landing.Y:F0} {world.Width} {world.Height}"));
                    System.IO.File.WriteAllText(path + i + ".txt", o.ToString());
                }
            }
            Wildlife.Shutdown.Quit(this, 0);
            return;
        }
        JoltWatch.Install();
        // --settings=path: as the owner plays (his settings, vsync on and paced, full screen; --screen=N for which); --minspec=1: the
        // minimum spec's stand-in as PopRoute's (4 cores, workers for 4, the lowest graphics). Without either, vsync off and no cap.
        if (_args.ContainsKey("minspec"))
        {
            Process.GetCurrentProcess().ProcessorAffinity = 0x55;
            PlanetSurface.Terrain.TerrainStreamer.Cores = 4;
            _args.TryAdd("settings", "res://tests/minspec.cfg");
        }
        if (_args.ContainsKey("settings"))
        {
            if (_args.TryGetValue("screen", out string screen)) DisplayServer.WindowSetCurrentScreen(int.Parse(screen));
            DisplayServer.WindowSetMode(DisplayServer.WindowMode.Fullscreen);
            DisplayServer.WindowSetVsyncMode(DisplayServer.VSyncMode.Enabled);
        }
        else
        {
            DisplayServer.WindowSetVsyncMode(DisplayServer.VSyncMode.Disabled);
            Engine.MaxFps = 0;
        }
        if (_args.TryGetValue("family", out string family) && !_args.ContainsKey("asrolled")) PoiSiting.ForceFamily = family;
        if (_args.ContainsKey("audit") || _args.ContainsKey("city")) PlanetSurface.Infra.CityPlan.Force = true;
        if (_args.TryGetValue("as", out string type)) PoiSiting.ForceType = type;
        if (_args.ContainsKey("fly")) PoiBuilds.Withhold = s => s.Whole != null;
        if (_args.ContainsKey("noease")) Wildlife.DrawSmoother.Off = true; // --noease=1: bodies drawn as the sim has them (the A side of an A/B)
        if (_args.ContainsKey("allawake")) Surface.DeepSite.AllAwake = true; // --allawake=1: places' unseen creatures step wherever they are (the A side)
        if (_args.GetValueOrDefault("bodies") == "pieces") Wildlife.CreatureView.Merged = false;
        if (_args.TryGetValue("variant", out string v)) PoiSiting.ForceVariant = int.Parse(v);
        if (_args.TryGetValue("state", out string st)) PoiSiting.ForceState = Enum.Parse<PoiState>(st, true);
        ProcessPhysicsPriority = 100;
        ProcessPriority = 1_000_000;
        // --places-at-load=1: every place built while the world loads, the old way. --compare-load=1: the old way, then the same world again with the place builder.
        // --compare-load=new: the builder's run first (the second game in a process runs on a bigger heap, so either order is worth a look).
        PlanetSurface.World.PlanetWorld.BuildPlacesAtLoad = _args.ContainsKey("places-at-load") || _args.ContainsKey("compare-load") && _args["compare-load"] != "new";
        NewGame(WorldChoice.FromArgs(OS.GetCmdlineUserArgs(), WorldChoice.Tested));
        RenderingServer.ViewportSetMeasureRenderTime(GetViewport().GetViewportRid(), true);
        AddChild(new TickEdge { Perf = this, Start = true, ProcessPhysicsPriority = -3_000_000, ProcessPriority = -3_000_000 });
        AddChild(new TickEdge { Perf = this, Start = false, ProcessPhysicsPriority = 3_000_000, ProcessPriority = 3_000_000 });
        _clock.Start();
        _probe = new PlanetSurface.FrameProbe(this);
        _run = Runs().GetEnumerator();
    }

    void NewGame(WorldChoice world)
    {
        _game = new Game.Game { Interactive = false, SkipIntro = true, World = world, SettingsPath = _args.GetValueOrDefault("settings") };
        _loading = Stopwatch.StartNew();
        AddChild(_game);
        _drive = new Driver(_game);
    }

    IEnumerable<int> Runs()
    {
        GD.Print($"PERF mode: places {(PlanetSurface.World.PlanetWorld.BuildPlacesAtLoad ? "built at load (old)" : "built after standing")}");
        foreach (int x in _args.ContainsKey("bands") ? BandScene() : _args.ContainsKey("standing") ? Standing() : Scenario()) yield return x;
        if (_args.ContainsKey("compare-force") && _flyTo != Vector3.Zero)
        {
            // The same world without the forced place, over the same routes.
            WorldChoice same = _game.World;
            _game.QueueFree();
            for (int i = 0; i < 10; i++) yield return 0;
            (PoiSiting.ForceFamily, PoiSiting.ForceType, PoiBuilds.Withhold) = (null, null, null);
            NewGame(same);
            GD.Print("PERF mode: the same world without the forced place, the same routes");
            foreach (int x in Stand()) yield return x;
            foreach (int x in FlyIn(null)) yield return x;
            foreach (int x in Ride("ride in (none)", _rideFrom, _rideTo, _rideTo)) yield return x;
            yield break;
        }
        if (!_args.ContainsKey("compare-load")) yield break;
        WorldChoice world = _game.World;
        _game.QueueFree();
        for (int i = 0; i < 10; i++) yield return 0;
        PlanetSurface.World.PlanetWorld.BuildPlacesAtLoad = !PlanetSurface.World.PlanetWorld.BuildPlacesAtLoad;
        NewGame(world);
        GD.Print($"PERF mode: places {(PlanetSurface.World.PlanetWorld.BuildPlacesAtLoad ? "built at load (old)" : "built after standing")}, the same world");
        foreach (int x in _args.ContainsKey("standing") ? Standing() : Scenario()) yield return x;
    }

    public override void _PhysicsProcess(double delta)
    {
        bool more;
        try { more = _run.MoveNext(); }
        catch (Exception e)
        {
            GD.PrintErr($"FAIL perf: {e}");
            _failed = true;
            more = false;
        }
        if (more) return;
        SetPhysicsProcess(false);
        Wildlife.Shutdown.Quit(this, _failed ? 1 : 0);
    }

    public override void _Process(double delta)
    {
        long now = _clock.ElapsedTicks;
        double ms = (now - _last) * 1000.0 / Stopwatch.Frequency;
        _last = now;
        if (_times == null) return;
        _times.Add(ms);
        _script.Add(_probe.LastMs);
        foreach (Slice.Places.Place pl in _game.Places.Standing)
            if (pl.Memory.Town is { } town) { _townTick += town.LastTickMs; _townPose += town.LastPoseMs; }
        // Places drawn as their plain far lump (not yet their merged blocks) in view within 600 m: the stand-ins a player sees.
        if (_game.Camera is { } cam)
        {
            int plain = 0;
            foreach (PoiSite site in _game.Zone.Planet.Pois)
            {
                Vector3 at = _game.Zone.Planet.NearestCopy(new Vector3(site.Origin.X, site.Origin.Y + 4f, site.Origin.Z), cam.GlobalPosition);
                if (at.DistanceTo(cam.GlobalPosition) > 600f || !cam.IsPositionInFrustum(at)) continue;
                if (_game.Zone.Pois.Shows(site) == 1 && _game.Places.PlaceOf(site) is not { Ready: true }) plain++;
            }
            if (plain > 0) _plainFrames++;
            _plainSites += plain;
        }
        int gc = GC.CollectionCount(0), gc2 = GC.CollectionCount(2);
        if (ms > 33.4 && _hitches.Count < 40) _hitches.Add($"{ms:F0}ms script {_probe.LastMs:F0} frame {_times.Count} gc0 +{gc - _gc0} gc2 +{gc2 - _gc2} pause {GC.GetGCMemoryInfo(GCKind.Ephemeral).PauseDurations[0].TotalMilliseconds:F0} promoted {GC.GetGCMemoryInfo(GCKind.Ephemeral).PromotedBytes / 1024 / 1024} MB heap {GC.GetGCMemoryInfo(GCKind.Ephemeral).HeapSizeBytes / 1024 / 1024} MB gen0 {GC.GetGCMemoryInfo(GCKind.Ephemeral).GenerationInfo[0].SizeBeforeBytes / 1024 / 1024} at {_game.Survivor?.WorldFeet.X:F0},{_game.Survivor?.WorldFeet.Z:F0} objs {Performance.GetMonitor(Performance.Monitor.ObjectNodeCount)}");
        _gc0 = gc;
        _gc2 = gc2;
        Rid vp = GetViewport().GetViewportRid();
        _cpu.Add(RenderingServer.ViewportGetMeasuredRenderTimeCpu(vp));
        _gpu.Add(RenderingServer.ViewportGetMeasuredRenderTimeGpu(vp));
        Viewport v = GetViewport();
        _prims += v.GetRenderInfo(Viewport.RenderInfoType.Visible, Viewport.RenderInfo.PrimitivesInFrame);
        _draws += v.GetRenderInfo(Viewport.RenderInfoType.Visible, Viewport.RenderInfo.DrawCallsInFrame);
        _objs += v.GetRenderInfo(Viewport.RenderInfoType.Visible, Viewport.RenderInfo.ObjectsInFrame);
        _shadowPrims += v.GetRenderInfo(Viewport.RenderInfoType.Shadow, Viewport.RenderInfo.PrimitivesInFrame);
        _shadowDraws += v.GetRenderInfo(Viewport.RenderInfoType.Shadow, Viewport.RenderInfo.DrawCallsInFrame);
        _physics = Math.Max(_physics, Performance.GetMonitor(Performance.Monitor.TimePhysicsProcess) * 1000.0);
        foreach (Surface.DeepSite s in _game.Zone.Wildlife.Sites)
        {
            _stepped += s.Sim.Stepped;
            _siteCreatures += s.Sim.Creatures.Count;
            if (_game.Camera == null) continue;
            Vector3 eye = s.Local(_game.Camera.GlobalPosition);
            foreach (Wildlife.Creature c in s.Sim.Creatures)
            {
                if (c.Removed || c.Dormant) continue;
                if (Wildlife.Brain.Busy(c) || c.Attacking) _awakeBusy++;
                else if ((c.Position - eye).Length() < 30f) _awakeNear++;
                else _awakeSeen++;
            }
        }
    }

    void Begin()
    {
        _times = new List<double>();
        _townTick = _townPose = 0;
        _plainFrames = _plainSites = 0;
        _tickMs = _stepMs = 0;
        _script.Clear();
        _cpu.Clear();
        _gpu.Clear();
        _prims = _draws = _objs = _shadowPrims = _shadowDraws = 0;
        _physics = 0;
        _split?.Reset();
        _allocs?.Take();
        _mainAlloc = GC.GetAllocatedBytesForCurrentThread();
        _allAlloc = GC.GetTotalAllocatedBytes();
        (_gcAt0, _gcAt1, _gcAt2) = (GC.CollectionCount(0), GC.CollectionCount(1), GC.CollectionCount(2));
        _ticksAt = Engine.GetPhysicsFrames();
        _sites0 = SiteTotals();
        if (_game?.Zone?.Wildlife is { } patch) foreach (Surface.DeepSite s in patch.Sites) s.Sim.WorstStepMs = 0;
        Wildlife.SpaceNav.WorstMs = 0;
        _stepped = _siteCreatures = _awakeBusy = _awakeNear = _awakeSeen = 0;
        _threads0 = ThreadTimes();
        _wall.Restart();
    }

    /// <summary>Each of the process's threads' CPU time so far, ms.</summary>
    static Dictionary<int, double> ThreadTimes()
    {
        var t = new Dictionary<int, double>();
        foreach (ProcessThread th in Process.GetCurrentProcess().Threads)
            try { t[th.Id] = th.TotalProcessorTime.TotalMilliseconds; }
            catch (Exception) { } // (one that ended as it was read)
        return t;
    }

    Dictionary<int, double> _threads0 = new();
    readonly Stopwatch _wall = new();

    /// <summary>The busiest threads over the stage, each as a share of one core, and the whole process in cores.</summary>
    string ThreadUse()
    {
        double wall = _wall.Elapsed.TotalMilliseconds;
        var used = ThreadTimes().Select(kv => (kv.Key, Ms: kv.Value - _threads0.GetValueOrDefault(kv.Key))).OrderByDescending(x => x.Ms).ToList();
        int main = (int)AppDomain.GetCurrentThreadId();
        return string.Create(CultureInfo.InvariantCulture, $"{used.Sum(x => x.Ms) / wall:F2} cores in all; busiest threads {string.Join(", ", used.Take(6).Select(x => $"{(x.Key == main ? "main " : "")}{100 * x.Ms / wall:F0}%"))}");
    }

    /// <summary>Every place's, cave's and underworld's creatures together: their sims' steps (brains, moving, rigs), quiet sounds and bodies, ms so far.</summary>
    double[] SiteTotals()
    {
        var t = new double[6];
        if (_game?.Zone?.Wildlife is not { } w) return t;
        foreach (Surface.DeepSite s in w.Sites)
        {
            s.Sim.Profile = true;
            t[0] += s.StepMs; t[1] += s.Sim.ThinkMs; t[2] += s.Sim.MoveMs; t[3] += s.Sim.RigMs; t[4] += s.QuietMs; t[5] += s.ViewMs;
        }
        return t;
    }

    double[] _sites0 = new double[6];
    long _stepped, _siteCreatures, _awakeBusy, _awakeNear, _awakeSeen;

    void End(string stage)
    {
        List<double> t = _times;
        _times = null;
        if (t.Count == 0) { GD.Print($"PERF {stage}: no frames"); return; }
        GD.Print(string.Create(CultureInfo.InvariantCulture, $"PERF {stage,-16} stand-ins: {_plainFrames} of {t.Count} frames with a place drawn as its plain far lump in view within 600 m ({_plainSites} place-frames)"));
        GD.Print(string.Create(CultureInfo.InvariantCulture, $"PERF {stage,-16} towns a frame: their last tick {_townTick / t.Count:F2} ms, posing {_townPose / t.Count:F2} ms"));
        _townTick = _townPose = 0;
        t.Sort();
        double P(double q) => t[Math.Min(t.Count - 1, (int)(t.Count * q))];
        int hitches = t.Count(x => x > 33.4);
        // Inside a place, the frame budget (60 fps): on average within it, and no more than one frame in a hundred late by half a frame or more.
        if (stage.StartsWith("inside") && !stage.EndsWith(" plain"))
        {
            int over = t.Count(x => x > Budget * 1.5);
            bool ok = t.Average() <= Budget * 1.05 && over <= t.Count / 100;
            GD.Print(string.Create(CultureInfo.InvariantCulture, $"PERF {(ok ? "ok" : "FAIL")} budget {stage}: avg {t.Average():F1} ms, {over} of {t.Count} frames over {Budget * 1.5:F0} ms"));
            if (!ok) _failed = true;
        }
        string line = string.Create(CultureInfo.InvariantCulture,
            $"PERF {stage,-16} frames {t.Count,4} avg {t.Average(),6:F1} p50 {P(0.5),6:F1} p95 {P(0.95),6:F1} p99 {P(0.99),6:F1} worst {t[^1],7:F1} over33 {hitches,3} | cpu {_cpu.DefaultIfEmpty(0).Average(),5:F1} gpu {_gpu.DefaultIfEmpty(0).Average(),5:F1} | prims {_prims / 1000 / t.Count,6}k draws {_draws / t.Count,5} objs {_objs / t.Count,5} shadow {_shadowPrims / 1000 / t.Count,6}k/{_shadowDraws / t.Count,5} | script {_script.DefaultIfEmpty(0).Average(),5:F1} nodes {Performance.GetMonitor(Performance.Monitor.ObjectNodeCount),6} active {Performance.GetMonitor(Performance.Monitor.Physics3DActiveObjects),4} pairs {Performance.GetMonitor(Performance.Monitor.Physics3DCollisionPairs),5}");
        GD.Print(line);
        var (bodies, shapes) = JoltWatch.Census(GetTree().Root);
        GD.Print($"PERF {stage,-16} live bodies {bodies}, shapes {shapes}, failed to create {JoltWatch.Failed}; physics process {Performance.GetMonitor(Performance.Monitor.TimePhysicsProcess) * 1000.0:F2} ms; a frame: scripts' physics ticks {_tickMs / t.Count:F2} ms, the engine's steps {_stepMs / t.Count:F2} ms");
        _tickMs = _stepMs = 0;
        GD.Print(string.Create(CultureInfo.InvariantCulture, $"PERF {stage,-16} alloc a frame {(GC.GetAllocatedBytesForCurrentThread() - _mainAlloc) / 1024.0 / t.Count,7:F1} KB main thread, {(GC.GetTotalAllocatedBytes() - _allAlloc) / 1024.0 / t.Count,7:F1} KB all threads; collections gen0 {GC.CollectionCount(0) - _gcAt0} gen1 {GC.CollectionCount(1) - _gcAt1} gen2 {GC.CollectionCount(2) - _gcAt2}; physics ticks a frame {(Engine.GetPhysicsFrames() - _ticksAt) / (double)t.Count:F2}, their callbacks worst {_physics:F1} ms; video memory {Performance.GetMonitor(Performance.Monitor.RenderVideoMemUsed) / 1048576.0:F0} MB"));
        double[] sites = SiteTotals();
        double S(int i) => (sites[i] - _sites0[i]) / t.Count;
        GD.Print(string.Create(CultureInfo.InvariantCulture, $"PERF {stage,-16} places' creatures a frame: {_stepped / (double)t.Count:F0} stepped of {_siteCreatures / (double)t.Count:F0} (awake: {_awakeBusy / (double)t.Count:F0} busy, {_awakeNear / (double)t.Count:F0} within 30 m, {_awakeSeen / (double)t.Count:F0} further); sims {S(0):F2} ms (brains {S(1):F2}, moving {S(2):F2}, rigs {S(3):F2}), quiet {S(4):F2}, bodies {S(5):F2}"));
        GD.Print($"PERF {stage,-16} cpu use: {ThreadUse()}");
        if (_game.Zone.Wildlife.Sites.MaxBy(s => s.Sim.WorstStepMs) is { } slow) GD.Print(string.Create(CultureInfo.InvariantCulture, $"PERF {stage,-16} slowest creature step {slow.Sim.WorstStepMs:F1} ms: {slow.Sim.WorstStep}"));
        GD.Print(string.Create(CultureInfo.InvariantCulture, $"PERF {stage,-16} slowest path search inside {Wildlife.SpaceNav.WorstMs:F1} ms: {Wildlife.SpaceNav.Worst}"));
        if (_split != null) GD.Print($"PERF {stage,-16} script by system, cpu ms: {_split.Report()}");
        if (_allocs != null) GD.Print($"PERF {stage,-16} allocated, by type: {_allocs.Take()}");
        foreach (string h in _hitches) GD.Print($"PERF hitch {h}");
        _hitches.Clear();
    }

    IEnumerable<int> Measure(string stage, int frames = Frames)
    {
        Begin();
        while (_times.Count < frames) yield return 0;
        End(stage);
        // --reactions=ab: the same frame again with nobody looking round for others (Sightings off), then on, twice over (so the
        // machine's own drift shows against the difference): what people's reactions to each other cost.
        if (_args.GetValueOrDefault("reactions") == "ab" && !stage.StartsWith("far") && stage != "start")
        {
            for (int k = 0; k < 4; k++)
            {
                Wildlife.Sightings.On = k % 2 == 1;
                foreach (int x in _drive.Wait(30)) yield return x;
                Begin();
                while (_times.Count < frames) yield return 0;
                End($"{stage} reactions {(k % 2 == 1 ? "on" : "off")} {k / 2 + 1}");
            }
        }
        // --ab: the same frame again with places' surfaces left as plain colour, then back: their cost, on one world.
        if (!_args.ContainsKey("ab") || stage.EndsWith(" plain") || stage.StartsWith("far") || stage == "start") yield break;
        PoiGenerator.BlockMaterials.Detail(false);
        foreach (int x in _drive.Wait(10)) yield return x;
        Begin();
        while (_times.Count < frames) yield return 0;
        End(stage + " plain");
        PoiGenerator.BlockMaterials.Detail(true);
        foreach (int x in _drive.Wait(10)) yield return x;
    }

    /// <summary>The game up and the sleeper out, with the builder done with everything in its range.</summary>
    IEnumerable<int> Stand()
    {
        foreach (int x in _drive.UntilPlaying(300)) yield return x;
        _split = new SystemSplit(_game, _game.Zone, _game.Zone.Wildlife, _game.Places) { Deeper = n => _args.ContainsKey("deeper") && n is Slice.Places.Place };
        if (_args.ContainsKey("audit")) _allocs = new AllocCensus { On = true };
        var zone = _game.Zone;
        zone.Weather.Bridge.Calm = true;
        zone.Sky.TimeOfDay = 0.4;
        zone.Weather.Sim.Snap(Weather.Model.WeatherKind.Clear);
        // From the sleeper standing, climbing out, while the place builder works (at least as long as a stage, so a world
        // built the old way is timed over the same window).
        Begin();
        var since = Stopwatch.StartNew();
        PlanetSurface.Poi.PoiBuilds builds = zone.Pois.Builds;
        foreach (int x in _drive.ClimbOut(_game.Bunker)) yield return x;
        while ((_times.Count < Frames || builds.AllInRangeMs < 0 || zone.Pois.Unshown(builds.Range) > 0) && since.Elapsed.TotalSeconds < 120) yield return 0;
        GD.Print($"PERF building: all built and drawn {since.Elapsed.TotalSeconds:F1} s after standing ({builds.Built} by the builder, {zone.Pois.Unshown(builds.Range)} still plain)");
        End(PlanetSurface.World.PlanetWorld.BuildPlacesAtLoad ? "building (old)" : "building");
    }

    IEnumerable<int> Scenario()
    {
        foreach (int x in Stand()) yield return x;
        var zone = _game.Zone;
        GD.Print($"PERF world {_game.World}; places {zone.Planet.Pois.Count}, grand {zone.Planet.Pois.Count(s => s.Family.Tags.Contains("grand"))}");
        foreach (var s in zone.Planet.Pois.OrderBy(s => _game.Places.DistanceTo(s, _game.Survivor.WorldFeet)))
            GD.Print($"PERF site {s.Family.Id} '{s.Name}' {s.State} {_game.Places.DistanceTo(s, _game.Survivor.WorldFeet):F0} m, footprint {s.Footprint.Max.X - s.Footprint.Min.X}x{s.Footprint.Max.Z - s.Footprint.Min.Z}, {(s.Built is { } b ? $"blocks {b.Blocks.CountBlocks()}" : "not built yet")}");
        foreach (int x in Measure("start", 120)) yield return x;
        if (_args.ContainsKey("audit"))
        {
            foreach (int x in Audit()) yield return x;
            yield break;
        }

        string family = _args.GetValueOrDefault("family");
        var sites = zone.Planet.Pois.Where(s => (family != null ? s.Family.Id == family : s.Family.Tags.Contains("grand")) && (s.Whole == null || s.Whole.Anchor == s)).OrderBy(s => _game.Places.DistanceTo(s, _game.Survivor.WorldFeet)).Take(int.Parse(_args.GetValueOrDefault("sites", "1")));
        foreach (PoiSite site in sites.ToList())
        {
            if (_args.ContainsKey("fly")) foreach (int x in FlyIn(site)) yield return x;
            foreach (int x in OnePlace(site)) yield return x;
        }
    }

    /// <summary>The height a craft flies in at (its speed is FlySpeed, in PlacePerf.Standing).</summary>
    const float FlyUp = 150f;
    Vector3 _flyFrom, _flyTo, _rideFrom, _rideTo;

    /// <summary>
    /// A craft at top speed flying in to a typed place from where it first comes into view (the builder's range, the far
    /// plane, or as far off as the world allows), the place held back from the builder until then: the frames on the way,
    /// and when every part of it stood built, no plain block left, against when the craft is over its edge. Wall-clock:
    /// a slow frame doesn't slow the craft. With no place, the same route again (the last one flown).
    /// </summary>
    IEnumerable<int> FlyIn(PoiSite site)
    {
        var zone = _game.Zone;
        TypedPlace whole = site?.Whole;
        double reach = 0;
        if (whole != null)
        {
            SketchBox fb = PlaceTypes.Turn(whole.Sketch.Footprint, whole.Turn);
            reach = Math.Min(zone.Pois.Builds.Range, zone.Planet.Width / 2 - fb.W - 100);
            Vector3 edge = zone.Planet.NearestCopy(new Vector3((float)(whole.X + fb.X0), whole.PadY, (float)(whole.Z + fb.Cz)), _game.Survivor.WorldFeet);
            (_flyFrom, _flyTo) = (edge - new Vector3((float)reach, 0, 0), edge);
        }
        _game.Survivor.PlaceOutside((_flyFrom + Vector3.Up * FlyUp).ToNumerics());
        foreach (int x in _drive.Wait(60)) yield return x;
        PoiBuilds.Withhold = null;
        Begin();
        var clock = Stopwatch.StartNew();
        double standAt = -1;
        float length = _flyFrom.DistanceTo(_flyTo);
        for (float along = 0f; along < length; along = FlySpeed * (float)clock.Elapsed.TotalSeconds)
        {
            Vector3 p = _flyFrom.Lerp(_flyTo, along / length);
            p.Y = zone.GroundAt(p.X, p.Z).Y + FlyUp;
            _game.Survivor.PlaceOutside(p.ToNumerics());
            _drive.Face(_flyTo);
            if (whole != null && standAt < 0 && whole.Parts.All(zone.Pois.Standing)) standAt = clock.Elapsed.TotalSeconds;
            yield return 0;
        }
        End(whole != null ? "fly in" : "fly in (none)");
        if (whole == null) yield break;
        int standing = whole.Parts.Count(zone.Pois.Standing);
        bool ok = standAt >= 0;
        GD.Print($"PERF {(ok ? "ok" : "FAIL")} fly in: {standing} of {whole.Parts.Count} parts of the {whole.Sketch.Type.Id} standing{(ok ? $" {standAt:F1} s" : "")} after it came into view {reach:F0} m out; a craft at {FlySpeed:F0} m/s is over its edge at {length / FlySpeed:F1} s");
        if (!ok) _failed = true;
    }

    /// <summary>Along the ground from one point to another at a hover bike's speed, facing a point: what the streaming costs.</summary>
    IEnumerable<int> Ride(string stage, Vector3 from, Vector3 to, Vector3 face)
    {
        const float Speed = 22f;
        var zone = _game.Zone;
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
            _drive.Face(face);
            yield return 0;
        }
        End(stage);
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
        // And by class: where the bodies, shapes, lights and people are.
        var kinds = new Dictionary<string, int>();
        void Count(Node n)
        {
            string k = n.GetType().Name;
            if (n is not Node3D n3 || n3.IsVisibleInTree() || n is CollisionObject3D or CollisionShape3D) kinds[k] = kinds.GetValueOrDefault(k) + 1;
            foreach (Node c in n.GetChildren()) Count(c);
        }
        Count(root);
        GD.Print($"PERF classes {string.Join(", ", kinds.OrderByDescending(kv => kv.Value).Take(22).Select(kv => $"{kv.Key} {kv.Value}"))}");
    }

    IEnumerable<int> OnePlace(PoiSite site)
    {
        var zone = _game.Zone;
        var places = _game.Places;
        GD.Print($"PERF place {site.Family.Id} '{site.Name}' {site.State}");
        Vector3 Centre() => zone.Planet.NearestCopy(new Vector3((site.Footprint.Min.X + site.Footprint.Max.X) * 0.5f, site.Origin.Y + 8f, (site.Footprint.Min.Z + site.Footprint.Max.Z) * 0.5f), _game.Survivor.WorldFeet);
        Vector3 Gate() => zone.Planet.NearestCopy(new Vector3(site.Entrance.X + 0.5f, site.Entrance.Y, site.Entrance.Z + 0.5f), _game.Survivor.WorldFeet);
        Int3 f = site.Layout.EntranceFacing.Vec();
        Vector3 outward = new Vector3(f.X, 0f, f.Z);
        if (outward.LengthSquared() < 0.1f) outward = Vector3.Right;

        Vector3 Out(float metres)
        {
            Vector3 p = Gate() + outward * metres;
            p.Y = zone.GroundAt(p.X, p.Z).Y + 0.3f;
            return p;
        }

        // --quick=1: straight to the gate (no far stages, no ride in).
        bool quick = _args.ContainsKey("quick");
        if (quick) _game.Survivor.PlaceOutside(Out(26f).ToNumerics());
        foreach (float d in quick ? Array.Empty<float>() : new[] { 1000f, 500f })
        {
            _game.Survivor.PlaceOutside(Out(d).ToNumerics());
            foreach (int x in _drive.Wait(60)) yield return x;
            foreach (int x in _drive.UntilSettled(30)) yield return x;
            foreach (int x in _drive.Wait(120)) yield return x;
            foreach (int x in _drive.LookAt(Centre(), 6)) yield return x;
            foreach (int x in Measure($"far {d:F0}")) yield return x;
        }

        // Riding in from a kilometre out at a hover bike's speed: what the streaming costs.
        (_rideFrom, _rideTo) = (Out(1000f), Out(8f));
        if (!quick) foreach (int x in Ride("ride in", _rideFrom, _rideTo, Centre())) yield return x;
        for (int i = 0; i < 1800 && places.PlaceOf(site) is not { Ready: true }; i++) yield return 0;

        _game.Survivor.PlaceOutside(Out(26f).ToNumerics());
        foreach (int x in _drive.Wait(30)) yield return x;
        foreach (int x in _drive.UntilSettled(30)) yield return x;
        foreach (int x in _drive.LookAt(Gate() + Vector3.Up * 3f, 6)) yield return x;
        foreach (int x in Measure("gate")) yield return x;
        Place place = places.PlaceOf(site);
        if (place == null) { GD.Print("PERF place never stood"); yield break; }
        GD.Print($"PERF built in {places.BuildLog.LastOrDefault().Seconds:F1} s, {place.ChunkCount} chunks, {place.Groups} cubes, {place.NearGroups} near, {place.Doors.Count} doors, {place.Memory.Fauna?.Sim?.Creatures.Count} creatures, {place.DecoPieces} pieces of {place.Memory.Rooms?.Values.Sum(r => r.Plan.Placements.Count)}; worst build step {places.WorstStepMs:F1} ms, worst build frame {places.WorstFrameMs:F1} ms");
        // A roofed spot well inside: the way a player would walk there.
        var reach = place.Nav().Reachable(site.Instance.Layout.EntranceCell);
        var inside = reach.Where(c => place.Roofed(place.WorldOf(c, 0.2f))).OrderBy(c => c.X + c.Z * 3).ToList();
        GD.Print($"PERF reachable {reach.Count}, roofed {inside.Count}");
        IEnumerable<int> Turn(string stage)
        {
            Begin();
            for (int i = 0; i < 360; i++)
            {
                _game.Controls.Look = new Vector2(40f, 0f);
                yield return 0;
            }
            _game.Controls.Look = Vector2.Zero;
            End(stage);
        }

        // Standing well inside on the way in's level, then (a many-storey place) on its middle and top floors: turning on the spot.
        var levels = inside.Select(c => c.Y).Distinct().OrderBy(y => y).ToList();
        int entryY = site.Instance.Layout.EntranceCell.Y;
        var stops = new List<(string Stage, Int3 Cell)>();
        if (inside.Count > 0) stops.Add(("inside", inside.Where(c => c.Y == entryY).DefaultIfEmpty(inside[inside.Count / 2]).ElementAt(0)));
        if (levels.Count > 0 && levels[^1] - levels[0] >= ManyStoreys)
            foreach (var (stage, y) in new[] { ("inside mid", levels[levels.Count / 2]), ("inside top", levels[^1]) })
            {
                var on = inside.Where(c => c.Y == y).ToList();
                stops.Add((stage, on[on.Count / 2]));
            }
        foreach (var (stage, cell) in stops)
        {
            _game.Survivor.PlaceOutside(place.WorldOf(cell, 0.1f).ToNumerics());
            foreach (int x in _drive.Wait(30)) yield return x;
            GD.Print($"PERF {stage} at {cell.Y - entryY:+0;-0} m from the way in ({levels.Count} levels, {levels.FirstOrDefault() - entryY} to {levels.LastOrDefault() - entryY} m)");
            foreach (int x in Turn(stage)) yield return x;
            if (!_args.ContainsKey("ab")) continue;
            PoiGenerator.BlockMaterials.Detail(false);
            foreach (int x in Turn(stage + " plain")) yield return x;
            PoiGenerator.BlockMaterials.Detail(true);
        }
        _game.Survivor.PlaceOutside(Out(-40f).ToNumerics());
        foreach (int x in _drive.Wait(30)) yield return x;
        foreach (int x in _drive.LookAt(Centre(), 6)) yield return x;
        foreach (int x in Measure("in the yard")) yield return x;
        Census(_game);
        if (_args.ContainsKey("toggles"))
        {
            GD.Print($"PERF sim {place.Memory.Fauna?.Sim?.Creatures.Count(c => !c.Dormant)} awake of {place.Memory.Fauna?.Sim?.Creatures.Count}, patch {zone.Wildlife.Sim?.Creatures.Count}");
            Node creatures = places.PlaceOf(site)?.GetNodeOrNull("Creatures");
            if (creatures != null) creatures.ProcessMode = ProcessModeEnum.Disabled;
            GD.Print($"PERF creatures node {creatures != null}");
            foreach (int x in Measure("gate-creatures", 120)) yield return x;
            zone.Wildlife.Frozen = true;
            foreach (int x in Measure("gate-wildlife", 120)) yield return x;
            // The animated pieces are all moved by one animator, near the eye only: stopping it stops them all.
            GD.Print($"PERF animated deco {GalHack.Deco.Engine.DecoAnimator.Count} ({GalHack.Deco.Engine.DecoAnimator.NearCount} near, {GalHack.Deco.Engine.DecoAnimator.MidCount} in the middle distance)");
            if (places.GetTree().Root.GetNodeOrNull("DecoAnimator") is Node animator) animator.ProcessMode = ProcessModeEnum.Disabled;
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

    }
}
