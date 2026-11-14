using System;
using System.Collections.Generic;
using System.Linq;
using GalHack.Items;
using GalHack.Physics.Engine;
using GalHack.Structures;
using Godot;
using Slice.Game;
using Slice.Items;
using Slice.Ui;
using Slice.Vessels;
using Weather.Model;
using Structure = GalHack.Structures.Structure;

namespace Slice.Tests;

/// <summary>
/// Weather warnings come only through a working weather forecast block: with none anywhere there are none; on a core
/// structure whose broadcast holds you, or on the one you sit in, they show; out of range, unpowered or with the block
/// gone they do not. Attacks flash in every case.
///
/// Run: test-q.ps1 -Project prototypes/slice -Scene res://tests/ForecastTest.tscn
/// </summary>
public partial class ForecastTest : Node
{
    IEnumerator<int> _run;
    int _failures, _passes;

    public override void _Ready()
    {
        new System.Threading.Thread(() =>
        {
            System.Threading.Thread.Sleep(TimeSpan.FromMinutes(8));
            Console.Error.WriteLine("FAIL  the run took over 8 minutes: watchdog");
            System.Environment.Exit(1);
        }) { IsBackground = true }.Start();
        ProcessPhysicsPriority = 100;
        _run = Scenario().GetEnumerator();
    }

    public override void _PhysicsProcess(double delta)
    {
        bool more;
        try { more = _run.MoveNext(); }
        catch (Exception e) { Check(false, $"scenario threw: {e}"); more = false; }
        if (more) return;
        GD.Print(_failures == 0 ? $"ALL PASSED ({_passes} checks)" : $"{_failures} CHECK(S) FAILED ({_passes} passed)");
        SetPhysicsProcess(false);
        Wildlife.Shutdown.Quit(this, _failures == 0 ? 0 : 1);
    }

    void Check(bool ok, string what)
    {
        if (ok) _passes++; else _failures++;
        GD.Print($"  {(ok ? "ok   " : "FAIL ")} {what}");
    }

    static readonly Vector2 Size = new(1920, 1080);

    IEnumerable<int> Scenario()
    {
        var game = new Game.Game { Interactive = false, World = WorldChoice.FromArgs(OS.GetCmdlineUserArgs()) ?? WorldChoice.Tested, SkipIntro = true };
        AddChild(game);
        var drive = new Driver(game);
        var play = new Playthrough(game);
        foreach (int x in drive.UntilPlaying()) yield return x;
        game.Zone.Wildlife.Frozen = true;
        GD.Print($"-- the weather forecast block, {game.World} --");
        var data = game.Zone.Data;
        Check(data.Catalogue.Type("weather-forecast").IsForecast && data.Items.TryGet("weather-forecast", out ItemDef item) && item.Unlock is { Level: > 1 },
            "data: the Weather Forecast is a researchable device block");

        // A structure with no forecast block: a hazard raises nothing.
        Vector3 site = play.FlatSpot(game.Survivor.WorldFeet, 30f, 0.6f, 0.4f, span: 14f);
        Structure s = CoreCraft.Ship(data.Catalogue, false, _ => true);
        Vessel v = Vessel.Spawn(game.Zone, s, (site + Vector3.Up * 3f).ToNumerics(), 0f, landed: true);
        v.Settle();
        foreach (int x in drive.Wait(30)) yield return x;
        Vector3 near = site + new Vector3(0f, 0.5f, 6f);
        foreach (int x in Stand(game, drive, near)) yield return x;
        foreach (int x in Hazard(game, drive)) yield return x;
        Check(game.CoresInReach.Contains(v) && !game.ForecastReaches && !game.Alerts.Hazards.Any(), "in range of a core structure with no forecast block: no warning");
        Check(game.Alerts.Attacks.Any(), "an attack still flashes");

        // The block on the structure: it shows, in range.
        PlaceError placed = Building.Place(s, new Int3(2, 1, 2), Building.Make(s, data.Catalogue.Type("weather-forecast")));
        Check(placed == PlaceError.None, $"a forecast block goes on the structure ({placed})");
        foreach (int x in Hazard(game, drive)) yield return x;
        Check(v.Forecasting && game.Alerts.Hazards.Any(), $"in range of a structure with a forecast block: the warning shows ({game.Alerts.Hazards.Count})");
        Check(game.Alerts.Attacks.Any(), "an attack flashes");

        // Out of range: gone.
        Vector3 far = site + new Vector3(500f, 0f, 0f);
        foreach (int x in Stand(game, drive, far)) yield return x;
        foreach (int x in Hazard(game, drive)) yield return x;
        Check(!game.CoresInReach.Contains(v) && !game.Alerts.Hazards.Any(), "out of its range: no warning");
        Check(game.Alerts.Attacks.Any(), "an attack still flashes there");

        // Seated in its chair: it shows; stand up out of range: gone again.
        foreach (int x in Stand(game, drive, near)) yield return x;
        Check(game.Survivor.Sit(v), "sits in its command chair");
        foreach (int x in Hazard(game, drive)) yield return x;
        Check(game.Survivor.Seated == v && game.ForecastReaches && game.Alerts.Hazards.Any(), "seated in the chair of a structure with a forecast block: the warning shows");
        game.Survivor.StandUp();
        foreach (int x in Stand(game, drive, far)) yield return x;
        foreach (int x in Hazard(game, drive)) yield return x;
        Check(game.Survivor.Seated == null && !game.Alerts.Hazards.Any(), "out of the chair and out of range: no warning");

        // Unpowered, then powered, then the block gone.
        foreach (int x in Stand(game, drive, near)) yield return x;
        v.PowerOn = false;
        foreach (int x in Hazard(game, drive)) yield return x;
        Check(!v.Forecasting && !game.Alerts.Hazards.Any(), "its power off: no warning");
        Check(game.Alerts.Attacks.Any(), "an attack still flashes unpowered");
        v.PowerOn = true;
        foreach (int x in Hazard(game, drive)) yield return x;
        Check(game.Alerts.Hazards.Any(), "power back on: the warning shows");
        Building.Remove(s, new Int3(2, 1, 2), null);
        foreach (int x in Hazard(game, drive)) yield return x;
        Check(!v.Forecasting && !game.Alerts.Hazards.Any(), "the block taken away: no warning");
        Check(game.Alerts.Attacks.Any(), "an attack still flashes with it gone");
    }

    /// <summary>Stands the survivor at a place on the ground and lets the area link settle.</summary>
    static IEnumerable<int> Stand(Game.Game game, Driver drive, Vector3 at)
    {
        game.Survivor.PlaceOutside((at with { Y = game.Zone.GroundAt(at.X, at.Z).Y + 0.3f }).ToNumerics());
        foreach (int x in drive.Wait(20)) yield return x;
    }

    /// <summary>A fresh lightning strike beside the survivor and an attack on them, and a few ticks for the visor's alerts to read them.</summary>
    static IEnumerable<int> Hazard(Game.Game game, Driver drive)
    {
        WeatherSim sim = game.Zone.Weather.Sim;
        sim.ClearHazards();
        sim.StartStrike(HazardKind.Lightning, game.Survivor.WorldFeet + new Vector3(20f, 0f, 0f));
        game.Alerts.Attack("wildlife", game.Survivor.WorldFeet + new Vector3(10f, 0f, 0f));
        foreach (int x in drive.Wait(4)) yield return x;
    }
}
