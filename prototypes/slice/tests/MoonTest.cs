using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using GalHack.Physics;
using GalHack.Physics.Engine;
using Godot;
using PlanetSurface;
using Slice.Game;
using Slice.Items;
using Slice.Orbit;
using Slice.Surface;
using Slice.Vessels;
using Weather.Model;
using N = System.Numerics;

namespace Slice.Tests;

/// <summary>
/// To the moon and down, headless, by the game's own controls: the castaway builds the Skiff, flies
/// it up out of the atmosphere, on out of high orbit to the start world's thruster moon (Empyrion's
/// distances: a minute or two at a small vessel's top speed), the moon taking over as the frame it
/// flies in on the way, descends through the moon's own crossing onto its generated surface, and
/// lands there on its gear. No loading screen anywhere: every crossing is the game's seamless one.
///
/// It checks the system model's promise (a moon within thruster reach, a vacuum world), the flight
/// readout (no world altitude in open space: speed and the nearest body), that the moon never
/// vanishes on the way (its far look hands over to its own globe), the frame hand-over, the
/// crossing's mapping, the landing, the moon's world (its gravity, no air or weather, its sky the
/// system seen from it), the climb back off it, and frame times on the way out.
///
/// Setting up is the one shortcut, printed when it happens: the Skiff's parts are handed over and
/// the tech tree opened. Fresh world every run.
///
/// Run: G:\GalHack\.tools\queue\test-q.ps1 -Project prototypes/slice -Scene res://tests/MoonTest.tscn
/// </summary>
public partial class MoonTest : Node
{
    const int WatchdogMinutes = 16;
    Game.Game _game;
    Driver _drive;
    Pilot _pilot;
    IEnumerator<int> _run;
    int _failures, _passes;
    readonly Stopwatch _clock = Stopwatch.StartNew();
    Vessel _skiff;

    public override void _Ready()
    {
        var thread = new System.Threading.Thread(() =>
        {
            System.Threading.Thread.Sleep(TimeSpan.FromMinutes(WatchdogMinutes));
            System.Console.Error.WriteLine($"FAIL  the run took over {WatchdogMinutes} minutes: watchdog");
            System.Environment.Exit(1);
        }) { IsBackground = true };
        thread.Start();
        ProcessPhysicsPriority = 100;
        _run = Scenario().GetEnumerator();
    }

