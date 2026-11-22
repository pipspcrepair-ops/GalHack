using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using GalHack.Physics;
using GalHack.Physics.Engine;
using Godot;
using PlanetSurface;
using PlanetSurface.Skies;
using Slice.Game;
using Slice.Orbit;
using Slice.Surface;
using Slice.Vessels;
using Weather.Model;
using N = System.Numerics;

namespace Slice.Tests;

/// <summary>
/// Round trips leave nothing behind (owner, 22 Nov: "Going from world to space to world to space to
/// world, shouldn't have any buggy after effects", and cleanup thorough from world to world). On a
/// fresh world, in the Skiff, four times over: up off the start world into orbit, out to the
/// thruster moon and down onto its surface, back up into orbit, back to the start world and down onto
/// it; and up into orbit once more at the end.
///
/// After every step, once it has settled: every body's globe is drawn at its full paint where it
/// should be (no low-detail skin), its lit side faces the sun the system has (no dark disc), the sky
/// in view agrees with the system model, exactly one sky and one sun are lit and they are the zone
/// you are in, every other zone is parked or hidden by design and plays no sound, the camera, the
/// gravity, the suit's air and the clock read the zone you are in, and nothing is held down. After a
/// forced collection: the engine's object, node, orphan and resource counts, its texture, buffer and
/// static memory, the managed heap, the physics bodies and the worker queues, each step's against the
/// same step's in the second loop, all within a small tolerance from loop 3 on (the first loop makes
/// what a first visit makes: the moon's surface, the ground streamed where you come down); and the
/// frame times.
///
/// The two cruises between the worlds are the one shortcut, printed: the Skiff is set over the
/// moon, and back over the start world, each time over the same place it first came down on, so
/// each loop's ground is the same and its figures can be laid against the first loop's.
///
/// Run windowed it is also the climb's timing: full screen, vsync, the owner's settings (--settings=path)
/// or the minimum spec's stand-in (--minspec=1: 4 cores, workers sized for 4, the lowest graphics); each
/// frame's wall, main-thread CPU and GPU time by phase (on a surface, climbing, at the switch, in orbit,
/// coming down toward a globe), and the threads' share of the run. And it takes a picture of each globe
/// from orbit after loop 1 and loop 3 (--shots=folder) and compares them.
///
/// Run: G:\GalHack\.tools\queue\test-q.ps1 -Project prototypes/slice -Scene res://tests/RoundTripTest.tscn
/// </summary>
public partial class RoundTripTest : Node
{
    const int WatchdogMinutes = 45, Loops = 4;

    /// <summary>The loop every later one is laid against: the first after everything has been visited once.</summary>
    const int Settled = 2;
    Game.Game _game;
    Driver _drive;
    Pilot _pilot;
    IEnumerator<int> _run;
    int _failures, _passes;
    readonly Stopwatch _clock = Stopwatch.StartNew();
    Vessel _skiff;
    readonly Dictionary<string, string> _args = new();
    bool Windowed => DisplayServer.GetName() != "headless";

    public override void _Ready()
    {
        new System.Threading.Thread(() =>
        {
            System.Threading.Thread.Sleep(TimeSpan.FromMinutes(WatchdogMinutes));
            Console.Error.WriteLine($"FAIL  the run took over {WatchdogMinutes} minutes: watchdog");
            System.Environment.Exit(1);
        }) { IsBackground = true }.Start();
        foreach (string arg in OS.GetCmdlineUserArgs())
        {
            int eq = arg.IndexOf('=');
            if (arg.StartsWith("--") && eq > 2) _args[arg[2..eq]] = arg[(eq + 1)..];
        }
        if (_args.ContainsKey("minspec"))
        {
            Process.GetCurrentProcess().ProcessorAffinity = 0x55;
            PlanetSurface.Terrain.TerrainStreamer.Cores = 4;
            _args.TryAdd("settings", "res://tests/minspec.cfg");
        }
        if (Windowed)
        {
            DisplayServer.WindowSetMode(DisplayServer.WindowMode.Fullscreen);
            DisplayServer.WindowSetVsyncMode(DisplayServer.VSyncMode.Enabled);
            RenderingServer.ViewportSetMeasureRenderTime(GetViewport().GetViewportRid(), true);
        }
        ProcessPhysicsPriority = 100;
        _run = Scenario().GetEnumerator();
    }

    // ---- Timing -----------------------------------------------------------------------------------

