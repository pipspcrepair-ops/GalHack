using System;
using System.Collections.Generic;
using System.Diagnostics;
using GalHack.Physics.Engine;
using Godot;
using Slice.Game;
using Slice.Surface;
using SurvivalVitals.Model;
using Weather.Model;
using Wildlife;
using WeatherKind = Weather.Model.WeatherKind;
using N = System.Numerics;

namespace Slice.Tests;

/// <summary>
/// The world pushing back, on a real world, through the game itself: swimming and going under.
///
/// Run: --headless --fixed-fps 60 --path prototypes/slice res://tests/ClimateTest.tscn
/// </summary>
public partial class ClimateTest : Node
{
    const int WatchdogMinutes = 15;
    Game.Game _game;
    Playthrough _play;
    Driver _drive;
    IEnumerator<int> _run;
    int _failures, _passes;
    readonly Stopwatch _clock = Stopwatch.StartNew();

    public override void _Ready()
    {
        var thread = new System.Threading.Thread(() =>
        {
            System.Threading.Thread.Sleep(TimeSpan.FromMinutes(WatchdogMinutes));
            Console.Error.WriteLine($"FAIL  the run took over {WatchdogMinutes} minutes: watchdog");
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

    SurfaceZone Zone => _game.Zone;
    Slice.Player.Survivor Me => _game.Survivor;

    IEnumerable<int> Scenario()
    {
        _game = new Game.Game { Interactive = false, SkipIntro = false, World = WorldChoice.FromArgs(OS.GetCmdlineUserArgs(), WorldChoice.Tested) };
        AddChild(_game);
        _play = new Playthrough(_game);
        _drive = _play.Drive;
        while (_game.Phase != Phase.Playing) yield return 0;
        Zone.Weather.Bridge.Calm = true;
        Zone.Weather.Sim.Snap(WeatherKind.Clear);
        Zone.Wildlife.Frozen = true;
        foreach (int x in _drive.UntilPlaying()) yield return x;
        foreach (int x in _play.WakeAndLeave((_, _) => { })) yield return x;
        foreach (int x in _drive.ClimbOut(_game.Bunker)) yield return x;
        foreach (int x in Sections()) yield return x;
    }

    IEnumerable<int> Sections()
    {
        foreach (int x in Swim()) yield return x;
    }

    /// <summary>The nearest deep water to the castaway, found in the generator's own columns; null on a dry world.</summary>
    public static Vector3? DeepWater(SurfaceZone zone, Vector3 near, float depth, float within)
    {
        for (float r = 40f; r <= within; r += 40f)
        for (int i = 0; i < 48; i++)
        {
            float a = i * MathF.Tau / 48f, x = near.X + MathF.Cos(a) * r, z = near.Z + MathF.Sin(a) * r;
            if (zone.Water.IsLava || !zone.Water.LiquidAt(x, z, out float surface, out float ground) || surface - ground < depth) continue;
            return new Vector3(x, surface, z);
        }
        return null;
    }

    IEnumerable<int> Swim()
    {
        if (DeepWater(Zone, Me.WorldFeet, 5f, 3000f) is not Vector3 water)
        {
            GD.Print("  (no deep water within 3 km on this world: going under is not tried)");
            yield break;
        }
        // STAGING: set down over the water (swimming is tested, not the walk there).
        Me.PlaceOutside((water + Vector3.Up * 2f).ToNumerics());
        foreach (int x in _drive.UntilSettled()) yield return x;
        foreach (int x in _drive.Wait(120)) yield return x;
        bool floats = Me.Legs.Swimming && !Me.Body.Water.Underwater && Me.WorldEye().Y > water.Y;
        Check(floats, $"dropped in, the castaway floats and swims with the head out: swimming {Me.Legs.Swimming}, eye {Me.WorldEye().Y - water.Y:F2} m over the surface");
        Check(!Zone.Underwater.CameraUnder, "with the head out the view stays above the surface");

        double oxygen = _game.Suit.Vitals.Oxygen;
        _game.Controls.Hold(Key.Ctrl);
        for (int i = 0; i < 600 && !(Zone.Underwater.CameraUnder && Me.WorldEye().Y < water.Y - 1.5f); i++) yield return 0;
        foreach (int x in _drive.Wait(240)) yield return x;
        _game.Controls.ReleaseAll();
        Exposure air = _game.Suit.Last.Exposure;
        Check(Me.Body.Water.Underwater && Zone.Underwater.CameraUnder, $"Ctrl dives: wholly under, {Zone.Underwater.Depth:F1} m down, and the view has gone under with it");
        Check(_game.Camera.Environment != null, "under the surface the camera sees the murk, not the sky");
        Check(air.OxygenPercent == 0 && _game.Suit.Sim.Player.Gear.HelmetOn && _game.Suit.Vitals.Oxygen < oxygen,
            $"nothing to breathe under water: the helmet sealed and the suit's oxygen falls ({oxygen:F1} to {_game.Suit.Vitals.Oxygen:F1})");
        for (int i = 0; i < 1200 && Me.Body.Water.Underwater; i++) yield return 0;
        foreach (int x in _drive.Wait(30)) yield return x;
        Check(!Me.Body.Water.Underwater && !Zone.Underwater.CameraUnder && _game.Camera.Environment == null, "let go, the body floats back up and the sky returns");

        // A land animal driven out over the water floats on it at the most, never sinks.
        WildlifePatch patch = Zone.Wildlife;
        patch.FinishFollowing();
        foreach (int x in _drive.Wait(5)) yield return x;
        Vector3 local = patch.PatchLocal(Me.WorldFeet);
        if (patch.Fauna.Species.Count > 0 && patch.Sim.Terrain.Inside(local, 20f))
        {
            Species s = patch.Fauna.Species[0];
            Creature c = patch.Sim.Spawn(s, local);
            float below = water.Y - patch.FromPatch(c.Position).Y;
            Check(below <= WildlifePatch.Draught + 0.05f, $"a land animal over deep water floats: its feet {below:F2} m under the surface");
            c.Removed = true;
        }
    }
}
