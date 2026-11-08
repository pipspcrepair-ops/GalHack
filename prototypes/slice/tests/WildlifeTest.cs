using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Godot;
using Slice.Game;
using Wildlife;

namespace Slice.Tests;

/// <summary>
/// Animals round the start of a real game: fresh worlds (two temperate, one meadow and one other
/// type, each a fresh seed), each started as a played game starts it, and in its first minute
/// of play the creatures living within 300 m of the castaway are counted, alive and drawn. Nothing
/// is spawned by the test. Each world prints what it rolled (its kinds, by body plan) and the
/// counts every 10 s.
///
/// Run: .\tools\test.ps1 -Project prototypes/slice -Scene res://tests/WildlifeTest.tscn
/// (-- --types temperate,meadow,arid: those types in turn)
/// </summary>
public partial class WildlifeTest : Node
{
    const int WatchdogMinutes = 25;
    IEnumerator<int> _run;
    int _failures, _passes;
    readonly Stopwatch _clock = Stopwatch.StartNew();

    /// <summary>The fewest animals (not people, alive and drawn) that must be in view within 300 m of the start in its first minute on a temperate world.</summary>
    public const int FewestNearStart = 4;

    public override void _Ready()
    {
        GalHack.Testing.Backstop.Arm(1, WatchdogMinutes * 60); // the wall-clock limit: the run ends itself, failed, if it is still going
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

    IEnumerable<int> Scenario()
    {
        string[] args = OS.GetCmdlineUserArgs();
        string[] types = { "meadow", "temperate", "random" };
        for (int i = 0; i < args.Length - 1; i++)
        {
            if (args[i] == "--types") types = args[i + 1].Split(',');
            if (args[i] == "--shots" && DisplayServer.GetName() != "headless") Shots = args[i + 1];
        }
        Walk = Array.IndexOf(args, "--no-walk") < 0;
        foreach (string type in types)
            foreach (int x in OneWorld(type)) yield return x;
    }

    /// <summary>Where a windowed run saves its look at an animal (-- --shots dir); none, no look.</summary>
    string Shots;

    /// <summary>Each world is walked after its first minute (-- --no-walk: not), this many legs of about 220 m.</summary>
    bool Walk = true;
    const int Legs = 4;

    /// <summary>On a walk over a temperate world, the median count of animals drawn within 150 m (scaled by a harsher world's livability).</summary>
    public const int FewestOnTheWay = 6;

    bool _shotSaved;

    static bool Animal(Creature c) => c.Species.Figure == null;

    IEnumerable<int> OneWorld(string type)
    {
        var game = new Game.Game { Interactive = false, World = WorldChoice.Fresh(type) };
        AddChild(game);
        yield return 0;
        WorldChoice world = game.World;
        GD.Print($"-- {world} ({type}) --");
        var watch = Stopwatch.StartNew();
        bool spawned = false;
        while (game.Phase != Phase.Playing && watch.Elapsed.TotalSeconds < 150)
        {
            if (!spawned && game.Zone?.Wildlife?.Sim is WildSim first)
            {
                spawned = true;
                GD.Print($"  the patch spawned: {first.Creatures.Count} creatures ({first.Creatures.Count(c => Animal(c) && !c.Species.Flies)} ground animals, {first.Creatures.Count(c => c.Species.Flies)} fliers, {first.Creatures.Count(c => !Animal(c))} people), {first.Creatures.Count(c => c.Dead)} dead");
            }
            if (game.Zone.IsBuilt && game.Bunker == null) break;
            yield return 0;
        }
        if (game.Zone?.Wildlife?.Sim is WildSim woke)
            GD.Print($"  at waking (sim time {woke.Time:F0} s): {woke.Creatures.Count} creatures, {woke.Creatures.Count(c => c.Dead)} dead, {woke.Creatures.Count(c => c.Removed)} removed");
        if (game.Phase != Phase.Playing)
        {
            GD.Print($"  (no start on {world}: skipped)");
            game.QueueFree();
            yield return 0;
            yield break;
        }
        Surface.WildlifePatch patch = game.Zone.Wildlife;
        WorldLife life = patch.Life;
        var kinds = life.Fauna.Species;
        GD.Print($"  livable {patch.Livable:F2}, layers: {game.Zone.Planet.Reading}");
        GD.Print($"  {kinds.Count} kinds: {string.Join("; ", kinds.Select(s => $"{s.CommonName} ({s.Plan}{(s.Figure != null ? ", person" : "")}{(s.Flies ? ", flies" : "")}, x{s.Scarcity:F2})"))}");
        int animalKinds = kinds.Count(s => s.Figure == null);
        Check(animalKinds >= 2, $"{world}: the world has {animalKinds} kinds of animal");

        // The first minute of play, standing at the start.
        int bestSeen = 0;
        var followed = patch.Sim.Creatures.Where(c => Animal(c) && !c.Species.Flies).Take(3).ToList();
        foreach (Creature c in followed) GD.Print($"  follow #{c.Id} {c.Species.CommonName} ({c.Species.Plan}, {c.Species.Rig})");
        for (int tick = 0; tick <= 60 * 60; tick++)
        {
            if (tick % 600 == 0 || tick == 60 * 60)
            {
                var (alive, near, drawn, people, dead, removed) = Count(game, patch);
                bestSeen = Math.Max(bestSeen, drawn);
                GD.Print($"  t {tick / 60,2} s: {patch.Sim.Creatures.Count} in the sim ({alive} animals alive, {people} people, {dead} dead, {removed} removed); {near} animals within 300 m, {drawn} drawn; views {patch.View.ViewCount}");
            }
            if (tick is 0 or 3600)
                foreach (Creature c in followed)
                {
                    CreatureView v = patch.View.ViewOf(c);
                    float ground = patch.Sim.Terrain.Height(c.Position.X, c.Position.Z);
                    GD.Print($"    tick {tick}: #{c.Id} at {c.Position.X:F0},{c.Position.Y:F2},{c.Position.Z:F0} ground {ground:F2} health {c.Health:F0} v {c.Velocity.Length():F1} {(c.Dead ? "DEAD" : "")}{(c.Removed ? " REMOVED" : "")}{(c.Thrown ? " thrown" : "")} state {c.State}; view {(v == null ? "none" : $"{(v.Visible ? "shown" : "hidden")} {v.Shows:F2}")}");
                }
            yield return 0;
        }
        if (world.Archetype == Planets.Gen.Archetype.Temperate)
            Check(bestSeen >= FewestNearStart, $"{world}: {bestSeen} animals drawn within 300 m of the start in the first minute (at least {FewestNearStart})");
        // On screen, really: the nearest ground animals, each looked at from 12 m, drawn with and without the creatures.
        if (DisplayServer.GetName() != "headless")
        {
            Vector3 feet0 = patch.PatchLocal(game.Survivor.WorldFeet);
            var nearest = patch.Sim.Creatures.Where(c => !c.Removed && !c.Dead && Animal(c) && !c.Species.Flies).OrderBy(c => WildSim.Flat(c.Position - feet0).Length()).Take(3).ToList();
            int onScreen = 0;
            foreach (Creature seen in nearest)
            {
                Vector3 at = patch.FromPatch(seen.Position) + Vector3.Up * MathF.Max(0.3f, seen.Species.BodyLength * 0.3f);
                Vector3 eye = at + new Vector3(8.5f, 0f, 8.5f);
                eye.Y = MathF.Max(game.Zone.GroundAt(eye.X, eye.Z).Y + 1.7f, at.Y + 2f);
                game.FixedCamera = new Transform3D(Basis.LookingAt(at - eye, Vector3.Up), eye);
                for (int i = 0; i < 20; i++) yield return 0;
                Image with = GetViewport().GetTexture().GetImage();
                patch.View.Visible = false;
                for (int i = 0; i < 3; i++) yield return 0;
                Image without = GetViewport().GetTexture().GetImage();
                patch.View.Visible = true;
                int pixels = Differ(with, without);
                if (pixels > 30) onScreen++;
                float ground = patch.Sim.Terrain.Height(seen.Position.X, seen.Position.Z), natural = game.Zone.GroundAt(patch.FromPatch(seen.Position).X, patch.FromPatch(seen.Position).Z).Y;
                GD.Print($"  look at #{seen.Id} {seen.Species.CommonName} ({seen.Species.BodyLength:F1} m) {WildSim.Flat(seen.Position - feet0).Length():F0} m off: {pixels} pixels of it on screen; feet {patch.FromPatch(seen.Position).Y:F2}, sim ground {ground:F2}, zone ground {natural:F2}");
                if (Shots != null && seen == nearest[0])
                {
                    System.IO.Directory.CreateDirectory(Shots);
                    with.SavePng(System.IO.Path.Combine(Shots, $"probe-{world.Seed}.png"));
                }
            }
            game.FixedCamera = null;
            if (nearest.Count > 0) GD.Print($"  {onScreen} of the {nearest.Count} nearest ground animals really on screen");
        }

        // Out of the bunker and a walk over the land, as a player goes: what is seen on the way.
        if (Walk)
        {
            var drive = new Driver(game);
            var play = new Playthrough(game);
            Slice.Start.Bunker b = game.Bunker;
            foreach (int x in drive.Tap(Key.F)) yield return x;
            foreach (int x in drive.Wait(40)) yield return x;
            foreach (int x in drive.WalkTo(b.CryoFront, 0.3f, 10f)) yield return x;
            foreach (int x in drive.ClimbOut(b)) yield return x;
            GD.Print($"  out on the surface: {drive.OnSurface}");
            int ticks = 0, sawAny = 0, samples = 0;
            var met = new HashSet<Creature>();
            Vector3 facing = b.Outside - b.StairTop;
            var counts = new List<int>();
            int firstPerson = 0, looked = 0;
            for (int leg = 0; leg < Legs; leg++)
            {
                Vector3 from = game.Survivor.WorldFeet;
                Vector3 to = play.AwayFrom(from, facing, 220f);
                facing = (to - from).Rotated(Vector3.Up, leg % 2 == 0 ? 0.6f : -0.9f);
                foreach (int x in drive.WalkTo(to, 4f, 50f, dig: false))
                {
                    if (++ticks % 300 == 0)
                    {
                        Vector3 feet = patch.PatchLocal(game.Survivor.WorldFeet);
                        Vector3 look = WildSim.Flat(game.Survivor.WorldLook()).Normalized();
                        int alive = 0, close = 0, inView = 0, people = 0;
                        foreach (Creature c in patch.Sim.Creatures)
                        {
                            if (c.Removed || c.Dead) continue;
                            if (!Animal(c)) { people++; continue; }
                            alive++;
                            Vector3 d = WildSim.Flat(c.Position - feet);
                            if (d.Length() > 150f) continue;
                            if (!(patch.View.ViewOf(c) is CreatureView v && v.Visible && v.Shows > 0.5f)) continue;
                            close++;
                            met.Add(c);
                            if (d.Length() > 1f && look.Dot(d.Normalized()) > 0.6f) inView++;
                        }
                        samples++;
                        counts.Add(close);
                        if (close > 0) sawAny++;
                        GD.Print($"  walk {ticks / 60,3} s, {new Vector2(game.Survivor.WorldFeet.X - b.Outside.X, game.Survivor.WorldFeet.Z - b.Outside.Z).Length():F0} m out (patch {patch.Patches}): {alive} animals and {people} people in the patch; {close} animals drawn within 150 m, {inView} ahead; {patch.Refilled} groups brought back, {patch.Retired} let go far behind");
                    }
                    yield return x;
                }
                // At the end of each leg, through the castaway's own eyes: turn to the nearest animal and see it on screen.
                if (DisplayServer.GetName() == "headless") continue;
                drive.ReleaseAll();
                Vector3 here = patch.PatchLocal(game.Survivor.WorldFeet);
                Creature near = patch.Sim.Creatures.Where(c => !c.Removed && !c.Dead && Animal(c) && !c.Species.Flies && WildSim.Flat(c.Position - here).Length() is > 6f and < 90f
                        && patch.View.ViewOf(c) is CreatureView { Visible: true } v && v.Shows > 0.9f)
                    .OrderBy(c => WildSim.Flat(c.Position - here).Length()).FirstOrDefault();
                if (near == null) continue;
                foreach (int x in drive.LookAt(patch.FromPatch(near.Position) + Vector3.Up * MathF.Max(0.3f, near.Species.BodyLength * 0.3f), 20)) yield return x;
                for (int i = 0; i < 10; i++) yield return 0;
                Image a = GetViewport().GetTexture().GetImage();
                for (int i = 0; i < 2; i++) yield return 0;
                Image again = GetViewport().GetTexture().GetImage();
                patch.View.Visible = false;
                for (int i = 0; i < 3; i++) yield return 0;
                Image none = GetViewport().GetTexture().GetImage();
                patch.View.Visible = true;
                int pixels = Differ(a, none) - Math.Max(0, Differ(a, again));
                looked++;
                if (pixels > 150) firstPerson++;
                GD.Print($"  first person at leg {leg + 1}: {near.Species.CommonName} {WildSim.Flat(near.Position - here).Length():F0} m off, {pixels} pixels of animals on screen");
                if (Shots != null && pixels > 150 && !_shotSaved)
                {
                    _shotSaved = true;
                    System.IO.Directory.CreateDirectory(Shots);
                    a.SavePng(System.IO.Path.Combine(Shots, "animals-meadow.png"));
                }
            }
            counts.Sort();
            int median = counts.Count == 0 ? 0 : counts[counts.Count / 2];
            GD.Print($"  the walk: animals within 150 m at {sawAny} of {samples} looks (median {median}), {met.Count} different ones, {patch.Refilled} groups brought back; seen in first person at {firstPerson} of {looked} stops");
            if (world.Archetype == Planets.Gen.Archetype.Temperate)
            {
                // Life spawns by place, not evenly (owner, 8 Nov 2026): a walk over poor ground meets less, and a
                // less livable world less again, so the look-by-look count is no measure. Over the walk: animals
                // near at half the looks or more, and many different ones met (something every few minutes).
                float share = 0.5f * Math.Min(1f, patch.Livable);
                int fewestMet = (int)MathF.Ceiling(FewestOnTheWay * 2 * Math.Min(1f, patch.Livable));
                Check(samples > 0 && sawAny >= samples * share && met.Count >= fewestMet,
                    $"{world}: walking the land, animals within 150 m at {sawAny} of {samples} looks (at least {samples * share:F0}), {met.Count} different ones met (at least {fewestMet}), {median} at the median; livable {patch.Livable:F2}");
                if (looked > 0) Check(firstPerson > 0, $"{world}: animals on screen through the castaway's own eyes at {firstPerson} of {looked} stops");
            }
        }
        game.QueueFree();
        for (int i = 0; i < 10; i++) yield return 0;
    }

    /// <summary>Pixels that differ between two frames of the same view.</summary>
    static int Differ(Image a, Image b)
    {
        if (a.GetSize() != b.GetSize()) return -1;
        a.Convert(Image.Format.Rgb8);
        b.Convert(Image.Format.Rgb8);
        byte[] x = a.GetData(), y = b.GetData();
        int n = 0;
        for (int i = 0; i + 2 < x.Length; i += 3)
            if (Math.Abs(x[i] - y[i]) + Math.Abs(x[i + 1] - y[i + 1]) + Math.Abs(x[i + 2] - y[i + 2]) > 24) n++;
        return n;
    }

    /// <summary>Animals alive in the patch, those within 300 m of the castaway, those of them drawn; people; the dead; the removed.</summary>
    static (int alive, int near, int drawn, int people, int dead, int removed) Count(Game.Game game, Surface.WildlifePatch patch)
    {
        int alive = 0, near = 0, drawn = 0, people = 0, dead = 0, removed = 0;
        Vector3 feet = patch.PatchLocal(game.Survivor.WorldFeet);
        foreach (Creature c in patch.Sim.Creatures)
        {
            if (c.Removed) { removed++; continue; }
            if (c.Dead) { dead++; continue; }
            if (!Animal(c)) { people++; continue; }
            alive++;
            if (WildSim.Flat(c.Position - feet).Length() > 300f) continue;
            near++;
            if (patch.View.ViewOf(c) is CreatureView v && v.Visible && v.Shows > 0.5f) drawn++;
        }
        return (alive, near, drawn, people, dead, removed);
    }
}