    FrameProbe _probe;
    long _last;
    bool _comingDown;
    int _crossingsSeen, _sinceCrossing = int.MaxValue;
    readonly Dictionary<string, List<(double Wall, double Cpu, double Gpu)>> _phases = new();

    /// <summary>Every drawn frame, by what is happening: on a surface, climbing to the cap, at the switch (a second either side), in orbit, coming down toward a globe.</summary>
    public override void _Process(double delta)
    {
        long now = Stopwatch.GetTimestamp();
        if (_game?.Phase != Phase.Playing || _probe == null)
        {
            _last = now;
            return;
        }
        int crossings = _game.Spaceflight.Crossings.Count;
        if (crossings != _crossingsSeen) (_crossingsSeen, _sinceCrossing) = (crossings, 0);
        else if (_sinceCrossing < int.MaxValue) _sinceCrossing++;
        float y = _game.InOrbit ? _game.Orbit.Anchor.Height(_game.Camera.GlobalPosition) : _game.Camera.GlobalPosition.Y;
        string phase = _sinceCrossing < 60 || MathF.Abs(y - _game.Surface.Physics.Zone.Ceiling) < 120f ? "at the switch"
            : _game.InOrbit ? (_comingDown ? "coming down to a globe" : "in orbit") : y > 300f ? "climbing" : "on a surface";
        if (_last != 0)
        {
            if (!_phases.TryGetValue(phase, out var list)) _phases[phase] = list = new();
            list.Add(((now - _last) * 1000.0 / Stopwatch.Frequency, _probe.LastCpuMs, RenderingServer.ViewportGetMeasuredRenderTimeGpu(GetViewport().GetViewportRid())));
        }
        _last = now;
    }

    /// <summary>The frame times by phase, mean and 99th percentile, and how the run's work spread over the threads.</summary>
    void Timing(TimeSpan cpuAtStart, double wallAtStart)
    {
        foreach (var (phase, frames) in _phases)
        {
            double P(Func<(double Wall, double Cpu, double Gpu), double> of, double q)
            {
                var sorted = frames.Select(of).OrderBy(v => v).ToList();
                return sorted[Math.Min(sorted.Count - 1, (int)(sorted.Count * q))];
            }
            GD.Print($"  timing {phase}: {frames.Count} frames; wall mean {frames.Average(f => f.Wall):F2} p99 {P(f => f.Wall, 0.99):F2} ms; main thread CPU mean {frames.Average(f => f.Cpu):F2} p99 {P(f => f.Cpu, 0.99):F2} ms; GPU mean {frames.Average(f => f.Gpu):F2} p99 {P(f => f.Gpu, 0.99):F2} ms");
            if (Windowed) Check(P(f => f.Cpu, 0.99) < 16.7 * 0.8, $"{phase}: the main thread keeps the 16.7 ms budget with headroom, p99 {P(f => f.Cpu, 0.99):F1} ms of CPU");
        }
        var process = Process.GetCurrentProcess();
        double wall = _clock.Elapsed.TotalSeconds - wallAtStart, used = (process.TotalProcessorTime - cpuAtStart).TotalSeconds;
        var busy = process.Threads.Cast<ProcessThread>().Select(t => { try { return t.TotalProcessorTime.TotalSeconds; } catch { return 0.0; } }).Where(t => t > 0.01).OrderByDescending(t => t).ToList();
        GD.Print($"  threads: the run used {used / wall:F2} cores on average over {wall:F0} s ({System.Environment.ProcessorCount} logical, affinity 0x{(long)process.ProcessorAffinity:X}); the busiest threads' CPU seconds: {string.Join(", ", busy.Take(12).Select(t => t.ToString("F1")))} ({busy.Count} threads with any)");
    }

    public override void _PhysicsProcess(double delta)
    {
        bool more;
        try { more = _run.MoveNext(); }
        catch (Exception e)
        {
            Check(false, $"scenario threw: {e}");
            more = false;
        }
        if (more) return;
        GD.Print(_failures == 0 ? $"ALL PASSED ({_passes} checks in {_clock.Elapsed.TotalSeconds:F1} s)" : $"{_failures} CHECK(S) FAILED ({_passes} passed)");
        SetPhysicsProcess(false);
        Wildlife.Shutdown.Quit(this, _failures == 0 ? 0 : 1);
    }

    void Check(bool ok, string what)
    {
        if (ok) _passes++;
        else _failures++;
        GD.Print($"  {(ok ? "ok   " : "FAIL ")} {what}");
    }

