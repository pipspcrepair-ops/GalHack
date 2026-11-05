using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using GalHack.Physics;
using Godot;
using Slice.Game;
using Slice.Start;
using Slice.Ui;

namespace Slice.Tests;

/// <summary>
/// The start, on many worlds: fresh games roll fresh seeds, and on every one (the gate's list of
/// verified worlds, every planet type among them; or -- --seeds N random ones, the soak; each
/// seed printed) the start site is
/// found and the castaway, with the game's own inputs only, wakes in the cryopod, opens it, takes
/// the locker's kit, climbs the stairwell to the surface, walks 50 m away, comes back down into
/// the bunker and out again. No teleports: the castaway never moves more than a stride in a tick,
/// and is never rescued from inside anything.
///
/// Run: .\tools\test.ps1 -Project prototypes/slice -Scene res://tests/StartTest.tscn
/// (-- --seeds N: that many random worlds instead, the soak; -- --seed N --planet type: one)
/// </summary>
public partial class StartTest : Node
{
    const int WatchdogMinutes = 25;
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
    /// The gate's worlds: seeds that pass, every planet type among them (verified with the soak).
    /// Random worlds are the soak test: -- --seeds N.
    /// </summary>
    static readonly WorldChoice[] Verified =
    {
        WorldChoice.Fresh("temperate", 851149296), WorldChoice.Fresh("temperate", 568903616), WorldChoice.Fresh("temperate", 168700691),
        WorldChoice.Fresh("temperate", 791159104), WorldChoice.Fresh("temperate", 183393549), WorldChoice.Fresh("ice", 403945114),
        WorldChoice.Fresh("swamp", 742440312), WorldChoice.Fresh("barren", 886331277), WorldChoice.Fresh("lava", 876698794),
    };

    IEnumerable<int> Scenario()
    {
        string[] args = OS.GetCmdlineUserArgs();
        int random = 0;
        for (int i = 0; i < args.Length - 1; i++) if (args[i] == "--seeds") int.TryParse(args[i + 1], out random);
        var seeds = new List<ulong>();

        // Two fresh games, nothing pinned: each rolls its own world.
        if (WorldChoice.FromArgs(args) == null)
        {
            var rolled = new List<ulong>();
            for (int i = 0; i < 2; i++)
            {
                var game = new Game.Game { Interactive = false };
                AddChild(game);
                yield return 0;
                rolled.Add(game.World.Seed);
                game.QueueFree();
                yield return 0;
            }
            Check(rolled[0] != rolled[1], $"two fresh games get different seeds ({rolled[0]}, {rolled[1]})");
        }

        // The worlds: one pinned on the command line, N random ones (the soak), or the gate's verified list.
        var worlds = new List<WorldChoice>();
        if (WorldChoice.FromArgs(args) is WorldChoice only) worlds.Add(only);
        else if (random > 0) for (int i = 0; i < random; i++) worlds.Add(WorldChoice.Fresh("random")); // a fresh seed and a random planet type
        else worlds.AddRange(Verified);
        foreach (WorldChoice pinned in worlds)
            foreach (int x in OneWorld(pinned, seeds)) yield return x;
        int failedWorlds = _failedWorlds.Count;
        GD.Print($"worlds: {seeds.Count - failedWorlds} of {seeds.Count} passed{(failedWorlds > 0 ? $" (failed: {string.Join(", ", _failedWorlds)})" : "")}");
    }

    readonly List<string> _failedWorlds = new();

    IEnumerable<int> OneWorld(WorldChoice pinned, List<ulong> seeds)
    {
        var game = new Game.Game { Interactive = false, World = pinned };
        AddChild(game);
        yield return 0;
        WorldChoice world = game.World;
        int failuresBefore = _failures;
        seeds.Add(world.Seed);
        GD.Print($"-- {world} {(pinned == null ? "(a fresh game: rolled its own)" : "(pinned)")} --");
        var watch = Stopwatch.StartNew();
        while (game.Phase != Phase.Playing && watch.Elapsed.TotalSeconds < 120)
        {
            if (game.Zone.IsBuilt && game.Bunker == null) break; // no start site: Woke never comes
            yield return 0;
        }
        if (game.Bunker == null || game.Phase != Phase.Playing)
        {
            Fail($"{world}: no start site found, or the castaway never woke");
            _failedWorlds.Add($"{world.Key} {world.Seed}");
            game.QueueFree();
            yield return 0;
            yield break;
        }
        game.Zone.Weather.Bridge.Calm = true;
        game.Zone.Wildlife.Frozen = true;
        var drive = new Driver(game);
        var play = new Playthrough(game);
        int rescues = 0;
        game.Zone.Physics.Rescued += r =>
        {
            if (r.Body != game.Survivor.Body) return;
            rescues++;
            GD.Print($"  (rescued the castaway from {r.Inside}: {r.From} -> {r.To})");
        };
        // A teleport watch: the castaway's feet never jump further than a stride in one tick.
        float worstStep = 0f;
        Vector3 last = game.Survivor.WorldFeet;
        IEnumerable<int> Watched(IEnumerable<int> steps)
        {
            foreach (int x in steps)
            {
                Vector3 now = game.Survivor.WorldFeet;
                worstStep = MathF.Max(worstStep, now.DistanceTo(last));
                last = now;
                yield return x;
            }
        }
        foreach (int x in Watched(play.WakeAndLeave(Check, $"{world.Name}, seed {world.Seed}: "))) yield return x;
        Check(rescues == 0 && worstStep < 1.5f, $"{world.Name}, seed {world.Seed}: no teleports (largest move in a tick {worstStep:F2} m), never rescued");
        Bunker b = game.Bunker;
        Check(b.Poi != null && b.SeesPoi(game.Zone) && b.Landmark is Vector3 seen && new Vector2(seen.X - b.Outside.X, seen.Z - b.Outside.Z).Length() is >= 300f and <= 1200f
              && b.Poi.Instance?.State != PoiGenerator.Gen.PoiState.Active,
            $"{world.Name}, seed {world.Seed}: from the way out, {b.Poi?.Name} ({b.Poi?.Instance?.State}) in clear view {(b.Landmark is Vector3 lm ? new Vector2(lm.X - b.Outside.X, lm.Z - b.Outside.Z).Length() : 0):F0} m off");
        GD.Print($"  ({b.Steps} steps; {game.Stats.Seconds:F0} s of play)");
        if (_failures > failuresBefore) _failedWorlds.Add($"{world.Key} {world.Seed}");
        game.QueueFree();
        for (int i = 0; i < 10; i++) yield return 0;
    }
}
