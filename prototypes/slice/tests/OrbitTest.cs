using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using GalHack.Items;
using GalHack.Physics;
using GalHack.Physics.Engine;
using GalHack.Structures;
using Godot;
using PhysicsSandbox.Bridges;
using PlanetSurface;
using Slice.Game;
using Slice.Items;
using Slice.Orbit;
using Slice.Surface;
using Slice.Vessels;
using Weather.Model;
using N = System.Numerics;
using Structure = GalHack.Structures.Structure;

namespace Slice.Tests;

/// <summary>
/// Phase 2 played through, headless, by the game's own controls: the castaway builds the Skiff,
/// flies it straight up out of the atmosphere into orbit, flies to a derelict, steps out and
/// floats across, cranks its airlock open, goes in (dark, no gravity), takes what is in its hold,
/// salvages a block, claims it with a core on the way out, floats back to the Skiff, flies over
/// a chosen point on the globe, turns to a chosen heading, descends, and lands there. Every tick
/// it checks that the castaway and the Skiff are in exactly one zone; it checks the crossings'
/// mapping (the map point under you and your heading), the ledger, that everything on the surface
/// waited exactly as it was left, and the frame cost in orbit.
///
/// Setting up is the one shortcut, printed when it happens: the Skiff's parts and a base core
/// are handed over and the tech tree opened (the real-player test earns all of that). From there
/// on, only the game's inputs: no teleports, no grants.
///
/// Run: .\tools\test.ps1 -Project prototypes/slice -Scene res://tests/OrbitTest.tscn
/// </summary>
public partial class OrbitTest : Node
{
    const int WatchdogMinutes = 14;
    Game.Game _game;
    Driver _drive;
    IEnumerator<int> _run;
    int _failures, _passes;
    readonly Stopwatch _clock = Stopwatch.StartNew();
    Vessel _skiff;
    int _zoneViolations;
    string _firstViolation;

    public override void _Ready()
    {
        var thread = new System.Threading.Thread(() =>
        {
            System.Threading.Thread.Sleep(TimeSpan.FromMinutes(WatchdogMinutes));
            System.Console.Error.WriteLine($"FAIL  the run took over {WatchdogMinutes} minutes: watchdog");
            System.Environment.Exit(1);
        }) { IsBackground = true };
        thread.Start();
        ProcessPhysicsPriority = 100; // checks after the game has stepped
        _run = Scenario().GetEnumerator();
    }