    IEnumerable<int> Scenario()
    {
        _game = new Game.Game { Interactive = false, SkipIntro = true, SettingsPath = _args.GetValueOrDefault("settings"), World = WorldChoice.FromArgs(OS.GetCmdlineUserArgs(), WorldChoice.Fresh()) };
        AddChild(_game);
        _game.Spaceflight.LedgerFolder = System.IO.Path.Combine(OS.GetUserDataDir(), "round-trip-zones");
        _drive = new Driver(_game);
        _pilot = new Pilot(_game, _drive);
        foreach (int x in _drive.UntilPlaying()) yield return x;
        _probe = new FrameProbe(this);
        TimeSpan cpuAtStart = Process.GetCurrentProcess().TotalProcessorTime;
        double wallAtStart = _clock.Elapsed.TotalSeconds;
        _game.Zone.Weather.Bridge.Calm = true;
        _game.Zone.Weather.Sim.Snap(WeatherKind.Clear);
        _game.Zone.Wildlife.Frozen = true;
        GD.Print($"world: {_game.World}");
        foreach (int x in _pilot.BuildSkiff(s => _skiff = s)) yield return x;
        AirCells.Give(_game, 16);
        Check(_skiff?.Structure.Count == _game.StarterPlan.Count, $"the Skiff is built: {_skiff?.Structure.Count} of {_game.StarterPlan.Count} blocks");
        if (_skiff?.Structure.Count != _game.StarterPlan.Count) yield break;
        foreach (int x in _pilot.Board(_skiff)) yield return x;
        if (_game.Survivor.Seated != _skiff) yield break;
        for (int loop = 1; loop <= Loops; loop++)
        {
            GD.Print($"-- loop {loop} --");
            foreach (int x in Up()) yield return x;
            if (!_game.InOrbit) yield break;
            foreach (int x in Settle(loop, "orbit over the start world")) yield return x;
            Celestial moon = null;
            foreach (int x in ToTheMoon(m => moon = m)) yield return x;
            if (moon == null) yield break;
            foreach (int x in Down(moon)) yield return x;
            if (_game.Zone != moon.Surface) yield break;
            foreach (int x in Settle(loop, "on the moon")) yield return x;
            foreach (int x in Up()) yield return x;
            if (!_game.InOrbit) yield break;
            foreach (int x in Settle(loop, "orbit over the moon")) yield return x;
            foreach (int x in BackHome()) yield return x;
            foreach (int x in Down(_game.Orbit.Home)) yield return x;
            if (_game.Zone != _game.Surface) yield break;
            foreach (int x in Settle(loop, "on the start world")) yield return x;
        }
        foreach (int x in Up()) yield return x;
        if (_game.InOrbit) foreach (int x in Settle(Loops + 1, "orbit over the start world")) yield return x;
        Levels();
        Timing(cpuAtStart, wallAtStart);
    }

    // ---- The flights ----------------------------------------------------------------------------

    /// <summary>Straight up through the cap, and on past the descent's band into the sphere's own terms.</summary>
    IEnumerable<int> Up()
    {
        int crossings = _game.Spaceflight.Crossings.Count;
        _game.Controls.Hold(Key.Space);
        for (int i = 0; i < 60 * 150 && !_game.InOrbit; i++) yield return 0;
        for (int i = 0; i < 60 * 60 && !_game.Spaceflight.PastBand; i++) yield return 0;
        _game.Controls.ReleaseAll();
        foreach (int x in _pilot.Stop()) yield return x;
        Check(_game.Spaceflight.PastBand && _game.Spaceflight.Crossings.Count == crossings + 1 && _game.Spaceflight.Crossings[^1].Audit.Count == 0,
            $"up through the cap into orbit, over {_game.Orbit?.Anchor?.Name}, nothing lost or doubled ({string.Join("; ", _game.Spaceflight.Crossings[^1].Audit)})");
    }

