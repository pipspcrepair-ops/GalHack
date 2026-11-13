using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using GalHack.Physics;
using GalHack.Physics.Engine;
using Godot;
using PlanetSurface.Poi;
using Slice.Game;
using Slice.Places;
using Slice.Vessels;
using PoiGenerator.Gen;
using P = PoiGenerator.Gen;
using S = GalHack.Structures;

namespace Slice.Tests;

/// <summary>
/// The ways up, walked and climbed as a player does, each place on a fresh world. Every lane of
/// every stair in a deep bunker, a house, a city block, an office tower, a spaceport and a research
/// base is walked up and down holding W, never Space: a lip anywhere (a step's edge, the floor past
/// the top, the ground at the head of a stair dug into it) stops the walker and fails the run, with
/// what it ran into. Every ladder in a mine, a sewage works, a grain elevator, a refinery and a fire
/// station lies flat against its wall, and is climbed from the floor at its foot to the floor at its
/// top and back down: W up it and over the top, S down it without letting go, and off at the foot.
/// A ladder built on a base's wall is climbed the same way.
///
/// Run: .\tools\test.ps1 -Project prototypes/slice -Scene res://tests/ClimbTest.tscn
/// (-- --stairs a,b / --ladders a,b: only those families, none of the other kind; --built: only the
/// built ladder; --minutes N: the watchdog)
/// </summary>
public partial class ClimbTest : Node
{
    const int WatchdogMinutes = 30;
    IEnumerator<int> _run;
    int _failures, _passes;
    readonly Stopwatch _clock = Stopwatch.StartNew();

    static readonly string[] StairFamilies = { "deep_bunker", "city_house", "city_midrise", "city_tower", "abandoned_spaceport", "research_base" };
    static readonly string[] LadderFamilies = { "old_mine", "sewage_works", "grain_elevator", "refinery", "city_fire" };