    public override void _PhysicsProcess(double delta)
    {
        bool more;
        try { more = _run.MoveNext(); }
        catch (Exception e)
        {
            Fail($"scenario threw: {e}");
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

    void Fail(string what) => Check(false, what);

    IEnumerable<int> Scenario()
    {
        _game = new Game.Game { Interactive = false, SkipIntro = true, World = WorldChoice.FromArgs(OS.GetCmdlineUserArgs(), WorldChoice.Fresh()) };
        AddChild(_game);
        _game.Spaceflight.LedgerFolder = System.IO.Path.Combine(OS.GetUserDataDir(), "moon-test-zones");
        _drive = new Driver(_game);
        _pilot = new Pilot(_game, _drive);
        foreach (int x in _drive.UntilPlaying()) yield return x;
        SurfaceZone zone = _game.Zone;
        zone.Weather.Bridge.Calm = true;
        zone.Weather.Sim.Snap(WeatherKind.Clear);
        zone.Wildlife.Frozen = true;
        GD.Print($"world: {_game.World}");

        TheSystem();
        foreach (int x in BuildSkiff()) yield return x;
        if (_skiff == null) yield break;
        foreach (int x in ClimbOut()) yield return x;
        if (!_game.InOrbit) yield break;
        foreach (int x in ToTheMoon()) yield return x;
        if (_game.Orbit.Anchor != _game.Orbit.Moon) yield break;
        foreach (int x in Descent()) yield return x;
        if (_game.Zone != _game.Spaceflight.MoonSurface) yield break;
        foreach (int x in Landing()) yield return x;
        foreach (int x in BackUp()) yield return x;
    }

    // ---- The system's promise -------------------------------------------------------------------

    void TheSystem()
    {
        GD.Print("-- the moon in the system --");
        var model = _game.Surface.Planet.StarSystem;
        var moon = model.FuelMoon;
        double centre = (model.Position(moon, 0) - model.Position(model.Home, 0)).Length;
        double home = model.Home.Radius;
        double gap = centre - home - (_game.Surface.Physics.Zone.Ceiling + OrbitRules.Shared.Crossing.Band) - moon.Radius;
        Check(model.StartFault() == null, $"a start system: {model.StartFault() ?? "everything it promises"}");
        Check(centre <= home * PlanetSurface.Skies.StarSystem.ThrusterMoonTo * 1.01 && gap is > 6000 and < 14000,
            $"the thruster moon is {moon.Name}, {centre / 1000:F1} km from the world's centre ({centre / home:F1} radii): {gap / 1000:F1} km of open space from the top of the climb to its ground, Empyrion's 8.3 to 12.3 km");
        Check(moon.Spec != null && moon.Spec.Airless, $"it is a vacuum world you can land on: {moon.Spec}");
    }

    // ---- Setting up ----------------------------------------------------------------------------

    IEnumerable<int> BuildSkiff()
    {
        GD.Print("-- the Skiff --");
        foreach (int x in _pilot.BuildSkiff(s => _skiff = s)) yield return x;
        Check(_skiff?.Structure.Count == _game.StarterPlan.Count, $"the Skiff is built: {_skiff?.Structure.Count} of {_game.StarterPlan.Count} blocks");
        if (_skiff?.Structure.Count != _game.StarterPlan.Count) _skiff = null;
    }

    // ---- Up and out ----------------------------------------------------------------------------

    IEnumerable<int> ClimbOut()
    {
        GD.Print("-- up out of the atmosphere --");
        Vessel v = _skiff;
        foreach (int x in _pilot.Board(v)) yield return x;
        Check(_game.Survivor.Seated == v, "at the Skiff's controls");
        _game.Controls.Hold(Key.Space);
        for (int i = 0; i < 60 * 150 && !_game.InOrbit; i++) yield return 0;
        Check(_game.InOrbit, "it crossed into orbit");
        // On up through the band, the readout the world's altitude the whole way.
        bool altInBand = true;
        for (int i = 0; i < 60 * 60 && _game.InOrbit && _game.Spaceflight.Band < 1f; i++)
        {
            altInBand &= _game.Spaceflight.FlightLine(v).Contains("ALT");
            yield return 0;
        }
        for (int i = 0; i < 60 * 8; i++) yield return 0; // well clear of the top
        _game.Controls.ReleaseAll();
        foreach (int x in _pilot.Stop()) yield return x;
        Check(altInBand, "near the world the flight readout gives its altitude");
        string line = _game.Spaceflight.FlightLine(v);
        Check(!line.Contains("ALT") && line.Contains("m/s"), $"over the band's top it gives no world altitude, only speed and the nearest body: '{line}'");
    }

    // ---- Out to the moon -----------------------------------------------------------------------

    IEnumerable<int> ToTheMoon()
    {
        GD.Print("-- out to the moon --");
        Vessel v = _skiff;
        OrbitZone orbit = _game.Orbit;
        for (int i = 0; i < 60 * 180 && (orbit.Moon?.Ready != true || _game.Spaceflight.MoonSurface?.Made == null); i++) yield return 0;
        Celestial moon = orbit.Moon;
        Check(moon?.Ready == true && _game.Spaceflight.MoonSurface?.Made != null, $"the moon's surface was made off the main thread while you played, and its globe painted from it ({_clock.Elapsed.TotalSeconds:F0} s in)");
        if (moon?.Ready != true) yield break;
        // Over the moon's near side, a little over the top of its band.
        var (tx, tz) = moon.Map.MapPoint(moon.ToGlobe(v.Body.Position));
        float over = moon.Ceiling + orbit.Rules.Crossing.Band + 400f;
        Vector3 Above() => moon.ToWorld(moon.Map.Direction(tx, tz) * moon.RadiusFor(over)).ToGodot();
        float start = v.Body.Position.ToGodot().DistanceTo(Above());
        GD.Print($"  the moon is {start / 1000:F1} km off ({moon.Name}: radius {moon.GlobeRadius:F0} m)");
        var probe = new FrameProbe(this);
        var frames = new List<double>();
        bool shown = true, nearestMoon = false;
        string lost = null;
        int handedOverAt = -1, tick = 0;
        float topSpeed = 0f;
        var watch = Stopwatch.StartNew();
        IEnumerator<int> fly = _pilot.FlyTo(v, Above, 30f, 400f).GetEnumerator();
        while (fly.MoveNext())
        {
            tick++;
            frames.Add(probe.LastMs);
            topSpeed = MathF.Max(topSpeed, v.Body.Velocity.Length());
            if (!_game.Spaceflight.MoonInView(out string why) && lost == null) { shown = false; lost = why; }
            nearestMoon |= _game.Spaceflight.FlightLine(v).Contains(moon.Name.ToUpperInvariant());
            if (handedOverAt < 0 && orbit.Anchor == moon) handedOverAt = tick;
            yield return fly.Current;
        }
        double seconds = watch.Elapsed.TotalSeconds;
        probe.Remove();
        frames.Sort();
        double p95 = frames.Count == 0 ? 0 : frames[(int)(frames.Count * 0.95)], worst = frames.Count == 0 ? 0 : frames[^1];
        GD.Print($"  frame times on the way: median {(frames.Count == 0 ? 0 : frames[frames.Count / 2]):F1} ms, 95th {p95:F1} ms, worst {worst:F1} ms over {frames.Count} frames (headless, machine shared with other runs)");
        Check(v.Body.Position.ToGodot().DistanceTo(Above()) < 40f, $"flew {start / 1000:F1} km from high orbit to over the moon in {tick / 60f:F0} s of game time at up to {topSpeed:F0} m/s ({v.Body.Position.ToGodot().DistanceTo(Above()):F0} m off the point)");
        Check(handedOverAt > 0 && orbit.Anchor == moon, $"the moon took over as the frame on the way ({handedOverAt / 60f:F0} s out), so its ground waits where you see it");
        Check(shown, $"the moon was in view every frame of the way, its far look handing over to its own globe ({lost ?? "never lost"})");
        Check(nearestMoon, "the readout named the moon as the nearest body as it came near");
        Check(_game.Spaceflight.MoonSurface.Planet != null, $"its surface arrived ahead of the descent, in {_game.Spaceflight.MoonSurface.ArriveMs:F0} ms on the main thread");
        Check(p95 < 16.7, $"the flight keeps the frame budget: 95th percentile {p95:F1} ms (budget 16.7 ms)");
    }

    // ---- Down onto it --------------------------------------------------------------------------

    IEnumerable<int> Descent()
    {
        GD.Print("-- down through the moon's crossing --");
        Vessel v = _skiff;
        Celestial moon = _game.Orbit.Moon;
        var (tx, tz) = moon.Under(v.Body.Position.ToGodot());
        N.Quaternion toWorld = N.Quaternion.Normalize(moon.Turn * moon.Map.Frame(tx, tz));
        Vector3 nose = N.Vector3.Transform(-N.Vector3.UnitZ, toWorld).ToGodot();
        Vector3 top = N.Vector3.Transform(N.Vector3.UnitY, toWorld).ToGodot();
        foreach (int x in _pilot.TurnTo(v, nose, top)) yield return x;
        int crossings = _game.Spaceflight.Crossings.Count;
        _game.Controls.Hold(Key.Ctrl);
        for (int i = 0; i < 60 * 120 && _game.Spaceflight.Crossings.Count == crossings; i++) yield return 0;
        _game.Controls.ReleaseAll();
        SurfaceZone surface = _game.Spaceflight.MoonSurface;
        Check(_game.Zone == surface && v.Zone == surface && _game.Survivor.Seated == v, "down through its band it crossed onto the moon's own surface, in the seat, no loading screen");
        if (_game.Zone != surface) yield break;
        Crossing down = _game.Spaceflight.Crossings[^1];
        Check(down.PositionBefore == down.PositionAfter && down.VelocityBefore == down.VelocityAfter && down.Audit.Count == 0,
            $"carried straight across at map ({down.MapX:F0}, {down.MapZ:F0}), in {down.Ms:F1} ms, nothing lost ({string.Join("; ", down.Audit)})");
        double dx = Math.Abs(down.MapX - tx);
        dx = Math.Min(dx, moon.Map.Width - dx);
        Check(Math.Sqrt(dx * dx + (down.MapZ - tz) * (down.MapZ - tz)) < 80, "it came in over the point it was over on the moon's globe");
    }

    IEnumerable<int> Landing()
    {
        GD.Print("-- landing --");
        Vessel v = _skiff;
        SurfaceZone surface = _game.Zone;
        Vector3 pad = OrbitTest.FlatSpot(surface, v.Body.Position.ToGodot(), 40f);
        float hurtBefore = v.Body.Damage;
        foreach (int x in _pilot.Land(v, pad)) yield return x;
        foreach (int x in _drive.Wait(30)) yield return x;
        Check(v.Controller.Frozen && v.Body.Damage == hurtBefore, $"landed on the moon on its gear, undamaged (settled {v.Controller.Frozen}, {v.AboveGround:F1} m up, damage {v.Body.Damage - hurtBefore:F2})");
        float g = surface.Physics.Zone.Strength, home = _game.Surface.Physics.Zone.Strength;
        Check(g < home * 0.8f && g > 1f, $"its gravity is its own: {g:F1} m/s² against the world's {home:F1}");
        Check(surface.Weather.Sim == null && surface.Planet.Sky.Density < SurfaceWeather.NoAir && _game.Suit.Beyond != null, "no air: no weather, a black sky, the suit on its own oxygen");
        var seen = surface.Planet.StarSystem;
        Check(seen.Home == _game.Surface.Planet.StarSystem.FuelMoon && seen.Origin == _game.Surface.Planet.StarSystem,
            "its sky is the start world's system seen from the moon: one model, one clock");
        Check(MathF.Abs((float)(surface.Sky.Days - _game.Surface.Sky.Days)) < 1e-6f, $"one clock: the moon's sky and the world's tell the same day ({surface.Sky.Days:F4})");
    }

    IEnumerable<int> BackUp()
    {
        GD.Print("-- and back up off it --");
        Vessel v = _skiff;
        int crossings = _game.Spaceflight.Crossings.Count;
        _game.Controls.Hold(Key.Space);
        for (int i = 0; i < 60 * 150 && !_game.InOrbit; i++) yield return 0;
        _game.Controls.ReleaseAll();
        Check(_game.InOrbit && _game.Spaceflight.Crossings.Count == crossings + 1 && _game.Orbit.Anchor == _game.Orbit.Moon, "the climb off the moon crossed back into orbit, the moon still the frame");
        if (_game.InOrbit) Check(_game.Spaceflight.Crossings[^1].Audit.Count == 0, "nothing lost or doubled on the way up");
    }
}