    /// <summary>Out to the thruster moon (the cruise skipped, printed), and over it a little past its band.</summary>
    IEnumerable<int> ToTheMoon(Action<Celestial> found)
    {
        OrbitZone orbit = _game.Orbit;
        for (int i = 0; i < 60 * 300 && (orbit.Moon?.Ready != true || _game.Spaceflight.MoonSurface?.Made == null); i++) yield return 0;
        Celestial moon = orbit.Moon;
        if (moon?.Ready != true)
        {
            Check(false, "the thruster moon's globe and surface were made");
            yield break;
        }
        Vessel v = _skiff;
        // Over the place it first came down on, or the near side to begin with.
        _moonAt ??= moon.Map.MapPoint(moon.ToGlobe(v.Body.Position));
        var (tx, tz) = _moonAt.Value;
        Vector3 Above() => moon.ToWorld(moon.Map.Direction(tx, tz) * moon.RadiusFor(moon.Ceiling + orbit.Rules.Crossing.Band + 400f)).ToGodot();
        GD.Print($"  (shortcut: the cruise out to {moon.Name} skipped: the Skiff set over its map point ({tx:F0}, {tz:F0}), {moon.Height(Above()):F0} m up)");
        Place(v, Above());
        for (int i = 0; i < 60 * 300 && !(orbit.Anchor == moon && moon.Surface.Arrived); i++) yield return 0;
        foreach (int x in _pilot.FlyTo(v, Above, 30f, 400f)) yield return x;
        Check(orbit.Anchor == moon && moon.Surface.Arrived, $"{moon.Name} took over as the frame, its surface there for the descent");
        found(moon);
    }

    (double X, double Z)? _moonAt;

    /// <summary>Back over the start world where it first left it (the cruise skipped, printed).</summary>
    IEnumerable<int> BackHome()
    {
        OrbitZone orbit = _game.Orbit;
        Celestial home = orbit.Home;
        Crossing first = _game.Spaceflight.Crossings[0];
        Vector3 over = home.ToWorld(home.Map.Direction(first.MapX, first.MapZ) * home.RadiusFor(home.Ceiling + orbit.Rules.Crossing.Band + 600f)).ToGodot();
        GD.Print($"  (shortcut: the cruise back skipped: the Skiff set {home.Height(over):F0} m over the start world, over the place it first left)");
        Place(_skiff, over);
        for (int i = 0; i < 60 * 10 && orbit.Anchor != home; i++) yield return 0;
        Check(orbit.Anchor == home, "the start world took the frame back");
    }

    /// <summary>Down through a body's cap at a pilot's pace, onto its surface, stopped there.</summary>
    IEnumerable<int> Down(Celestial body)
    {
        Vessel v = _skiff;
        foreach (int x in MoonTest.Upright(_pilot, body, v)) yield return x;
        int crossings = _game.Spaceflight.Crossings.Count;
        _comingDown = true;
        for (int i = 0; i < 60 * 120 && _game.Spaceflight.Crossings.Count == crossings; i++)
        {
            _game.Controls.Hold(Key.Ctrl, -v.Body.Velocity.ToGodot().Dot((v.Body.Position.ToGodot() - body.Centre).Normalized()) < 40f);
            yield return 0;
        }
        _comingDown = false;
        _game.Controls.ReleaseAll();
        foreach (int x in _pilot.Stop()) yield return x;
        Check(_game.Zone == body.Surface && v.Zone == body.Surface && _game.Spaceflight.Crossings.Count == crossings + 1 && _game.Spaceflight.Crossings[^1].Audit.Count == 0,
            $"down through {body.Name}'s cap onto its surface, in the seat, nothing lost or doubled");
    }

    static void Place(Vessel v, Vector3 at)
    {
        v.Body.Position = at.ToNumerics();
        v.Body.Velocity = N.Vector3.Zero;
        v.Body.AngularVelocity = N.Vector3.Zero;
        v.Binding.Teleport();
    }

    // ---- After every step -----------------------------------------------------------------------

    /// <summary>What the engine and the game hold after a step, once collected.</summary>
    sealed record Level(int Loop, string Step, double Objects, double Nodes, double Orphans, double Resources, double TextureMb, double BufferMb,
        double StaticMb, double ManagedMb, int Bodies, int Pending, int Threads, double P50, double P99);

    readonly List<Level> _levels = new();