    public override void _PhysicsProcess(double delta)
    {
        bool more;
        try
        {
            more = _run.MoveNext();
            if (more) WatchZones();
        }
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

    /// <summary>
    /// Every tick: the castaway and the Skiff are each registered in exactly one zone's world, the
    /// zone the game says they are in, and the castaway's body node is under that zone (or in a
    /// cabin inside it). Never in both, never in neither.
    /// </summary>
    void WatchZones()
    {
        if (_game?.Survivor == null) return;
        SurfaceZone surface = _game.Surface, orbit = _game.Orbit;
        void Look(Body b, SurfaceZone said, string what)
        {
            int n = Handover.WorldsHolding(b, surface, orbit);
            bool ok = n == 1 && said.Physics.Bodies.Contains(b) && said.IsInsideTree() && said == _game.Zone;
            if (ok) return;
            _zoneViolations++;
            _firstViolation ??= $"{what}: in {n} worlds at {_clock.Elapsed.TotalSeconds:F1} s";
        }
        Look(_game.Survivor.Body, _game.Survivor.Zone, "the castaway");
        if (_skiff != null && (_game.Survivor.Seated == _skiff || _skiff.Zone == _game.Zone)) Look(_skiff.Body, _skiff.Zone, "the Skiff");
        Node n = _game.Survivor.Node;
        while (n != null && n is not SurfaceZone) n = n.GetParent();
        if (n != _game.Zone)
        {
            _zoneViolations++;
            _firstViolation ??= $"the castaway's node is under {n?.Name ?? "nothing"}, not the zone they are in, at {_clock.Elapsed.TotalSeconds:F1} s";
        }
    }

    IEnumerable<int> Scenario()
    {
        _game = new Game.Game { Interactive = false, SkipIntro = true, World = WorldChoice.FromArgs(OS.GetCmdlineUserArgs(), WorldChoice.Tested) };
        AddChild(_game);
        _game.Spaceflight.LedgerFolder = System.IO.Path.Combine(OS.GetUserDataDir(), "orbit-test-zones");
        _drive = new Driver(_game);
        foreach (int x in _drive.UntilPlaying()) yield return x;
        SurfaceZone zone = _game.Zone;
        zone.Weather.Bridge.Calm = true;
        zone.Weather.Sim.Snap(WeatherKind.Clear);
        zone.Wildlife.Frozen = true;
        GD.Print($"world: {_game.World}");

        foreach (int x in BuildSkiff()) yield return x;
        if (_skiff == null) yield break;
        foreach (int x in ClimbOut()) yield return x;
        Check(_zoneViolations == 0, $"every tick the castaway and the Skiff were in exactly one zone, the one the game says ({_zoneViolations} ticks wrong{(_firstViolation != null ? ": " + _firstViolation : "")})");
    }

    // ---- Setting up ----------------------------------------------------------------------------

    IEnumerable<int> BuildSkiff()
    {
        GD.Print("-- the Skiff, built by hand --");
        // SETUP SHORTCUT (printed): the Skiff's parts and a base core handed over, the tech tree
        // opened. The real-player test makes and earns all of it; this test is about orbit.
        foreach (var (item, n) in _game.Builder.TotalCost(_game.StarterPlan)) _game.Pack.Add(item, n);
        _game.Pack.Add("base-core", 1);
        _game.Progress.Creative = true;
        GD.Print($"  (setup: handed over the Skiff's parts and a base core, and opened the tech tree)");
        _game.Pack.Grid.SetBlock(3, PlayerInventory.BlueprintBlock);
        Vector3 pad = FlatSpot(_game.Start, 18f);
        Vector3 stand = pad + (pad - _game.Start).Normalized() * 7f;
        foreach (int x in _drive.WalkTo(new Vector3(stand.X, 0, stand.Z), 1.5f)) yield return x;
        foreach (int x in _drive.Tap(Key.Key4)) yield return x;
        foreach (int x in _drive.LookAt(pad, 6)) yield return x;
        foreach (int x in _drive.Click()) yield return x;
        Vessel skiff = _game.Builder.Underway;
        if (skiff == null)
        {
            Fail("the blueprint projector set nothing down");
            yield break;
        }
        int stalls = 0;
        while (_game.Builder.Underway == skiff && stalls < 240)
        {
            int before = skiff.Structure.Count;
            _drive.Face(skiff.Body.Position.ToGodot());
            _game.Controls.Primary = true;
            yield return 0;
            stalls = skiff.Structure.Count > before ? 0 : stalls + 1;
        }
        _game.Controls.Primary = false;
        foreach (int x in _drive.Wait(5)) yield return x;
        Check(skiff.Structure.Count == _game.StarterPlan.Count, $"the Skiff is built block by block: {skiff.Structure.Count} of {_game.StarterPlan.Count}");
        _skiff = skiff;
    }

    // ---- Up ------------------------------------------------------------------------------------

    IEnumerable<int> ClimbOut()
    {
        GD.Print("-- straight up, out of the atmosphere --");
        Vessel v = _skiff;
        foreach (int x in _drive.Board(v)) yield return x;
        Check(_game.Survivor.Seated == v, "F at the seat: at the Skiff's controls");
        float ceiling = _game.Surface.Physics.Zone.Ceiling;
        Vector3 start = v.Body.Position.ToGodot();
        double heading = GlobeMap.HeadingDegrees(v.Body.Rotation);
        _game.Controls.Hold(Key.Space);
        bool preloaded = false;
        int ticks = 0;
        while (!_game.InOrbit && ticks++ < 60 * 150)
        {
            preloaded |= _game.Orbit != null && v.Body.Position.Y < ceiling;
            yield return 0;
        }
        _game.Controls.ReleaseAll();
        Check(_game.InOrbit, $"the climb crossed into orbit after {ticks / 60f:F0} s ({_game.Spaceflight.Crossings.Count} crossing)");
        if (!_game.InOrbit) yield break;
        Check(preloaded, "orbit started loading below the atmosphere's top, before the crossing (the seam is hidden)");
        Crossing up = _game.Spaceflight.Crossings[0];
        OrbitZone orbit = _game.Orbit;
        GD.Print($"  crossed at map ({up.MapX:F0}, {up.MapZ:F0}) heading {up.HeadingDegrees:F1}°, in {up.Ms:F1} ms; orbit was made in {orbit.PrepareMs:F0} ms off the main thread");
        // The point over the globe is the map point under the Skiff, at its height over the top of the atmosphere.
        N.Vector3 p = v.Body.Position;
        var (mx, mz) = orbit.Map.MapPoint(p);
        double dx = Math.Abs(mx - up.MapX);
        dx = Math.Min(dx, orbit.Map.Width - dx);
        double off = Math.Sqrt(dx * dx + (mz - up.MapZ) * (mz - up.MapZ));
        Check(off < 2.0 && v.Zone == orbit && _game.Survivor.Seated == v,
            $"the Skiff came out over the map point it left from ({off:F2} m off), with the castaway still in its seat");
        var (_, local) = orbit.Map.ToSurface(p, v.Body.Rotation);
        double after = GlobeMap.HeadingDegrees(local);
        Check(HeadingError(after, heading) < 1.0, $"its heading over the globe is the heading it climbed with: {after:F1}° (was {heading:F1}°; north stays north)");
        Check(up.Audit.Count == 0, $"the crossing lost and doubled nothing: the ledger's totals and zones match the game's ({string.Join("; ", up.Audit)})");
        Check(_game.Spaceflight.Ledger.ZoneOf("castaway")?.StartsWith("cabin/") == true && _game.Spaceflight.Ledger.ZoneOf(IdOf(v)) == ZoneLedger.Orbit,
            $"on disk the Skiff is in orbit and the castaway in its cabin (crossing committed before anything moved: {_game.Spaceflight.Ledger.LastCommit?.Written} record written)");
        Check(!_game.Surface.IsInsideTree() && _game.Surface.Vessels.Contains(_game.Bunker.Ruin),
            "the surface waits out of the scene, everything on it as it was left (the bunker among it)");
    }

    string IdOf(Vessel v) => _game.Spaceflight.Holders().First(h => h.Name == v.Name && h.Type == "structure").Id;

    static double HeadingError(double a, double b)
    {
        double d = Math.Abs(a - b) % 360;
        return d > 180 ? 360 - d : d;
    }

    Vector3 FlatSpot(Vector3 near, float distance)
    {
        SurfaceZone zone = _game.Zone;
        for (int i = 0; i < 64; i++)
        {
            float a = i * 0.61f, r = distance + (i / 16) * 6f;
            var p = new Vector3(near.X + MathF.Cos(a) * r, 0f, near.Z + MathF.Sin(a) * r);
            float h0 = zone.GroundAt(p.X, p.Z).Y, spread = 0f;
            for (int k = 0; k < 8; k++)
            {
                float b = k * MathF.Tau / 8f;
                spread = MathF.Max(spread, MathF.Abs(zone.GroundAt(p.X + MathF.Cos(b) * 4f, p.Z + MathF.Sin(b) * 4f).Y - h0));
            }
            if (h0 > 1f && spread < 0.35f) return zone.GroundAt(p.X, p.Z);
        }
        return zone.GroundAt(near.X + distance, near.Z);
    }
}
