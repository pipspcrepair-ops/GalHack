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
/// The start, on many worlds: fresh games roll fresh seeds, and on every one (five random
/// temperate worlds, then one of every other planet type, each seed printed) the start site is
/// found and the castaway, with the game's own inputs only, wakes in the cryopod, opens it, takes
/// the locker's kit, climbs the stairwell to the surface, walks 50 m away, comes back down into
/// the bunker and out again. No teleports: the castaway never moves more than a stride in a tick,
/// and is never rescued from inside anything.
///
/// Run: .\tools\test.ps1 -Project prototypes/slice -Scene res://tests/StartTest.tscn
/// (-- --seeds N for more random worlds)
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

    IEnumerable<int> Scenario()
    {
        string[] args = OS.GetCmdlineUserArgs();
        int random = 5;
        for (int i = 0; i < args.Length - 1; i++) if (args[i] == "--seeds") int.TryParse(args[i + 1], out random);

        // Fresh games: no world pinned, so each game rolls its own.
        var worlds = new List<WorldChoice>();
        for (int i = 0; i < random; i++) worlds.Add(null);
        foreach (var t in WorldChoice.Types.Skip(1)) worlds.Add(WorldChoice.Fresh(t.Key));
        // One world only, to look into it: -- --seed N --planet type
        if (WorldChoice.FromArgs(args) is WorldChoice only) worlds = new List<WorldChoice> { only, null };

        var seeds = new List<ulong>();
        foreach (WorldChoice pinned in worlds)
            foreach (int x in OneWorld(pinned, seeds)) yield return x;

        Check(seeds.Count >= 2 && seeds[0] != seeds[1], $"two fresh games get different seeds ({string.Join(", ", seeds.Take(2))})");
        Check(seeds.Distinct().Count() == seeds.Count, $"every game had its own world: seeds {string.Join(", ", seeds)}");
    }

    IEnumerable<int> OneWorld(WorldChoice pinned, List<ulong> seeds)
    {
        var game = new Game.Game { Interactive = false, World = pinned };
        AddChild(game);
        yield return 0;
        WorldChoice world = game.World;
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
        GD.Print($"  ({b.Steps} steps; the start site {(b.Landmark is Vector3 l ? $"faces a skyline {l.DistanceTo(b.Outside):F0} m off" : "has no skyline")}; {game.Stats.Seconds:F0} s of play)");
        game.QueueFree();
        for (int i = 0; i < 10; i++) yield return 0;
    }
}