    /// <summary>A step settles for a few seconds (its frame times measured), then everything is checked and counted.</summary>
    IEnumerable<int> Settle(int loop, string step)
    {
        var frames = new List<double>();
        for (int i = 0; i < 240; i++)
        {
            if (i >= 60) frames.Add(_probe.LastCpuMs);
            yield return 0;
        }
        frames.Sort();
        Anomalies(step);
        Globes(step, loop);
        TheSky(step);
        if (Windowed && (loop == 1 || loop == Loops - 1) && _game.InOrbit) foreach (int x in Pictures(loop, step)) yield return x;
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        double Mb(Performance.Monitor m) => Performance.GetMonitor(m) / 1048576.0;
        var zones = Zones().ToList();
        var level = new Level(loop, step,
            Performance.GetMonitor(Performance.Monitor.ObjectCount), Performance.GetMonitor(Performance.Monitor.ObjectNodeCount),
            Performance.GetMonitor(Performance.Monitor.ObjectOrphanNodeCount), Performance.GetMonitor(Performance.Monitor.ObjectResourceCount),
            Mb(Performance.Monitor.RenderTextureMemUsed), Mb(Performance.Monitor.RenderBufferMemUsed), Mb(Performance.Monitor.MemoryStatic),
            GC.GetTotalMemory(true) / 1048576.0, zones.Sum(z => z.Physics?.Bodies.Count ?? 0),
            (int)System.Threading.ThreadPool.PendingWorkItemCount, System.Threading.ThreadPool.ThreadCount,
            frames.Count > 0 ? frames[frames.Count / 2] : 0, frames.Count > 0 ? frames[(int)(frames.Count * 0.99)] : 0);
        _levels.Add(level);
        GD.Print($"  (after {step}, loop {loop}: {Row(level)})");
    }

    IEnumerable<SurfaceZone> Zones() => _game.GetChildren().OfType<SurfaceZone>();

