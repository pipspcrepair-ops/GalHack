using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using Slice.Start;
using Slice.Game;
using Wildlife;

namespace Slice.Tests;

/// <summary>
/// The start is quiet: with the wildlife let loose, for the first minutes outside the bunker, nothing
/// is ever inside its walls or interior, nothing is first seen within its clearance (surface and caves
/// alike), no aggressive creature turns up near it, and creatures set against its outside, or put
/// inside it, never end a step in it.
///
/// Split out of StartTest (it took about 100 s a world). Run: .	ools	est.ps1 -Project prototypes/slice -Scene res://tests/WildStartTest.tscn
/// (-- --seeds N: that many random worlds; -- --seed N --planet type: replay one)
/// </summary>
public partial class WildStartTest : Node
{
    const float WildSeconds = 100f;

    const int WatchdogMinutes = 25;
    const int WorldsPerRun = 3;
    IEnumerator<int> _run;
    int _failures, _passes;
    readonly System.Diagnostics.Stopwatch _clock = System.Diagnostics.Stopwatch.StartNew();

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
        catch (Exception e) { Check(false, $"scenario threw: {e}"); more = false; }
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

    IEnumerable<int> Scenario()
    {
        string[] args = OS.GetCmdlineUserArgs();
        int random = WorldsPerRun;
        for (int i = 0; i < args.Length - 1; i++) if (args[i] == "--seeds") int.TryParse(args[i + 1], out random);
        var worlds = new List<WorldChoice>();
        if (WorldChoice.FromArgs(args) is WorldChoice only) worlds.Add(only);
        else
        {
            for (int i = 0; i < random; i++) worlds.Add(WorldChoice.Fresh("random"));
            worlds.Add(WorldChoice.Fresh("ice"));
        }
        foreach (WorldChoice pinned in worlds)
            foreach (int x in OneWorld(pinned)) yield return x;
    }

    IEnumerable<int> OneWorld(WorldChoice pinned)
    {
        var game = new Game.Game { Interactive = false, World = pinned };
        AddChild(game);
        yield return 0;
        WorldChoice world = game.World;
        GD.Print($"-- {world} --");
        var watch = System.Diagnostics.Stopwatch.StartNew();
        while (game.Phase != Phase.Playing && watch.Elapsed.TotalSeconds < 120)
        {
            if (game.Zone.IsBuilt && game.Bunker == null) break;
            yield return 0;
        }
        if (game.Bunker == null || game.Phase != Phase.Playing)
        {
            Check(false, $"{world}: no start site found, or the castaway never woke");
            game.QueueFree();
            yield return 0;
            yield break;
        }
        game.Zone.Weather.Bridge.Calm = true;
        game.Zone.Wildlife.Frozen = true;
        Bunker b = game.Bunker;
        game.Survivor.PlaceOutside(new System.Numerics.Vector3(b.Outside.X, b.Outside.Y, b.Outside.Z));
        foreach (int x in QuietStart(game, world)) yield return x;
        game.QueueFree();
        for (int i = 0; i < 10; i++) yield return 0;
    }

