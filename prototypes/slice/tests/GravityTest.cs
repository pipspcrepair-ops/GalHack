using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using GalHack.Items;
using GalHack.Physics;
using GalHack.Physics.Engine;
using Godot;
using Planets.Gen;
using PhysicsSandbox.Bridges;
using ShipHandling.Handling;
using Slice.Construction;
using Slice.Game;
using Slice.Items;
using Slice.Player;
using Slice.Vessels;
using Structure = GalHack.Structures.Structure;

namespace Slice.Tests;

/// <summary>
/// Gravity from a world's size reaches everything that falls or thrusts (owner, 22 Nov), on a forced 8 km world
/// (0.2 g, a moon's) and a forced 64 km one (5 g), fresh random worlds each run: the zone pulls its size's g from
/// the one table, and a sleeper's jump height, a 10 m fall's time and landing speed (and the hurt that speed does,
/// the same at any g), the jetpack's climb, a tossed bag's fall and the Skiff's lift-off (thrust against mass × g)
/// all come out of that g.
///
/// Run: G:\GalHack\.tools\queue\test-q.ps1 -Project prototypes/slice -Scene res://tests/GravityTest.tscn
/// </summary>
public partial class GravityTest : Node
{
    const int WatchdogMinutes = 14;
    Game.Game _game;
    Driver _drive;
    IEnumerator<int> _run;
    int _failures, _passes;
    readonly Stopwatch _clock = Stopwatch.StartNew();

    Survivor S => _game.Survivor;
    float Dt => (float)GetPhysicsProcessDeltaTime();