    /// <summary>Everything that changes with the zone reads the zone you are in, never the one you left.</summary>
    void Anomalies(string step)
    {
        SurfaceZone here = _game.Zone;
        OrbitZone orbit = _game.Orbit;
        bool space = here is OrbitZone;
        // Every other zone parked by design (out of the physics and hidden): the start world's, the moons' and the orbit
        // (drawn on a layer of its own, which the camera sees only in space).
        bool seen(SurfaceZone z) => z is OrbitZone ? _game.Camera.GetCullMaskValue(Spaceflight.SpaceLayer) : z.Visible;
        var awake = Zones().Where(z => z != here && (z.ProcessMode != ProcessModeEnum.Disabled || seen(z))).Select(z => z.Name.ToString()).ToList();
        Check(awake.Count == 0 && here.ProcessMode != ProcessModeEnum.Disabled, $"{step}: in {here.Name}, every other zone parked and hidden ({string.Join(", ", Zones().Where(z => z != here).Select(z => z.Name.ToString()))}){(awake.Count > 0 ? "; awake: " + string.Join(", ", awake) : "")}");
        // One sky lit, and it is this zone's.
        var skies = _game.FindChildren("*", "WorldEnvironment", true, false).OfType<WorldEnvironment>().Where(w => w.Environment != null && w.GetViewport() == GetViewport()).ToList();
        Godot.Environment want = space ? orbit.Space.Environment : here.Sky.Environment;
        Check(skies.Count == 1 && skies[0].Environment == want, $"{step}: one sky drawn, this zone's ({skies.Count} environments set: {string.Join(", ", skies.Select(w => w.GetPath().ToString()))})");
        // Every sun or night light that lights anything is this zone's.
        var lights = _game.FindChildren("*", "DirectionalLight3D", true, false).OfType<DirectionalLight3D>()
            .Where(l => l.GetViewport() == GetViewport() && l.IsVisibleInTree() && l.LightEnergy > 0f && l.SkyMode != DirectionalLight3D.SkyModeEnum.SkyOnly).ToList();
        bool eclipsed = space && orbit.Space.SunLit < 0.01f;
        Check((lights.Count > 0 || eclipsed) && lights.All(l => here.IsAncestorOf(l)), $"{step}: the lights that light the scene are this zone's ({string.Join(", ", lights.Select(l => $"{l.GetPath()} {l.LightEnergy:F2}"))}{(eclipsed ? "; the sun eclipsed from here" : "")})");
        // No sound plays from a zone you left.
        var heard = _game.FindChildren("*", "", true, false).Where(n => n is AudioStreamPlayer { Playing: true, StreamPaused: false } or AudioStreamPlayer3D { Playing: true, StreamPaused: false })
            .Where(n => n.CanProcess() && Zones().Any(z => z != here && z.IsAncestorOf(n))).Select(n => n.GetPath().ToString()).ToList();
        Check(heard.Count == 0, $"{step}: nothing plays from a zone you left{(heard.Count > 0 ? ": " + string.Join(", ", heard.Take(4)) : "")}");
        // The camera, the gravity, the suit's air and the clock: this zone's.
        float far = _game.Camera.Far;
        bool spaceSeen = _game.Camera.GetCullMaskValue(Spaceflight.SpaceLayer);
        Check((space ? far == orbit.Rules.Space.CameraFar : far < orbit.Rules.Space.CameraFar) && spaceSeen == space,
            $"{step}: the camera sees as far as {(space ? "space" : "a surface")} does ({far:F0} m), and sees what is drawn in space {(spaceSeen ? "too" : "not at all")}");
        Check(_skiff.Zone == here && _game.Survivor.Zone == here && _game.Survivor.Seated == _skiff && (space ? here.Physics.Zone.Strength == 0f : here.Physics.Zone.Strength > 0f),
            $"{step}: the Skiff and the sleeper in it are in this zone, under its gravity ({here.Physics.Zone.Strength:F1} m/s²)");
        bool vacuum = space || here.Planet.Sky.Density < SurfaceWeather.NoAir;
        Check(vacuum == (_game.Suit.Beyond != null), $"{step}: the suit breathes {(vacuum ? "vacuum" : "the world's air")} outside ({(_game.Suit.Beyond != null ? "space" : "air")})");
        double days = here.Sky?.Days ?? orbit.Days();
        var clocks = Zones().Where(z => z.Sky != null && z.Arrived).Select(z => z.Sky.Days).ToList();
        Check(clocks.All(d => Math.Abs(d - days) < 1e-6), $"{step}: one clock in every sky ({string.Join(", ", clocks.Select(d => d.ToString("F5")))})");
        Check(!new[] { Key.W, Key.A, Key.S, Key.D, Key.Space, Key.Ctrl, Key.Shift }.Any(_game.Controls.IsHeld), $"{step}: nothing held down after the crossing");
    }

    /// <summary>In space every body near enough is drawn by its own globe at its full paint, lit from the sun the system has.</summary>
    void Globes(string step, int loop)
    {
        OrbitZone orbit = _game.Orbit;
        if (!_game.InOrbit)
        {
            // From a surface, the sky draws every other body in its own far look, painted (a headless run paints none).
            SkyRig sky = _game.Zone.Sky;
            Check(orbit.Bodies.All(c => c.Globe == null || !c.Globe.Visible), $"{step}: no globe drawn from the ground");
            if (!Windowed) return;
            var plain = sky.Drawn.Where(d => d.Body.Spec != null && sky.Surfaces.LayerOf(d.Body) < 0).Select(d => d.Body.Name).ToList();
            Check(sky.Surfaces.Finished && plain.Count == 0, $"{step}: the sky draws every world in view in its own paint ({sky.Surfaces.Done} of {sky.Surfaces.Wanted} painted{(plain.Count > 0 ? "; plain: " + string.Join(", ", plain) : "")})");
            return;
        }
        Vector3 eye = _game.Camera.GlobalPosition;
        StarSystem model = _game.Surface.Planet.StarSystem;
        N.Vector3 inHome = orbit.Home.ToGlobe(eye.ToNumerics());
        Vector3 toSun = (orbit.Home.Turn.ToBasis() * V(model.See(model.Star, _game.Surface.Sky.Days, new D3(inHome.X, inHome.Y, inHome.Z)).Direction)).Normalized();
        foreach (Celestial c in orbit.Bodies.Where(c => c.Ready))
        {
            float ground = eye.DistanceTo(c.Centre) - c.GlobeRadius;
            bool near = c.IsHome || c == orbit.Anchor || ground < orbit.Rules.Space.MoonGlobeFrom[1];
            if (!near) continue;
            int want = GlobePaint.WidthFor(c.Map.Width, orbit.Rules.Globe);
            Check(c.Globe.Visible && c.Globe.Detail == want && (c.IsHome || orbit.Space.FadeOf(c.Body) < 0.002f),
                $"{step}: {c.Name} is its own globe at full paint, {c.Globe.Detail} of {want} texels round, its far look {(c.IsHome ? "none" : orbit.Space.FadeOf(c.Body).ToString("P0"))}, {ground / 1000:F1} km off");
            float sun = orbit.Space.SunEnergy * orbit.Space.SunLit;
            Check(c.Globe.LitFrom.AngleTo(toSun) < Mathf.DegToRad(0.5f) && MathF.Abs(orbit.Space.Sun.LightEnergy - sun) < 0.01f && orbit.Space.Sun.GlobalBasis.Z.AngleTo(toSun) < Mathf.DegToRad(0.5f),
                $"{step}: {c.Name}'s lit side faces the sun the system has ({Mathf.RadToDeg(c.Globe.LitFrom.AngleTo(toSun)):F2} degrees off; space's sun {orbit.Space.Sun.LightEnergy:F2} strong, {orbit.Space.SunLit:P0} of it in sight from here)");
        }
    }

    /// <summary>The sky in view draws each body where the system has it from your eye, on whichever body you are.</summary>
    void TheSky(string step)
    {
        OrbitZone o = _game.Orbit;
        Vector3 cam = _game.Camera.GlobalPosition;
        StarSystem model;
        N.Quaternion toGlobe;
        N.Vector3 eye;
        IEnumerable<(Orbiter Body, Vector3 Direction, float Angular)> drawn;
        double days;
        if (_game.InOrbit)
        {
            model = _game.Surface.Planet.StarSystem;
            toGlobe = N.Quaternion.Conjugate(o.Home.Turn);
            eye = o.Home.ToGlobe(cam.ToNumerics());
            drawn = o.Space.Drawn.Where(d => o.Space.FadeOf(d.Body) > 0.002f).ToList(); // a body near enough is its own globe instead (checked above)
            days = _game.Surface.Sky.Days;
        }
        else
        {
            SurfaceZone z = _game.Zone;
            var map = new GlobeMap(z.Planet.Width, z.Planet.Height, z.Planet.Width / Math.Tau);
            double x = Planets.Gen.Mathx.WrapX(cam.X, z.Planet.Width), y = Planets.Gen.Mathx.Wrap(cam.Z, z.Planet.Height);
            model = z.Planet.StarSystem;
            toGlobe = map.Frame(x, y);
            eye = map.Direction(x, y) * (float)(map.Radius + cam.Y);
            drawn = z.Sky.Drawn.ToList();
            days = z.Sky.Days;
        }
        var d3 = new D3(eye.X, eye.Y, eye.Z);
        double worst = 0, size = 0;
        string at = "";
        int n = 0;
        foreach (var (body, direction, angular) in drawn)
        {
            Sighting s = model.See(body, days, d3);
            double off = N.Vector3.Transform(direction.ToNumerics(), toGlobe).ToGodot().Normalized().AngleTo(V(s.Direction)) * 180 / Math.PI;
            if (off > worst) (worst, at) = (off, body.Name);
            size = Math.Max(size, Math.Abs(angular - s.Angular) / s.Angular);
            n++;
        }
        Check(n > 0 && worst < 0.3 && size < 0.02, $"{step}: the sky draws all {n} bodies where the system has them from here, {worst:F3} degrees off at worst ({at}), at their sizes within {size:P1}");
    }

    static Vector3 V(D3 d) => new((float)d.X, (float)d.Y, (float)d.Z);

    // ---- Pictures --------------------------------------------------------------------------------

    /// <summary>Each globe in view, looked at straight on from the Skiff, saved; the lit side's brightness and its detail kept to compare.</summary>
    readonly Dictionary<string, (float Lit, float Detail)> _seen = new();

    IEnumerable<int> Pictures(int loop, string step)
    {
        string folder = _args.GetValueOrDefault("shots", System.IO.Path.Combine(OS.GetUserDataDir(), "round-trip"));
        System.IO.Directory.CreateDirectory(folder);
        OrbitZone orbit = _game.Orbit;
        foreach (Celestial c in orbit.Bodies.Where(c => c.Ready && c.Globe.Visible))
        {
            Vector3 eye = _skiff.Body.Position.ToGodot() + (_skiff.Body.Position.ToGodot() - c.Centre).Normalized() * 30f;
            Vector3 to = c.Centre - eye;
            _game.FixedCamera = new Transform3D(Basis.LookingAt(to, MathF.Abs(to.Normalized().Y) > 0.95f ? Vector3.Forward : Vector3.Up), eye);
            for (int i = 0; i < 20; i++) yield return 0;
            Image image = _game.GetViewport().GetTexture().GetImage();
            string tag = $"{step.Replace(" ", "-")}-{c.Name.Replace(" ", "")}-loop{loop}";
            image.SavePng(System.IO.Path.Combine(folder, tag + ".png"));
            var (lit, detail) = Looks(image);
            string key = $"{step}/{c.Name}";
            GD.Print($"  (picture {tag}: the lit part's brightness {lit:F3}, its detail {detail:F4})");
            if (loop == 1) _seen[key] = (lit, detail);
            else if (_seen.TryGetValue(key, out var first))
                Check(lit > 0.02f && MathF.Abs(lit - first.Lit) <= 0.15f * first.Lit + 0.01f && detail >= first.Detail * 0.8f,
                    $"{step}: {c.Name} after loop {loop} looks as it did after loop 1: lit {lit:F3} against {first.Lit:F3}, detail {detail:F4} against {first.Detail:F4}");
        }
        _game.FixedCamera = null;
    }

    /// <summary>A frame's lit part (its brightest half, by the median) and its detail there (the mean step between neighbouring pixels).</summary>
    static (float Lit, float Detail) Looks(Image image)
    {
        int w = image.GetWidth(), h = image.GetHeight();
        var lum = new List<float>();
        float steps = 0f;
        int counted = 0;
        for (int y = h / 4; y < h * 3 / 4; y += 2)
            for (int x = w / 4; x < w * 3 / 4; x += 2)
            {
                Color c = image.GetPixel(x, y), r = image.GetPixel(x + 1, y);
                float l = 0.2126f * c.R + 0.7152f * c.G + 0.0722f * c.B;
                lum.Add(l);
                if (l > 0.05f)
                {
                    steps += MathF.Abs(l - (0.2126f * r.R + 0.7152f * r.G + 0.0722f * r.B));
                    counted++;
                }
            }
        lum.Sort();
        return (lum[(int)(lum.Count * 0.75f)], counted > 0 ? steps / counted : 0f);
    }

    // ---- Levels -----------------------------------------------------------------------------------

    static string Row(Level l) =>
        $"{l.Objects:F0} objects, {l.Nodes:F0} nodes, {l.Orphans:F0} orphans, {l.Resources:F0} resources, textures {l.TextureMb:F1} MB, buffers {l.BufferMb:F1} MB, static {l.StaticMb:F1} MB, managed {l.ManagedMb:F1} MB, {l.Bodies} bodies, {l.Pending} pool items waiting on {l.Threads} threads, frames {l.P50:F1} / {l.P99:F1} ms";

    /// <summary>From loop 3 on, every step's figures back at the same step's in loop 2, within a small tolerance.</summary>
    void Levels()
    {
        GD.Print("  loop | step | objects | nodes | orphans | resources | textures MB | buffers MB | static MB | managed MB | bodies | pool waiting | threads | frame p50 / p99 ms");
        foreach (Level l in _levels)
            GD.Print($"  {l.Loop} | {l.Step} | {l.Objects:F0} | {l.Nodes:F0} | {l.Orphans:F0} | {l.Resources:F0} | {l.TextureMb:F1} | {l.BufferMb:F1} | {l.StaticMb:F1} | {l.ManagedMb:F1} | {l.Bodies} | {l.Pending} | {l.Threads} | {l.P50:F1} / {l.P99:F1}");
        foreach (Level l in _levels.Where(l => l.Loop > Settled))
        {
            Level first = _levels.FirstOrDefault(f => f.Loop == Settled && f.Step == l.Step);
            if (first == null) continue;
            var drifts = new List<string>();
            void Near(string what, double now, double was, double share, double plus)
            {
                if (now > was * (1 + share) + plus) drifts.Add($"{what} {was:F1} to {now:F1}");
            }
            Near("objects", l.Objects, first.Objects, 0.02, 200);
            Near("nodes", l.Nodes, first.Nodes, 0.02, 50);
            Near("orphan nodes", l.Orphans, first.Orphans, 0, 5);
            Near("resources", l.Resources, first.Resources, 0.02, 50);
            Near("texture memory", l.TextureMb, first.TextureMb, 0.02, 4);
            Near("buffer memory", l.BufferMb, first.BufferMb, 0.02, 4);
            Near("static memory", l.StaticMb, first.StaticMb, 0.05, 16);
            Near("managed heap", l.ManagedMb, first.ManagedMb, 0.1, 24);
            Near("physics bodies", l.Bodies, first.Bodies, 0, 2);
            Near("pool items waiting", l.Pending, first.Pending, 0, 8);
            Check(drifts.Count == 0, $"loop {l.Loop}, {l.Step}: everything back at loop {Settled}'s level{(drifts.Count > 0 ? ": " + string.Join(", ", drifts) : "")}");
            Check(l.P99 <= first.P99 * 1.3 + 3, $"loop {l.Loop}, {l.Step}: frame times level, p99 {l.P99:F1} ms against {first.P99:F1} (p50 {l.P50:F1} against {first.P50:F1}; CPU time, headless, machine shared)");
        }
    }
}