    public override void _Ready()
    {
        string[] args = OS.GetCmdlineUserArgs();
        int m = Array.IndexOf(args, "--minutes");
        int minutes = m >= 0 && m + 1 < args.Length && int.TryParse(args[m + 1], out int given) ? given : WatchdogMinutes;
        var thread = new System.Threading.Thread(() =>
        {
            System.Threading.Thread.Sleep(TimeSpan.FromMinutes(minutes));
            Console.Error.WriteLine($"FAIL  the run took over {minutes} minutes: watchdog");
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
        string[] Arg(string name, string[] all)
        {
            int at = Array.IndexOf(args, name);
            if (at >= 0 && at + 1 < args.Length) return args[at + 1].Split(',');
            return args.Any(a => a is "--stairs" or "--ladders" or "--built" or "--landing") ? Array.Empty<string>() : all;
        }
        if (Array.IndexOf(args, "--landing") >= 0)
        {
            foreach (string family in LandingFamilies)
                foreach (int x in Place(family, Landing)) yield return x;
            yield break;
        }
        foreach (string family in Arg("--stairs", StairFamilies))
            foreach (int x in Place(family, Stairs)) yield return x;
        foreach (string family in Arg("--ladders", LadderFamilies))
            foreach (int x in Place(family, Ladders)) yield return x;
        if (Array.IndexOf(args, "--built") >= 0 || !args.Any(a => a is "--stairs" or "--ladders"))
            foreach (int x in Built()) yield return x;
    }

    /// <summary>A fresh world with the family's place seated near the start, stood up round the survivor, and then the walk.</summary>
    IEnumerable<int> Place(string family, Func<Game.Game, Driver, Place, IEnumerable<int>> walk)
    {
        PoiSiting.ForceFamily = family;
        PoiSiting.ForceState = P.PoiState.Abandoned;
        var game = new Game.Game { Interactive = false, World = WorldChoice.Fresh(), SkipIntro = true };
        AddChild(game);
        var drive = new Driver(game);
        foreach (int x in drive.UntilPlaying()) yield return x;
        PoiSiting.ForceFamily = null;
        PoiSiting.ForceState = null;
        game.Zone.Weather.Bridge.Calm = true;
        game.Zone.Wildlife.Frozen = true;
        GD.Print($"-- {family}, {game.World} --");
        PoiSite site = game.Zone.Planet.Pois.FirstOrDefault(s => s.Family.Id == family);
        if (site == null) { GD.Print($"  (no {family} could be seated on this world: on to the next)"); game.QueueFree(); yield return 0; yield break; }
        Vector3 entrance = game.Zone.Planet.NearestCopy(new Vector3(site.Entrance.X + 0.5f, site.Entrance.Y + 0.2f, site.Entrance.Z + 0.5f), game.Survivor.WorldFeet);
        game.Survivor.PlaceOutside(entrance.ToNumerics());
        var waited = Stopwatch.StartNew();
        while (waited.Elapsed.TotalSeconds < 120 && game.Places.PlaceOf(site) is not { Ready: true }) yield return 0;
        Place place = game.Places.PlaceOf(site);
        Check(place is { Ready: true }, $"{site.Name} stands up");
        if (place is { Ready: true })
            foreach (int x in walk(game, drive, place)) yield return x;
        game.QueueFree();
        for (int i = 0; i < 5; i++) yield return 0;
    }

    /// <summary>Puts the survivor on a floor (setting up, not walking) and waits for the ground under it.</summary>
    static IEnumerable<int> Stand(Game.Game game, Driver drive, Vector3 feet)
    {
        game.Survivor.PlaceOutside((feet + Vector3.Up * 0.05f).ToNumerics());
        for (int i = 0; i < 600 && !game.Zone.Streamer.ReadyAround(feet, 1, needCollision: true); i++) yield return 0;
        foreach (int x in drive.Wait(20)) yield return x;
    }

    // ---- Stairs ---------------------------------------------------------------------------------

    /// <summary>One lane of a flight: its lowest and highest step, which way it rises, the floor before its foot and past its top.</summary>
    readonly record struct Lane(P.Int3 Bottom, P.Int3 Top, P.Dir Rises, P.Int3 Foot, P.Int3 Head, int Steps);

    static List<Lane> Lanes(Place place)
    {
        var g = place.Inst.Blocks;
        var nav = place.Nav();
        bool Same(P.Int3 c, P.Dir d) => g.Get(c) is { Type: P.BlockType.Stair } b && b.Facing == d;
        var lanes = new List<Lane>();
        foreach (var (cell, block) in g.Blocks())
        {
            if (block.Type != P.BlockType.Stair) continue;
            P.Dir d = block.Facing;
            P.Int3 f = d.Vec();
            if (Same(cell - f + P.Int3.Down, d)) continue; // not the foot of its lane
            P.Int3 top = cell;
            int n = 1;
            while (Same(top + f + P.Int3.Up, d)) { top += f + P.Int3.Up; n++; }
            P.Int3 foot = cell - f, head = top + f + P.Int3.Up;
            if (!nav.Standable(foot) || !nav.Standable(head) || nav.RampAt(foot, out _) || nav.RampAt(head, out _)) continue; // a flight that turns onto another is walked from its own ends
            lanes.Add(new Lane(cell, top, d, foot, head, n));
        }
        return lanes;
    }

    IEnumerable<int> Stairs(Game.Game game, Driver drive, Place place)
    {
        List<Lane> lanes = Lanes(place);
        int up = 0, down = 0;
        var stops = new List<string>();
        var nav = place.Nav();
        foreach (Lane lane in lanes)
        {
            Vector3 foot = PlaceWalk.Feet(place, lane.Foot), head = PlaceWalk.Feet(place, lane.Head);
            // A stride back from the foot and on past the top, where there is floor, so the walk is under way when it meets the step.
            P.Int3 back = lane.Foot - lane.Rises.Vec(), past = lane.Head + lane.Rises.Vec();
            // (Not into a doorway: a shut door there is opened with E, which is no part of walking a stair.)
            bool InDoor(P.Int3 c) => place.Doors.Any(d => d.Cells.Contains(c) || d.Cells.Contains(c + P.Int3.Up));
            Vector3 start = nav.Standable(back) && !InDoor(back) ? PlaceWalk.Feet(place, back) : foot;
            Vector3 beyond = nav.Standable(past) && !InDoor(past) ? PlaceWalk.Feet(place, past) : head;
            foreach (int x in Stand(game, drive, start)) yield return x;
            string note = null;
            foreach (int x in Stroll(game, drive, place, beyond, lane.Steps, r => note = r)) yield return x;
            if (note == null) up++;
            else
            {
                stops.Add($"up {lane.Bottom}..{lane.Top} rising {lane.Rises}: {note}");
                foreach (int x in Stand(game, drive, beyond)) yield return x;
            }
            note = null;
            foreach (int x in Stroll(game, drive, place, start, lane.Steps, r => note = r)) yield return x;
            if (note == null) down++;
            else stops.Add($"down {lane.Top}..{lane.Bottom}: {note}");
        }
        int cells = place.Inst.Blocks.Blocks().Count(b => b.Block.Type == P.BlockType.Stair);
        Check(lanes.Count > 0 || cells == 0, $"{place.Site.Name}: {lanes.Count} lanes of stairs to walk ({cells} stair cells)");
        Check(stops.Count == 0, $"{place.Site.Name}: up {up} and down {down} of {lanes.Count} lanes at a walk, no jump{(stops.Count > 0 ? $"; stopped: {string.Join(" | ", stops.Take(6))}" : "")}");
    }

    /// <summary>Walks (W only, never Space) to a point. Null if it gets there; else what stopped it.</summary>
    static IEnumerable<int> Stroll(Game.Game game, Driver drive, Place place, Vector3 to, int steps, Action<string> done)
    {
        var c = game.Controls;
        int ticks = (int)((6f + steps * 1.2f) * 60);
        float best = float.MaxValue, still = 0f;
        string note = "out of time";
        for (int t = 0; t < ticks; t++)
        {
            Vector3 feet = game.Survivor.WorldFeet;
            float flat = new Vector2(to.X - feet.X, to.Z - feet.Z).Length();
            float dist = (to - feet).Length();
            if (flat < 0.3f && MathF.Abs(to.Y - feet.Y) < 0.35f) { note = null; break; }
            drive.Face(new Vector3(to.X, game.Survivor.WorldEye().Y, to.Z));
            c.Hold(Key.W, true);
            c.Hold(Key.Shift, false);
            c.Hold(Key.Space, false);
            if (dist < best - 0.05f) { best = dist; still = 0f; }
            else still += 1f / 60f;
            if (still > 1.0f)
            {
                note = Stuck(game, place, to);
                break;
            }
            yield return 0;
        }
        c.Hold(Key.W, false);
        done(note);
    }

    /// <summary>Where the walker stands stuck and what it presses against.</summary>
    static string Stuck(Game.Game game, Place place, Vector3 to)
    {
        var s = game.Survivor;
        var ch = s.Binding.Character;
        Vector3 feet = s.WorldFeet;
        Vector3 l = place.FromWorld(feet);
        var hits = new List<string>();
        for (int i = 0; i < ch.GetSlideCollisionCount(); i++)
        {
            KinematicCollision3D k = ch.GetSlideCollision(i);
            hits.Add($"n {k.GetNormal():F2} at {place.FromWorld(k.GetPosition()):F2} on {(k.GetCollider() as Node)?.Name}");
        }
        // The floor along the way, every 0.15 m.
        Vector3 dir = ((to - feet) with { Y = 0f }).Normalized();
        var space = game.Zone.GetWorld3D().DirectSpaceState;
        var heights = new List<string>();
        for (int i = -2; i <= 6; i++)
        {
            Vector3 from = feet + dir * (i * 0.15f) + Vector3.Up * 1.2f;
            var r = space.IntersectRay(PhysicsRayQueryParameters3D.Create(from, from + Vector3.Down * 2.5f, Layers.World | Layers.Props));
            heights.Add(r.Count == 0 ? "-" : $"{place.FromWorld(r["position"].AsVector3()).Y:F2}{((r["collider"].AsGodotObject() as Node)?.Name.ToString().StartsWith("Blocks") == true ? "p" : "t")}");
        }
        return $"stuck at {l:F2} (cell {place.CellAt(feet)}), on floor {s.Body.OnFloor}, v {s.Body.Velocity.ToGodot():F2} (move {s.Legs.Move}, climb {s.Legs.Climb}, crouched {s.Crouched}, asleep {s.Body.Asleep}, in worms {game.InWorms}, dead {game.Suit.Dead}, steps {s.Binding.StepsTaken}); touching {(hits.Count == 0 ? "nothing" : string.Join("; ", hits))}; floor ahead from -0.3 m (p place, t terrain): {string.Join(" ", heights)}";
    }

    // ---- Ladders --------------------------------------------------------------------------------

    /// <summary>A run of ladder up a column: its lowest and highest cell, the way it looks out from its wall, and the floors you get on and off it from.</summary>
    readonly record struct Run(P.Int3 Low, P.Int3 High, P.Dir Out, P.Int3? Foot, P.Int3? Top);

    static bool Wall(Place place, P.NavGrid nav, P.Int3 c)
    {
        P.Block b = place.Inst.Blocks.Get(c);
        if (b.IsAir) return nav.IsRock(c);
        return P.BlockInfo.IsFullCube(b.Type) || b.Type is P.BlockType.Lattice or P.BlockType.Tank or P.BlockType.Machine or P.BlockType.SealedDoor or P.BlockType.Shelf or P.BlockType.Growth;
    }

    /// <summary>A floor to step off onto at a ladder's top that the place's routes refuse only for low debris lying on it (the walk steps over it).</summary>
    static bool Landing(Place place, P.Int3 high, P.Int3 off)
    {
        var g = place.Inst.Blocks;
        bool Open(P.Int3 c) => g.Get(c) is var b && (!P.BlockInfo.Collides(b.Type) || b.Type is P.BlockType.Rubble);
        P.Block under = g.Get(off + P.Int3.Down);
        return off.Y == high.Y + 1 && Open(off) && Open(off + P.Int3.Up) && Open(high + P.Int3.Up) && Open(high + P.Int3.Up * 2)
               && g.Get(off).Type != P.BlockType.Ladder && P.BlockInfo.Supports(under.Type);
    }

    static List<Run> Runs(Place place)
    {
        var g = place.Inst.Blocks;
        var nav = place.Nav();
        var runs = new List<Run>();
        foreach (var (cell, block) in g.Blocks())
        {
            if (block.Type != P.BlockType.Ladder || g.Get(cell + P.Int3.Down).Type == P.BlockType.Ladder) continue;
            P.Int3 high = cell;
            while (g.Get(high + P.Int3.Up).Type == P.BlockType.Ladder) high += P.Int3.Up;
            P.Dir o = block.Facing;
            // On at the foot from the floor beside it, in front first; off at the top a level up and aside, as the place's own walkers go.
            P.Int3? foot = null, top = null;
            foreach (P.Dir d in new[] { o, o.RotateCw(1), o.RotateCw(3), o.Opposite() })
            {
                P.Int3 a = cell + d.Vec();
                if (foot == null && nav.Standable(a) && !nav.LadderAt(a) && !nav.RampAt(a, out _) && nav.Standable(cell)) foot = a;
                P.Int3 off = high + P.Int3.Up + d.Vec();
                if (top == null && (nav.Neighbours(high).Contains(off) || Landing(place, high, off))) top = off;
            }
            runs.Add(new Run(cell, high, o, foot, top));
        }
        return runs;
    }

    IEnumerable<int> Ladders(Game.Game game, Driver drive, Place place)
    {
        List<Run> runs = Runs(place);
        var nav = place.Nav();
        Where = w => $"{place.FromWorld(w):F2} in the place";
        // Flat against a wall: whatever stands beside a run, its back is to the most of it.
        var askew = runs.Where(r =>
        {
            int Along(P.Dir d) { int n = 0; for (P.Int3 p = r.Low; p.Y <= r.High.Y; p += P.Int3.Up) if (Wall(place, nav, p + d.Vec())) n++; return n; }
            int back = Along(r.Out.Opposite());
            return new[] { r.Out, r.Out.RotateCw(1), r.Out.RotateCw(3) }.Any(d => Along(d) > back);
        }).ToList();
        Check(askew.Count == 0, $"{place.Site.Name}: all {runs.Count} ladders lie flat against their walls{(askew.Count > 0 ? $"; askew: {string.Join(" ", askew.Take(6).Select(r => $"{r.Low} out {r.Out}"))}" : "")}");
        var climbable = runs.Where(r => r.Foot != null && r.Top != null).ToList();
        int ups = 0, downs = 0;
        var stops = new List<string>();
        foreach (Run run in climbable)
        {
            string note = null;
            foreach (int x in Stand(game, drive, PlaceWalk.Feet(place, run.Foot.Value))) yield return x;
            foreach (int x in ClimbUp(game, drive, place, run, r => note = r)) yield return x;
            if (note == null) ups++;
            else
            {
                stops.Add($"up {run.Low}..{run.High}: {note}");
                foreach (int x in Stand(game, drive, PlaceWalk.Feet(place, run.Top.Value))) yield return x;
            }
            note = null;
            foreach (int x in ClimbDown(game, drive, place, run, r => note = r)) yield return x;
            if (note == null) downs++;
            else stops.Add($"down {run.High}..{run.Low}: {note}");
        }
        Check(runs.Count > 0 && climbable.Count == runs.Count, $"{place.Site.Name}: {climbable.Count} of {runs.Count} ladders have a floor at the foot and the top" +
            string.Concat(runs.Where(r => r.Foot == null || r.Top == null).Take(4).Select(r => $"; {r.Low}..{r.High} out {r.Out} foot {r.Foot?.ToString() ?? "none"} top {r.Top?.ToString() ?? "none"}, beside the foot {string.Join(" ", new[] { r.Out, r.Out.RotateCw(1), r.Out.RotateCw(3), r.Out.Opposite() }.Select(d => $"{d}:{place.Inst.Blocks.Get(r.Low + d.Vec()).Type}/{place.Inst.Blocks.Get(r.Low + d.Vec() + P.Int3.Down).Type}{(place.Inst.Nav().Standable(r.Low + d.Vec()) ? " bare-standable" : "")}"))}, under it {place.Inst.Blocks.Get(r.Low + P.Int3.Down).Type}, off the top {string.Join(" ", new[] { r.Out.Opposite(), r.Out.RotateCw(1), r.Out.RotateCw(3) }.Select(d => $"{d}:{string.Join("/", Enumerable.Range(-1, 3).Select(k => place.Inst.Blocks.Get(r.High + P.Int3.Up * (1 + k) + d.Vec()).Type))}"))}, over it {string.Join(" ", Enumerable.Range(1, 3).Select(k => place.Inst.Blocks.Get(r.High + P.Int3.Up * k).Type))}")));
        Check(stops.Count == 0, $"{place.Site.Name}: up {ups} and down {downs} of {climbable.Count} ladders, on and off at both ends{(stops.Count > 0 ? $"; stopped: {string.Join(" | ", stops.Take(6))}" : "")}");
    }

    /// <summary>From the floor at the foot: face the rungs, W up to the top, and over it onto the floor there.</summary>
    static IEnumerable<int> ClimbUp(Game.Game game, Driver drive, Place place, Run run, Action<string> done)
    {
        Vector3 o = Level(run.Out);
        Vector3 column = place.ToWorld(new Vector3(run.Low.X + 0.5f, 0f, run.Low.Z + 0.5f));
        float topY = place.ToWorld(new Vector3(0, run.High.Y + 1, 0)).Y;
        Vector3 off = PlaceWalk.Feet(place, run.Top.Value);
        string note = null;
        foreach (int x in Hold(game, drive, () => column + Vector3.Up * (game.Survivor.WorldEye().Y - column.Y), Key.W,
                     () => game.Survivor.Legs.Climb == ClimbMode.Ladder, () => -Flat(game, column), 6f, r => note = r))
            yield return x;
        if (note != null) { done($"onto it at the foot ({note})"); yield break; }
        foreach (int x in Hold(game, drive, () => column - o + Vector3.Up * (game.Survivor.WorldEye().Y - column.Y), Key.W,
                     () => game.Survivor.WorldFeet.Y >= topY - 0.1f, () => game.Survivor.WorldFeet.Y, 12f + (run.High.Y - run.Low.Y) * 0.8f, r => note = r))
            yield return x;
        if (note != null) { done($"climbing ({note})"); yield break; }
        foreach (int x in Hold(game, drive, () => off + Vector3.Up * 1.6f, Key.W,
                     // The landing may hold low debris (Landing lets rubble lie there): standing on it is off at the top too.
                     () => Flat(game, off) < 0.35f && game.Survivor.WorldFeet.Y > off.Y - 0.4f && game.Survivor.WorldFeet.Y < off.Y + 0.6f && game.Survivor.Body.OnFloor,
                     () => -Flat(game, off), 6f, r => note = r))
            yield return x;
        done(note == null ? null : $"over the top ({note})");
    }

    /// <summary>From the floor at the top: W out over the ladder until it holds you, turn to the rungs, S down to the floor, and off to the floor beside the foot.</summary>
    static IEnumerable<int> ClimbDown(Game.Game game, Driver drive, Place place, Run run, Action<string> done)
    {
        Vector3 o = Level(run.Out);
        Vector3 column = place.ToWorld(new Vector3(run.Low.X + 0.5f, run.High.Y + 1, run.Low.Z + 0.5f));
        float footY = place.ToWorld(new Vector3(0, run.Low.Y, 0)).Y;
        Vector3 foot = PlaceWalk.Feet(place, run.Foot.Value);
        string note = null;
        foreach (int x in Hold(game, drive, () => column + Vector3.Up * 1.6f, Key.W,
                     () => game.Survivor.Legs.Climb == ClimbMode.Ladder && Flat(game, column) < 0.3f, () => -Flat(game, column), 6f, r => note = r))
            yield return x;
        if (note != null) { done($"onto it from the top ({note})"); yield break; }
        foreach (int x in Hold(game, drive, () => column - o + Vector3.Up * (game.Survivor.WorldEye().Y - column.Y), Key.S,
                     () => game.Survivor.Body.OnFloor && game.Survivor.WorldFeet.Y < footY + 0.2f, () => -game.Survivor.WorldFeet.Y, 12f + (run.High.Y - run.Low.Y) * 0.8f, r => note = r))
            yield return x;
        if (note != null) { done($"climbing down ({note}, climb {game.Survivor.Legs.Climb})"); yield break; }
        // Off it still facing the rungs, as a climber does: back (S) when the floor is in front of it, aside (A or D) when it is beside it.
        Vector3 toFoot = (foot - column) with { Y = 0f };
        Vector3 right = (-o).Cross(Vector3.Up);
        Key off = toFoot.Dot(o) > 0.5f ? Key.S : toFoot.Dot(right) > 0f ? Key.D : Key.A;
        foreach (int x in Hold(game, drive, () => column - o + Vector3.Up * (game.Survivor.WorldEye().Y - column.Y), off,
                     () => game.Survivor.Legs.Climb == ClimbMode.None, () => -Flat(game, foot), 4f, r => note = r))
            yield return x;
        if (note == null)
            foreach (int x in Hold(game, drive, () => foot + Vector3.Up * 1.6f, Key.W, () => Near(game, foot), () => -Flat(game, foot), 6f, r => note = r))
                yield return x;
        done(note == null ? null : $"off at the foot with {off} ({note}, climb {game.Survivor.Legs.Climb})");
    }

    static Vector3 Level(P.Dir d) { P.Int3 v = d.Vec(); return new Vector3(v.X, 0f, v.Z); }
    static float Flat(Game.Game game, Vector3 to) => new Vector2(to.X - game.Survivor.WorldFeet.X, to.Z - game.Survivor.WorldFeet.Z).Length();
    static bool Near(Game.Game game, Vector3 to) => Flat(game, to) < 0.35f && MathF.Abs(to.Y - game.Survivor.WorldFeet.Y) < 0.4f;

    /// <summary>Looks at a point and holds a key until done, failing if what it measures stops getting better for a second and a half or the time runs out.</summary>
    static IEnumerable<int> Hold(Game.Game game, Driver drive, Func<Vector3> look, Key key, Func<bool> there, Func<float> progress, float seconds, Action<string> done)
    {
        var c = game.Controls;
        float best = float.MinValue, still = 0f;
        string note = "out of time", first = null;
        for (int t = 0; t < seconds * 60; t++)
        {
            if (there()) { note = null; break; }
            drive.Face(look());
            c.Hold(key, true);
            if (t <= 1) first += $"(t{t} on floor {game.Survivor.Body.OnFloor}, climb {game.Survivor.Legs.Climb}, below {game.Survivor.Legs.Ladder.Below}, backed {game.Survivor.Legs.Ladder.Backed}, move {game.Survivor.Legs.Move}, yaw {game.Survivor.Legs.Yaw:F2})";
            float p = progress();
            if (p > best + 0.05f) { best = p; still = 0f; }
            else still += 1f / 60f;
            if (still > 1.5f) { note = "stopped getting anywhere"; break; }
            yield return 0;
        }
        c.Hold(key, false);
        LadderHold l = game.Survivor.Legs.Ladder;
        if (note != null) note += $" at {Where?.Invoke(game.Survivor.WorldFeet) ?? $"{game.Survivor.WorldFeet:F2}"}, on floor {game.Survivor.Body.OnFloor}, climb {game.Survivor.Legs.Climb}, v {game.Survivor.Body.Velocity.ToGodot():F2}"
                                  + $", ladder out {l.Out.ToGodot():F1} backed {l.Backed} below {l.Below} top {l.Top}, first {first}";
        done(note);
    }

    /// <summary>Where a stop is told, in the frame of what is being climbed.</summary>
    static Func<Vector3, string> Where;

    // ---- A built ladder -------------------------------------------------------------------------

    /// <summary>
    /// A base on open ground: a floor, a wall two blocks tall with a deck on it, and a ladder on the
    /// wall's face, its back to the wall. It climbs as a place's ladder does, up and over onto the
    /// deck and back down to the floor.
    /// </summary>
    IEnumerable<int> Built()
    {
        var game = new Game.Game { Interactive = false, World = WorldChoice.Fresh(), SkipIntro = true };
        AddChild(game);
        var drive = new Driver(game);
        foreach (int x in drive.UntilPlaying()) yield return x;
        game.Zone.Weather.Bridge.Calm = true;
        game.Zone.Wildlife.Frozen = true;
        GD.Print($"-- a built ladder, {game.World} --");
        S.Catalogue cat = game.Zone.Data.Catalogue;
        var s = new S.Structure(cat) { Name = "Ladder test" };
        S.BlockType steel = cat.Type("steel"), ladderType = cat.Type("ladder");
        for (int x = -2; x <= 2; x++)
            for (int z = -3; z <= 3; z++) s.Set(new S.Int3(x, 0, z), S.Building.Make(s, steel));
        for (int x = -1; x <= 1; x++)
            for (int y = 1; y <= 2; y++)
                for (int z = 1; z <= 2; z++) s.Set(new S.Int3(x, y, z), S.Building.Make(s, steel));
        // Mounted by its +z face on the wall at +z: rails up, rungs flat on the wall.
        int rot = S.Orientations.Identity;
        for (int y = 1; y <= 2; y++) s.Set(new S.Int3(0, y, 0), S.Building.Make(s, ladderType, null, rot));
        s.DirtyChunks.Clear();
        float cell = s.Grid.CellSize;
        var play = new Playthrough(game);
        Vector3 site = play.FlatSpot(game.Start, 40f, 0.4f, 0.5f, span: 12f);
        // Stood up clear of the ground (a base holds where it is put), so no bank or tunnel round it decides the climb.
        float highest = float.MinValue;
        for (int i = -8; i <= 8; i += 2)
            for (int k = -8; k <= 8; k += 2) highest = MathF.Max(highest, game.Zone.GroundAt(site.X + i, site.Z + k).Y);
        site.Y = highest + 2f;
        game.Survivor.PlaceOutside((site + new Vector3(0f, 1f, -12f)).ToNumerics());
        foreach (int x in drive.Wait(30)) yield return x;
        Vessel v = Vessel.SpawnBase(game.Zone, s, (site + Vector3.Up * (cell * 0.5f + 0.05f)).ToNumerics(), 0f);
        foreach (int x in drive.Wait(60)) yield return x;
        Vector3 W(float x, float y, float z) => v.Body.ToParent(new System.Numerics.Vector3(x, y, z) * cell).ToGodot();
        Where = w => $"{(v.Body.ToLocal(w.ToNumerics()) / cell).ToGodot():F2} in the base's cells (riding it {game.Survivor.Body.Frame == v.Body})";
        Vector3 floor = W(0f, 0.5f, -2f), deck = W(0f, 2.5f, 1.5f);
        Vector3 into = W(0f, 0.5f, 0.5f) - W(0f, 0.5f, 0f);
        // Up: from the floor in front, W into it and up, and over the top onto the deck.
        foreach (int x in Stand(game, drive, floor)) yield return x;
        string note = null;
        foreach (int x in Hold(game, drive, () => game.Survivor.WorldEye() + into * 4f, Key.W, () => game.Survivor.WorldFeet.Y > deck.Y - 0.1f,
                     () => game.Survivor.WorldFeet.Y, 20f, r => note = r))
            yield return x;
        if (note == null)
            foreach (int x in Hold(game, drive, () => deck + Vector3.Up * 1.6f, Key.W, () => Near(game, deck) && game.Survivor.Body.OnFloor, () => -Flat(game, deck), 6f, r => note = r))
                yield return x;
        bool up = note == null;
        Check(up, $"up the built ladder holding W and over its top onto the deck ({note ?? "made it"}; climbing {game.Survivor.Legs.Climb})");
        if (!up) foreach (int x in Stand(game, drive, deck)) yield return x;
        // Down: out over it from the deck, turn to the wall, S down to the floor, and off.
        Vector3 column = W(0f, 2.5f, 0f);
        note = null;
        foreach (int x in Hold(game, drive, () => column + Vector3.Up * 1.6f, Key.W, () => game.Survivor.Legs.Climb == ClimbMode.Ladder && Flat(game, column) < 0.4f, () => -Flat(game, column), 6f, r => note = r))
            yield return x;
        if (note == null)
            foreach (int x in Hold(game, drive, () => game.Survivor.WorldEye() + into * 4f, Key.S, () => game.Survivor.Body.OnFloor && game.Survivor.WorldFeet.Y < floor.Y + 0.3f,
                         () => -game.Survivor.WorldFeet.Y, 20f, r => note = r))
                yield return x;
        if (note == null)
            foreach (int x in Hold(game, drive, () => floor + Vector3.Up * 1.6f, Key.W, () => Near(game, floor), () => -Flat(game, floor), 6f, r => note = r))
                yield return x;
        Check(note == null, $"down the built ladder: onto it from the deck, S down without letting go, and off at its foot ({note ?? "made it"})");
        game.QueueFree();
        for (int i = 0; i < 5; i++) yield return 0;
    }
}
