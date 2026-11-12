using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using Slice.Game;
using Slice.Surface;
using Weather.Model;

namespace Slice.Tests;

/// <summary>
/// Dust devils and tornadoes stand on open, fairly flat, low ground only (slope at most StandSlope, no more than MaxPerch
/// metres above the land PerchRadius round it), and ground rising sharply ahead turns one aside or breaks it up. On a fresh
/// world, with dust-devil weather then tornado weather and a moving observer, every natural vortex is followed for its whole
/// life, first with the thresholds off (how it was), then on. Counts: vortices born, born on ground above the thresholds,
/// and vortex-seconds touched down on such ground (a grace of 1 s to be broken up).
/// Pass --mesa for a canyon/mesa world (Arid variant 1).
/// Run: --headless --fixed-fps 60 --path prototypes/slice res://tests/VortexTest.tscn
/// </summary>
public partial class VortexTest : Node
{
    Game.Game _game;
    Driver _drive;
    IEnumerator<int> _run;
    int _failures;

    public override void _Ready() => _run = Scenarios().GetEnumerator();

    public override void _PhysicsProcess(double delta)
    {
        bool more;
        try { more = _run.MoveNext(); }
        catch (Exception e) { GD.Print($"FAIL  threw: {e}"); _failures++; more = false; }
        if (more) return;
        GD.Print(_failures == 0 ? "ALL PASSED" : $"{_failures} CHECK(S) FAILED");
        SetPhysicsProcess(false);
        Wildlife.Shutdown.Quit(this, _failures == 0 ? 0 : 1);
    }

    void Check(bool ok, string what)
    {
        GD.Print($"{(ok ? "PASS" : "FAIL")}  {what}");
        if (!ok) _failures++;
    }

    record struct Tally(int Born, int BornHigh, float SecondsHigh, int StayedHigh, float RiseAheadWorst);

    IEnumerable<int> Scenarios()
    {
        _game = new Game.Game { Interactive = false, SkipIntro = true, World = WorldChoice.FromArgs(OS.GetCmdlineUserArgs(), OS.GetCmdlineUserArgs().Contains("--mesa") ? WorldChoice.Fresh("arid") with { Variant = 1 } : WorldChoice.Tested) };
        AddChild(_game);
        _drive = new Driver(_game);
        foreach (int x in _drive.UntilPlaying()) yield return x;
        WeatherSim sim = _game.Zone.Weather.Sim;
        sim.FrontsOff = true;
        RegionRules r = sim.Data.Regions;
        float slope = r.StandSlope, perch = r.MaxPerch, rise = r.AheadRise;
        Vector3 home = _game.Survivor.WorldFeet;
        GD.Print($"thresholds: slope {slope:F2}, perch {perch:F0} m over the mean of the land {r.PerchRadius:F0} m round, ahead {r.AheadRise:F2} over {r.AheadRun:F0} m");

        var real = new RegionRules { StandSlope = slope, MaxPerch = perch, PerchRadius = r.PerchRadius };
        var before = new Tally();
        var after = new Tally();
        foreach (bool on in new[] { false, true })
        {
            r.StandSlope = on ? slope : 1e9f;
            r.MaxPerch = on ? perch : 1e9f;
            r.AheadRise = on ? rise : 1e9f;
            Tally t = default;
            var rng = new Random(on ? 7 : 7);
            var seconds = new Dictionary<int, float>();
            var born = new HashSet<int>();
            foreach (WeatherKind kind in new[] { WeatherKind.Clear, WeatherKind.Tornado, WeatherKind.DustStorm })
            {
                sim.ClearTornadoes();
                sim.ClearCells();
                sim.Snap(kind);
                if (kind == WeatherKind.Clear) sim.Data.Regions.Dust.DevilsPerMinute = 60f;
                for (int step = 0; step < 4 * 60 * 15; step++) // fifteen minutes of this weather, four steps a second
                {
                    if (step % 40 == 0) // a new patch of the world every 10 s
                    {
                        Vector3 p = home + new Vector3((float)(rng.NextDouble() - 0.5) * 24000f, 0f, (float)(rng.NextDouble() - 0.5) * 24000f);
                        _observer = p;
                    }
                    sim.Step(0.25f, _observer);
                    foreach (Tornado v in sim.Tornadoes)
                    {
                        if (v.Kind is not (TornadoKind.Tornado or TornadoKind.DustDevil)) continue;
                        bool bad = !Regions.Stands(real, sim.Land, v.Position);
                        if (born.Add(v.Id)) { t.Born++; if (bad) t.BornHigh++; }
                        if (!v.Touchdown || v.Dissipating || !bad) { seconds[v.Id] = 0f; continue; }
                        seconds[v.Id] = seconds.GetValueOrDefault(v.Id) + 0.25f;
                        if (seconds[v.Id] > 1f) t.SecondsHigh += 0.25f;
                        if (seconds[v.Id] == 1.25f) t.StayedHigh++;
                    }
                    if (step % 240 == 0) yield return 0; // keep the game ticking
                }
            }
            if (on) after = t; else before = t;
            yield return 0;
        }
        r.StandSlope = slope; r.MaxPerch = perch; r.AheadRise = rise;
        GD.Print($"BEFORE: {before.Born} vortices born, {before.BornHigh} on ground above the thresholds, {before.StayedHigh} stayed on such ground over 1 s, {before.SecondsHigh:F0} vortex-seconds there");
        GD.Print($"AFTER:  {after.Born} vortices born, {after.BornHigh} on ground above the thresholds, {after.StayedHigh} stayed on such ground over 1 s, {after.SecondsHigh:F0} vortex-seconds there");
        Check(after.Born >= 5, $"enough vortices to judge ({after.Born})");
        // The rules must thin vortices out over hills and mesas, not abolish them: flat ground still gets its share.
        Check(after.Born >= 0.25f * before.Born, $"enough vortices still stand on the allowed ground: {after.Born} of the {before.Born} born with the rules off (at least 25%)");
        Check(after.BornHigh == 0 && after.StayedHigh == 0, "none spawned on, or stayed on, ground above the thresholds");
    }

    Vector3 _observer;
}