    IEnumerable<int> QuietStart(Game.Game game, WorldChoice world)
    {
        Bunker b = game.Bunker;
        var patch = game.Zone.Wildlife;
        string tag = $"{world.Name}, seed {world.Seed}";
        // Out at the stairs' top, on foot, wildlife loose.
        patch.Frozen = false;
        var seen = new HashSet<Creature>();
        int inside = 0, tooNear = 0, aggressive = 0, aggressiveAnywhere = 0, total = 0;
        float nearest = float.MaxValue;
        float Edge(Vector3 p)
        {
            var (centre, along, hx, hz) = b.Footprint();
            Vector3 d = p - centre;
            float ax = MathF.Atan2(along.Z, along.X);
            float c = MathF.Cos(ax), s = MathF.Sin(ax);
            float lx = d.X * c + d.Z * s, lz = -d.X * s + d.Z * c;
            return new Vector2(MathF.Max(MathF.Abs(lx) - hx, 0f), MathF.Max(MathF.Abs(lz) - hz, 0f)).Length();
        }
        bool InWalls(Vector3 p) => b.Over(p, 0.2f) && p.Y < game.Zone.GroundAt(p.X, p.Z).Y + 0.1f;
        void Watch(WildSim sim, Vector3 origin)
        {
            foreach (Creature c in sim.Creatures)
            {
                if (c.Removed || c.Dead) continue;
                Vector3 p = (c.Species.Climbs ? Crawl.Center(c) : c.Position + Vector3.Up * 0.3f) + origin;
                if (InWalls(p)) inside++;
                if (!seen.Add(c)) continue;
                total++;
                float e = Edge(p);
                nearest = MathF.Min(nearest, e);
                bool bad = e < Bunker.KeepClear - 0.5f;
                if (bad) tooNear++;
                if (c.Species.Temperament == Temperament.Aggressive)
                {
                    aggressiveAnywhere++;
                    if (bad) aggressive++;
                }
            }
        }
        for (float t = 0f; t < WildSeconds; t += 1f / 60f)
        {
            Watch(patch.Sim, patch.Origin);
            foreach (var site in patch.Sites.ToArray()) if (GodotObject.IsInstanceValid(site)) Watch(site.Sim, site.GlobalPosition);
            yield return 0;
        }
        Check(inside == 0, $"{tag}: {WildSeconds:F0} s loose at the start, no creature ever inside the bunker's walls or interior ({inside} samples)");
        Check(tooNear == 0, $"{tag}: none of {total} creatures first seen within {Bunker.KeepClear:F0} m of the bunker (nearest {nearest:F0} m)");
        Check(aggressive == 0, $"{tag}: aggressive creatures spawned near the start: {aggressive} ({aggressiveAnywhere} further out, {patch.Sites.Count} cave sims)");

        // Put against the outside and inside: walkers never end a step in the walls.
        var species = patch.Sim.Creatures.Where(c => !c.Species.Flies && !c.Species.Climbs && c.Species.Rig != RigKind.Swarm).Select(c => c.Species).Distinct().Take(3).ToList();
        var put = new List<Creature>();
        var (centre2, along2, hx2, hz2) = b.Footprint();
        Vector3 across = new Vector3(-along2.Z, 0f, along2.X).Normalized();
        Vector3 aln = new Vector3(along2.X, 0f, along2.Z).Normalized();
        for (int i = 0; i < 12 && species.Count > 0; i++)
        {
            float side = i % 2 == 0 ? 1f : -1f, k = (i / 2 - 2.5f) * 0.9f;
            Vector3 w = i < 6 ? centre2 + aln * (side * (hx2 + 0.5f)) + across * (k * 2f) : centre2 + across * (side * (hz2 + 0.5f)) + aln * (k * 2f);
            w.Y = game.Zone.GroundAt(w.X, w.Z).Y;
            Vector3 local = w - patch.Origin;
            Creature c = patch.Sim.Spawn(species[i % species.Count], local, null, null);
            put.Add(c);
            // Drive them at the walls: home and heading inward.
            c.Home = centre2 - patch.Origin;
            c.Velocity = (centre2 - w).Normalized() * 3f;
        }
        int ended = 0, steps = 0;
        for (int t = 0; t < 600; t++)
        {
            foreach (Creature c in put)
            {
                if (c.Removed || c.Dead) continue;
                c.Velocity = ((centre2 - patch.Origin - c.Position) with { Y = 0f }).Normalized() * 3f;
                steps++;
                if (InWalls(c.Position + Vector3.Up * 0.3f + patch.Origin)) ended++;
            }
            yield return 0;
        }
        Check(put.Count > 0 && ended == 0, $"{tag}: {put.Count} creatures set against the bunker's outside and driven at it never end a step in its walls ({ended} of {steps})");
        foreach (Creature c in put) patch.Sim.Remove(c);
        patch.Frozen = true;
    }
}
