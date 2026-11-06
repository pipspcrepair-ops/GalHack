using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using GalHack.Physics;
using Godot;
using PoiGenerator.Gen;
using Slice.Game;
using Slice.Places;

namespace Slice.Tests;

/// <summary>
/// The planet's places as real, playable structures in the slice.
///
/// Run: .\tools\test.ps1 -Project prototypes/slice -Scene res://tests/PlaceTest.tscn
/// (-- --survey: list the places round the start of a world and what each holds, nothing else)
/// </summary>
public partial class PlaceTest : Node
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
        if (Array.IndexOf(args, "--survey") >= 0)
        {
            foreach (int x in Survey(WorldChoice.FromArgs(args) ?? WorldChoice.Tested)) yield return x;
            yield break;
        }
        foreach (int x in Systems(WorldChoice.FromArgs(args) ?? WorldChoice.Tested)) yield return x;
    }

    /// <summary>
    /// The systems checks, with the focus moved by hand (printed): the place in view stands up
    /// within the frame budget, the far drawing gives way to it, its parts are all there.
    /// </summary>
    IEnumerable<int> Systems(WorldChoice world)
    {
        var game = new Game.Game { Interactive = false, World = world, SkipIntro = true };
        AddChild(game);
        var drive = new Driver(game);
        foreach (int x in drive.UntilPlaying()) yield return x;
        game.Zone.Weather.Bridge.Calm = true;
        PoiPlaces places = game.Places;
        var site = game.Bunker.Poi;
        // Far places are solid on the world layer.
        Node3D far = null;
        for (int i = 0; i < 600 && (far = game.Zone.Pois.NodeOf(site)) == null; i++) yield return 0;
        var farBody = far?.GetNodeOrNull<StaticBody3D>("Collision");
        Check(farBody != null && farBody.CollisionLayer == Layers.World, $"the far drawing of {site.Name} collides on the world layer ({farBody?.CollisionLayer})");
        GD.Print($"  (SYSTEMS: the focus is moved to the place by hand, not walked; the warm-up while loading took {places.WarmUpMs:F0} ms)");
        Vector3 centre = game.Zone.Planet.NearestCopy(new Vector3(site.Origin.X, site.Origin.Y, site.Origin.Z), game.Survivor.WorldFeet);
        places.Focus = () => centre + Vector3.Back * 250f;
        var watch = Stopwatch.StartNew();
        int frames = 0;
        while (places.PlaceOf(site) is not { Ready: true } && watch.Elapsed.TotalSeconds < 60) { frames++; yield return 0; }
        Place p = places.PlaceOf(site);
        Check(p is { Ready: true }, $"{site.Name} stands up as a structure when the focus comes within {places.BuildRadius} m ({frames} frames, {watch.Elapsed.TotalSeconds:F1} s)");
        if (p == null) yield break;
        var log = places.BuildLog.Last();
        Check(places.WorstFrameMs < places.BudgetMs + 12.0, $"built over frames: {log.Chunks} chunks; worst frame {places.WorstFrameMs:F1} ms of building, worst single step {places.WorstStepMs:F1} ms (by stage {string.Join(", ", places.StageWorst.Select(kv => $"{kv.Key}: {kv.Value:F1}"))}) (budget {places.BudgetMs} ms a frame)");
        for (int i = 0; i < 240; i++) yield return 0; // the creatures and lairs go in over these frames
        double views = game.Zone.Wildlife.Sites.Count > 0 ? game.Zone.Wildlife.Sites.Max(s => s.WorstViewMs) : 0;
        Check(views < 8.0, $"its creatures and lair marks go in a few a frame: worst {views:F1} ms a frame");
        GD.Print($"  slow steps: {string.Join("; ", places.SlowSteps.Select(s => $"{s.Stage} {s.Ms:F1}"))}");
        bool farHidden = far.GetChildren().OfType<MeshInstance3D>().Where(m => m.Name.ToString().StartsWith("Chunk")).All(m => !m.Visible) && farBody.ProcessMode == ProcessModeEnum.Disabled;
        Check(farHidden, "the far drawing's blocks give way to it in the same frame (hidden, its collision off); its lamps and smoke stay");
        GD.Print($"  doors {p.Doors.Count} ({p.Doors.Count(d => d.Clearance != null)} locked, {p.Doors.Count(d => d.Jammed)} jammed), deco rooms {p.DecoRooms}, caches {p.Memory.Caches.Count}, creatures {p.Memory.Fauna?.Sim?.Creatures.Count}");
        game.QueueFree();
        yield return 0;
    }

    /// <summary>What each place within 1.5 km of the start holds.</summary>
    IEnumerable<int> Survey(WorldChoice world)
    {
        var game = new Game.Game { Interactive = false, World = world, SkipIntro = true };
        AddChild(game);
        var drive = new Driver(game);
        foreach (int x in drive.UntilPlaying()) yield return x;
        Vector3 start = game.Start;
        GD.Print($"-- {world}: start {start}, the place in view: {game.Bunker.Poi?.Name}");
        var deep = game.Places.Fauna;
        foreach (var site in game.Zone.Planet.Pois.OrderBy(s => game.Places.DistanceTo(s, start)))
        {
            float d = game.Places.DistanceTo(site, start);
            if (d > 1500f) break;
            PoiInstance inst = site.Instance;
            var layout = inst.Layout;
            int ladders = inst.Blocks.Blocks().Count(b => b.Block.Type == BlockType.Ladder);
            int stairs = inst.Blocks.Blocks().Count(b => b.Block.Type == BlockType.Stair);
            int doors = layout.Doorways.Count(dw => dw.Cells.Any(c => inst.Blocks.Get(c).Type == BlockType.Door && inst.Blocks.Get(c).Variant != DoorState.Open));
            var mem = new PlaceMemory(site);
            var fauna = PlaceFauna.Plan(mem, deep);
            mem.Rooms = Decor.Shared?.Plan(mem);
            GD.Print($"{d,6:F0} m  {site.Name} [{site.Family.Id}] {inst.State} {site.Family.Tier}: {layout.Modules.Count} modules, storeys {layout.Modules.Min(m => m.Storey)}..{layout.Modules.Max(m => m.Storey)}, " +
                     $"{doors} shut doors, {layout.Extras.Locks.Count} locks, {layout.Extras.Keys.Count} keys, {layout.Extras.Secrets.Count} secrets, {ladders} ladder cells, {stairs} stair cells, " +
                     $"{layout.Extras.Lifts.Count} lifts, {inst.Caches.Count} caches, town {(inst.Townsfolk != null ? $"{inst.Townsfolk.People.Count} people {inst.Mood}" : "none")}, " +
                     $"fauna {(fauna.Silent ? "silent" : string.Join(" ", fauna.Population.Spawns.GroupBy(s => s.Role).Select(g => $"{g.Key}x{g.Count()}")))}");
        }
        yield return 0;
    }
}
