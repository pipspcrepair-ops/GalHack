using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using GalHack.Physics;
using Godot;
using GalHack.Physics.Engine;
using Slice.Game;
using Slice.Start;
using Slice.Ui;

namespace Slice.Tests;

/// <summary>
/// The start, on many worlds: fresh games roll fresh seeds, and on every one (three random worlds,
/// or -- --seeds N of them, the soak; each seed printed) the start site is
/// found and the castaway, with the game's own inputs only, wakes in the cryopod, opens it, takes
/// the locker's kit, climbs the stairwell to the surface, walks 50 m away, comes back down into
/// the bunker and out again. No teleports: the castaway never moves more than a stride in a tick,
/// and is never rescued from inside anything.
///
/// Run: .\tools\test.ps1 -Project prototypes/slice -Scene res://tests/StartTest.tscn
/// (-- --seeds N: that many random worlds, the soak; -- --seed N --planet type: replay one)
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

    /// <summary>Worlds in a plain run: each a fresh seed and a random planet type, so every run tries new ones.</summary>
    const int WorldsPerRun = 3;

    IEnumerable<int> Scenario()
    {
        string[] args = OS.GetCmdlineUserArgs();
        int random = WorldsPerRun;
        for (int i = 0; i < args.Length - 1; i++) if (args[i] == "--seeds") int.TryParse(args[i + 1], out random);
        _exitOnly = args.Contains("--exit-only");
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

        // The worlds: one pinned on the command line, or random ones.
        var worlds = new List<WorldChoice>();
        if (WorldChoice.FromArgs(args) is WorldChoice only) worlds.Add(only);
        else
        {
            for (int i = 0; i < random; i++) worlds.Add(WorldChoice.Fresh("random")); // a fresh seed and a random planet type
            worlds.Add(WorldChoice.Fresh("ice")); // and always a cold one, for the chamber's warmth
        }
        foreach (WorldChoice pinned in worlds)
            foreach (int x in OneWorld(pinned, seeds)) yield return x;
        int failedWorlds = _failedWorlds.Count;
        GD.Print($"worlds: {seeds.Count - failedWorlds} of {seeds.Count} passed{(failedWorlds > 0 ? $" (failed: {string.Join(", ", _failedWorlds)})" : "")}");
    }

    readonly List<string> _failedWorlds = new();

    /// <summary>-- --exit-only: just the way-out check on each world (cheap, for many worlds).</summary>
    bool _exitOnly;

    /// <summary>
    /// What stands over the way out, or null: the sky above the open steps, the top step and the ground beyond it,
    /// 1.5 m wide, 40 m up, asked of every solid there is (terrain, structures, wrecks, hulks) and of the
    /// physics server (places, rocks, trunks), the bunker's own blocks aside.
    /// </summary>
    static string Covered(Game.Game game)
    {
        Bunker b = game.Bunker;
        if (b.Crowded(game.Zone)) return "a place, ruin or wreck stands on it or by it";
        Vector3 across = (b.At(new Vector3(1f, 0f, 0f)) - b.At(Vector3.Zero)).Normalized();
        var space = game.Zone.GetWorld3D().DirectSpaceState;
        var spots = new List<Vector3>();
        for (int i = 0; i <= 8; i++) spots.Add(b.StairTop.Lerp(b.Outside, i / 8f));
        foreach (Vector3 w in b.WayOut) if (w.Y > game.Zone.GroundAt(w.X, w.Z).Y - 0.5f) spots.Add(w);
        foreach (Vector3 spot in spots)
            foreach (float side in new[] { -0.6f, 0f, 0.6f })
            {
                Vector3 p = spot + across * side;
                var hit = space.IntersectRay(PhysicsRayQueryParameters3D.Create(p + Vector3.Up * 0.4f, p + Vector3.Up * 40f));
                if (hit.Count > 0 && hit["collider"].AsGodotObject() is Node n && n != b.Ruin.Node && !b.Ruin.Node.IsAncestorOf(n)) return $"{n.GetPath()} ({n.GetType().Name}) {((Vector3)hit["position"]).Y - p.Y:F1} m over {p}";
                for (float up = 0.4f; up <= 40f; up += 0.5f)
                {
                    var at = (p + Vector3.Up * up).ToNumerics();
                    foreach (var f in game.Zone.Physics.StuckFields)
                        if (f.Owner != b.Ruin.Body && f.IsSolid(game.Survivor.Body, at)) return $"{f.Name} {up:F1} m over {p}";
                }
            }
        return null;
    }

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
        Check(game.WorldStanding, $"{world}: at waking everything round the start stands: {game.Zone.Infra.TileCount} tiles of ruins, wrecks and bridges, {game.Zone.Planet.Pois.Count} places");
        // The wrecks round the start are the tool's to strip: worked through, one comes apart, and stays gone.
        if (game.Zone.Planet.Infra.Wrecks.Count > 0 && game.Places.SalvageSites.FirstOrDefault() is Slice.Places.SalvageSite wreck)
        {
            int count = game.Places.SalvageSites.Count();
            Godot.CollisionObject3D body = wreck.Body;
            for (int k = 0; k < 40 && !wreck.Empty; k++) game.Places.Work(wreck, wreck.HitPoints);
            yield return 0;
            Check(wreck.Empty && game.Places.SalvageSites.Count() == count - 1 && !Godot.GodotObject.IsInstanceValid(body), $"{world}: a wreck comes apart under the tool ({count} to salvage round the start) and is gone");
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
        // The cold of the world stays outside the chamber: the bunker is heated and has its own air.
        foreach (int x in drive.Wait(2)) yield return x;
        var at = new SurvivalVitals.Model.Pos(game.Bunker.Outside.X, game.Bunker.Outside.Y, game.Bunker.Outside.Z);
        var chamber = game.Suit.Exposure(at, game.Suit.Sim.Time);
        bool sealedIn = game.Suit.InSealedRoom;
        game.Suit.InSealedRoom = false;
        var outside = game.Suit.Exposure(at, game.Suit.Sim.Time);
        game.Suit.InSealedRoom = sealedIn;
        double roomC = game.Zone.Data.Rules.Climate.SealedRoomC;
        Check(sealedIn && chamber.Temperature == roomC && chamber.OxygenPercent == 21 && (world.Archetype != Planets.Gen.Archetype.Ice || outside.Temperature < roomC),
            $"{world.Name}, seed {world.Seed}: the world's {outside.Temperature:F0} °C and {outside.OxygenPercent:F0}% oxygen outside don't reach the chamber: {chamber.Temperature:F0} °C, {chamber.OxygenPercent:F0}%");
        // Nothing solid (ground, a place, a hulk, a wreck, a ruin, a rock, a tree) stands over the way out's open sky.
        foreach (int x in drive.Wait(600)) yield return x;
        string covered = Covered(game);
        Check(covered == null, $"{world.Name}, seed {world.Seed}: the way out is open to the sky{(covered == null ? "" : $": {covered}")}");
        if (_exitOnly)
        {
            game.QueueFree();
            for (int i = 0; i < 10; i++) yield return 0;
            yield break;
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