    public override void _Ready()
    {
        new System.Threading.Thread(() =>
        {
            System.Threading.Thread.Sleep(TimeSpan.FromMinutes(WatchdogMinutes));
            Console.Error.WriteLine($"FAIL  the run took over {WatchdogMinutes} minutes: watchdog");
            System.Environment.Exit(1);
        }) { IsBackground = true }.Start();
        ProcessPhysicsPriority = 100;
        _run = Scenario().GetEnumerator();
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

    bool Check(bool ok, string what)
    {
        if (ok) _passes++;
        else _failures++;
        GD.Print($"  {(ok ? "ok   " : "FAIL ")} {what}");
        return ok;
    }

    static bool Near(float got, float want, float share, float slack = 0f) => MathF.Abs(got - want) <= MathF.Abs(want) * share + slack;

    IEnumerable<int> Scenario()
    {
        foreach (int width in new[] { PlanetSpec.Widths[0], PlanetSpec.Widths[^1] })
            foreach (int x in On(width)) yield return x;
    }

    IEnumerable<int> On(int width)
    {
        var types = WorldChoice.Types;
        WorldChoice world = WorldChoice.Fresh(types[Random.Shared.Next(types.Count)].Key) with { WidthKm = width };
        GD.Print($"-- {world} --");
        _game = new Game.Game { Interactive = false, SkipIntro = true, World = world };
        AddChild(_game);
        _drive = new Driver(_game);
        foreach (int x in _drive.UntilPlaying(300)) yield return x;
        if (Check(_game.Phase == Phase.Playing, $"{width} km: the world starts")) foreach (int x in Measure(width)) yield return x;
        _game.QueueFree();
        for (int i = 0; i < 10; i++) yield return 0;
    }

    IEnumerable<int> Measure(int width)
    {
        _game.Zone.Weather.Bridge.Calm = true;
        _game.Zone.Wildlife.Frozen = true;
        float g = _game.Zone.Physics.Zone.Strength, table = PlanetSpec.Gravities[PlanetSpec.ClassOf(width)];
        string at = $"{width} km, {g / 9.8f:F1} g";
        Check(Near(g, 9.8f * table, 1e-4f), $"{at}: the zone pulls {g:F2} m/s², its size's {table} g");
        Controls keys = _game.Controls;
        Vector3 spot = new Playthrough(_game).FlatSpot(S.WorldFeet, 40f, span: 14f);
        Vector3 ground = _game.Zone.GroundAt(spot.X, spot.Z);

        // A 10 m fall: its time and landing speed are the zone's g's, and so is the hurt (the same at the same speed).
        double health = _game.Suit.HealthShare;
        S.PlaceOutside((ground + Vector3.Up * 10f).ToNumerics());
        int ticks = 0;
        float landing = 0f;
        for (; ticks < 900 && !S.Body.OnFloor; ticks++)
        {
            landing = MathF.Max(landing, S.ImpactSpeed);
            yield return 0;
        }
        float fell = ticks * Dt, wantTime = MathF.Sqrt(2f * 10f / g), wantSpeed = MathF.Sqrt(2f * g * 10f);
        foreach (int x in _drive.Wait(20)) yield return x;
        bool hurts = wantSpeed > _game.Zone.Data.Physics.Contacts.PlayerSafeSpeed, hurt = _game.Suit.HealthShare < health - 1e-3;
        Check(Near(fell, wantTime, 0.08f, 2f * Dt) && Near(landing, wantSpeed, 0.08f),
            $"{at}: a 10 m fall takes {fell:F2} s (want {wantTime:F2}) and lands at {landing:F1} m/s (want {wantSpeed:F1})");
        Check(hurt == hurts, $"{at}: landing at {landing:F1} m/s {(hurt ? "hurts" : "doesn't hurt")} (health {health:P0} to {_game.Suit.HealthShare:P0}), as that speed does anywhere (safe to {_game.Zone.Data.Physics.Contacts.PlayerSafeSpeed} m/s)");

        // A hop, the key tapped (one tick, so no jetpack): as high as its push makes under this g.
        foreach (int x in _drive.UntilSettled(10)) yield return x;
        float from = S.WorldFeet.Y, top = from;
        keys.Hold(Key.Space);
        yield return 0;
        keys.Hold(Key.Space, false);
        for (int t = 0; t < 600 && (t < 5 || !S.Body.OnFloor); t++)
        {
            top = MathF.Max(top, S.WorldFeet.Y);
            yield return 0;
        }
        float jump = S.Legs.JumpSpeed, wantHop = jump * jump / (2f * g);
        Check(Near(top - from, wantHop, 0.12f, 0.04f), $"{at}: a hop rises {top - from:F2} m (want {wantHop:F2} at {jump} m/s)");

        // The jetpack held: in the air it pushes up at its own rate against this g (it can't lift off a heavy world).
        foreach (int x in _drive.UntilSettled(10)) yield return x;
        keys.Hold(Key.Space);
        var climb = new List<float>();
        for (int t = 0; t < 300 && climb.Count < 8; t++)
        {
            float before = S.Body.Velocity.Y;
            yield return 0;
            if (t > 2 && !S.Body.OnFloor && S.Legs.JetpackFiring && S.Body.Velocity.Y < 7f && before < 7f) climb.Add((S.Body.Velocity.Y - before) / Dt);
            if (t > 2 && S.Body.OnFloor) break;
        }
        keys.Hold(Key.Space, false);
        float accel = climb.Count > 0 ? climb.Average() : float.NaN, wantAccel = S.Legs.JetpackAccel - g;
        Check(climb.Count >= 3 && Near(accel, wantAccel, 0.1f, 0.3f), $"{at}: the jetpack climbs at {accel:F1} m/s² (want {wantAccel:F1}: its {S.Legs.JetpackAccel} against {g:F1}), over {climb.Count} ticks");
        for (int t = 0; t < 1800 && !S.Body.OnFloor; t++) yield return 0; // a light world's climb comes down slowly
        foreach (int x in _drive.UntilSettled(10)) yield return x;

        // A bag tossed down from 6 m: it falls at this g.
        // (From the ground, not the sleeper's feet: on a light world the jetpack's climb takes a while to come back down.)
        Vector3 under = _game.Zone.GroundAt(S.WorldFeet.X, S.WorldFeet.Z);
        Lootable bag = _game.Zone.Loot.Toss(Bill.Of(("pentaxite", 1)), under + Vector3.Up * 6f, _game.Zone.GroundAt(under.X + 3f, under.Z), "Bag", Colors.White);
        var drops = new List<float>();
        for (int t = 0; t < 10 && bag.Flying is Vector3 v0; t++)
        {
            yield return 0;
            if (bag.Flying is Vector3 v1) drops.Add((v0.Y - v1.Y) / Dt);
        }
        float bagG = drops.Count > 0 ? drops.Max() : float.NaN;
        Check(drops.Count >= 2 && Near(bagG, g, 0.02f), $"{at}: a tossed bag falls at {bagG:F2} m/s² (want {g:F2})");

        foreach (int x in Skiff(at, g)) yield return x;
    }

    /// <summary>The Skiff set down and its throttle held up: it climbs only if its thrust beats its mass × this g (design).</summary>
    IEnumerable<int> Skiff(string at, float g)
    {
        GameData data = _game.Zone.Data;
        Structure plan = data.Blueprint("starter.gbp");
        plan.DirtyChunks.Clear();
        _game.Mode = GameMode.Creative;
        S.Yaw = S.Pitch = 0f;
        WholePlacement job = _game.Builder.PlaceWhole(plan);
        for (int t = 0; t < 600 && !job.Done; t++) yield return 0;
        Vessel v = job.Vessel;
        if (!Check(v != null, $"{at}: the Skiff is set down{(v == null ? $" ({job.Why})" : "")}")) yield break;
        for (int t = 0; t < 300 && (v.PendingShapes > 0 || v.View.Waiting > 0); t++) yield return 0;
        v.Controller?.Wake(v.Body);
        foreach (int x in _drive.Wait(60)) yield return x;
        bool sat = S.Sit(v);
        foreach (int x in _drive.Wait(30)) yield return x;
        HandlingStats here = v.Stats, standard = HandlingModel.Compute(new StructureVessel(plan, data.Handling), new FlightConditions(9.8f, true), data.Handling);
        Check(here != null && Near(here.Weight / g, standard.Weight / 9.8f, 0.01f) && here.CanLiftOff == here.UpwardThrust >= here.Weight,
            $"{at}: the Skiff weighs {here?.Weight:F0} kN here ({standard.Weight:F0} at 1 g) against {here?.UpwardThrust:F0} kN of lift");
        float start = v.Body.Position.Y;
        _game.Controls.Hold(Key.Space);
        foreach (int x in _drive.Wait(180)) yield return x;
        _game.Controls.Hold(Key.Space, false);
        float rose = v.Body.Position.Y - start;
        bool lifts = here?.CanLiftOff == true;
        Check(sat && (lifts ? rose > 3f : rose < 0.5f), $"{at}: seated, Space held 3 s, it {(lifts ? "lifts off" : "stays down, too heavy")}: {rose:+0.0;-0.0} m (lift margin {here?.LiftMargin:+0;-0} kN; console: {v.Controller?.ConsoleMessage})");
        S.StandUp();
        foreach (int x in _drive.Wait(5)) yield return x;
    }
}
