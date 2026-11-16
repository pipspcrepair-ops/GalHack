using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using GalHack.Physics;
using GalHack.Physics.Engine;
using Godot;
using Slice.Game;
using Slice.Surface;
using Wildlife;

namespace Slice.Tests;

/// <summary>
/// Nothing walks through a tree (owner: "NPC's ran through trees rather than pathing around them"). The real game on a
/// fresh temperate world, collision and all: the sleeper set down in the thickest wood near the start, hunters and a
/// raiding band sent at them through it from outside, grazers inside it fleeing, and three minutes watched. Every time
/// a person's or an animal's footprint (<see cref="Locomotion.Footprint"/>) comes into a trunk or a rock of the ground's
/// own scatter, it is logged; there must be none, and they must really have gone through the wood to get there.
/// --before: the same with the woods kept out of the creatures' grid (how it was), counted, not checked.
/// --shot=FILE (windowed): a frame of the band among the trees.
///
/// Run: G:\GalHack\.tools\queue\test-q.ps1 -Project prototypes/slice -Scene res://tests/WoodsTest.tscn
/// </summary>
public partial class WoodsTest : Node
{
    IEnumerator<int> _run;
    int _failures, _passes;
    readonly Stopwatch _clock = Stopwatch.StartNew();
    readonly Random _rng = new();
    string[] Args => OS.GetCmdlineUserArgs();

    public override void _Ready()
    {
        var thread = new System.Threading.Thread(() =>
        {
            System.Threading.Thread.Sleep(TimeSpan.FromMinutes(9));
            Console.Error.WriteLine("FAIL  the run took over 9 minutes: watchdog");
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
        Shutdown.Quit(this, _failures == 0 ? 0 : 1);
    }

    void Check(bool ok, string what)
    {
        if (ok) _passes++;
        else _failures++;
        GD.Print($"  {(ok ? "ok   " : "FAIL ")} {what}");
    }

    IEnumerable<int> Scenario()
    {
        bool before = Args.Contains("--before");
        WildlifePatch.NoWoods = before;
        var game = new Game.Game { Interactive = false, World = WorldChoice.Fresh("temperate"), SkipIntro = true };
        AddChild(game);
        var drive = new Driver(game);
        foreach (int x in drive.UntilPlaying()) yield return x;
        game.Zone.Weather.Bridge.Calm = true;
        double max = game.Suit.Sim.Data.Vitals.Health.Max;
        Vector3 Ground(Vector3 at) => game.Zone.GroundAt(at.X, at.Z);

        // The thickest wood within a few hundred metres: the most trunks within 20 m of a dry spot.
        Vector3 start = game.Survivor.WorldFeet, wood = start;
        int most = -1;
        for (int k = 0; k < 40; k++)
        {
            float a = k * 2.39996f, d = 30f + k * 9f;
            Vector3 at = start + new Vector3(MathF.Cos(a) * d, 0f, MathF.Sin(a) * d);
            List<PlanetSurface.Terrain.FloraSolid> near;
            while ((near = game.Zone.Scatter.Near(at.ToNumerics(), 20f)) == null) yield return 0;
            int count = near.Where(s => s.IsTrunk).Select(s => s.Id).Distinct().Count();
            if (count > most && Ground(at).Y > 1f) { most = count; wood = at; }
        }
        Check(most >= 8, $"a wood to watch: {most} trees within 20 m of a spot {wood.DistanceTo(start):F0} m from the start");
        game.Survivor.PlaceOutside((Ground(wood) + Vector3.Up * 0.3f).ToNumerics());
        WildlifePatch patch = game.Zone.Wildlife;
        patch.FinishFollowing();
        for (int i = 0; i < 600 && (patch.Sim == null || !before && patch.Wooded == 0); i++) { game.Suit.Vitals.Health = max; yield return 0; }
        WildSim sim = patch.Sim;
        if (sim == null) { Check(false, "the patch has a sim"); yield break; }
        patch.Frozen = false;
        GD.Print($"  (footprints of trunks and rocks in the creatures' grid: {patch.Wooded}{(before ? ", woods kept out (--before)" : $", read in in {patch.WoodMs:F1} ms")})");

        // The ground's own trunks and rocks round the wood, where they stand in the zone: what is watched against.
        List<PlanetSurface.Terrain.FloraSolid> solids;
        while ((solids = game.Zone.Scatter.Near(wood.ToNumerics(), 90f)) == null) yield return 0;
        var trunks = new List<(long Id, Vector3 At, float R)>();
        game.Zone.Scatter.Footprints(solids, wood, trunks);
        // How far a body this wide standing here would be into the nearest of them, m.
        float Into(Vector3 at, float r) => trunks.Max(t => r + t.R - WildSim.Flat(at - t.At).Length());

        // Hunters from one side, a raiding band from the other, grazers in among the trees, and a giant.
        foreach (Creature other in sim.Creatures) if (patch.WorldOf(other).DistanceTo(wood) < 150f) other.Removed = true;
        var walkers = patch.Life.Fauna.Species.Where(s => s.Rig == RigKind.Legged && !s.Flies && !s.Mechanical).ToList();
        Species Hunter(Species s, float size)
        {
            Species h = s.Resized(size);
            h.Behaviour = Behaviour.Predator;
            h.Temperament = Temperament.Aggressive;
            h.Stats.Hunts = true;
            h.Stats.Damage = 1f;
            return h;
        }
        var watched = new List<Creature>();
        void Add(Species s, Vector3 at, int n, float spread)
        {
            for (int i = 0; i < n; i++)
            {
                watched.Add(sim.Spawn(s, patch.PatchLocal(Ground(Open(at, spread, Locomotion.Footprint(s))))));
            }
        }
        // Set down on open ground, as the game's own spawns are: none starts inside a trunk.
        Vector3 Open(Vector3 at, float spread, float r)
        {
            Vector3 p = at;
            for (int k = 0; k < 200 && (k == 0 && spread > 0f || Into(p, r) > 0f); k++)
                p = at + new Vector3((float)_rng.NextDouble() - 0.5f, 0f, (float)_rng.NextDouble() - 0.5f) * (spread + k * 0.1f);
            return p;
        }
        Vector3 east = new(1f, 0f, 0f);
        if (walkers.Count > 0)
        {
            Add(Hunter(walkers[_rng.Next(walkers.Count)], 1.4f), wood + east * 45f, 3, 6f);
            Add(walkers[_rng.Next(walkers.Count)], wood + new Vector3(0f, 0f, 8f), 3, 10f);
            Add(Hunter(walkers.OrderByDescending(s => s.Body.Size).First(), 6f), wood + new Vector3(0f, 0f, -45f), 1, 0f);
        }
        var band = new List<Creature>();
        for (int i = 0; i < 4; i++)
        {
            Species p = patch.Life.People.Person((ulong)_rng.NextInt64(), PersonRole.Raider, Occupancy.Raider);
            p.Figure.Prop = i % 2 == 0 ? "club" : "machete";
            Creature c = sim.Spawn(p, patch.PatchLocal(Ground(Open(wood - east * 40f + new Vector3(0f, 0f, i * 2f - 3f), 0f, Locomotion.Footprint(p)))));
            band.Add(c);
            watched.Add(c);
            sim.Damage(c, 0.01f, patch.Player); // set on: the band comes for you through the wood
        }
        foreach (Creature c in watched) c.Noticed = patch.Player;

        // Three minutes, every body's footprint against every trunk and rock: a frame a body is into one counts once.
        var log = new List<string>();
        int overlaps = 0, people = 0, animals = 0, frames = 60 * 180;
        float deepest = 0f;
        var walked = new Dictionary<Creature, float>();
        var last = watched.ToDictionary(c => c, c => patch.WorldOf(c));
        int passedTrunks = 0;
        string shot = Args.FirstOrDefault(a => a.StartsWith("--shot="))?.Substring(7);
        bool shotTaken = shot == null;
        for (int f = 0; f < frames; f++)
        {
            game.Suit.Vitals.Health = max;
            yield return 0;
            foreach (Creature c in watched)
            {
                if (c.Removed || c.Dead || c.Species.Flies) continue;
                Vector3 at = patch.WorldOf(c);
                walked[c] = walked.GetValueOrDefault(c) + WildSim.Flat(at - last[c]).Length();
                last[c] = at;
                float into = Into(at, Locomotion.Footprint(c.Species));
                if (into > -1.5f) passedTrunks++;
                if (into <= 0.02f) continue;
                overlaps++;
                deepest = MathF.Max(deepest, into);
                if (c.Species.Figure != null) people++; else animals++;
                if (log.Count < 12) log.Add($"{f / 60f:F1} s: {c.Species.CommonName} ({(c.Species.Figure != null ? "person" : $"{c.Species.Body.Size:F1} m animal")}, {c.State}{(c.Attacking ? $" {c.Phase}" : "")}) {into:F2} m into a trunk or rock");
            }
            if (!shotTaken && f > 60 * 6 && band.Count(c => !c.Dead && Into(patch.WorldOf(c), 0.3f) > -3f) >= 2)
            {
                shotTaken = true;
                Vector3 mid = band.Where(c => !c.Dead).Aggregate(Vector3.Zero, (a, c) => a + patch.WorldOf(c)) / band.Count(c => !c.Dead);
                Vector3 eye = mid + new Vector3(-6f, 3.2f, 7f);
                for (int i = 0; i < 6; i++)
                {
                    game.FixedCamera = new Transform3D(Basis.LookingAt(mid + Vector3.Up * 1f - eye, Vector3.Up), eye);
                    yield return 0;
                }
                foreach (int x in Capture(shot)) yield return x;
                game.FixedCamera = null;
            }
        }
        foreach (string line in log) GD.Print($"  ({line})");
        float peopleWalked = band.Sum(c => walked.GetValueOrDefault(c)), animalsWalked = watched.Except(band).Sum(c => walked.GetValueOrDefault(c));
        string what = $"over 3 minutes in a wood of {trunks.Select(t => t.Id).Distinct().Count()} trees and rocks ({trunks.Count} footprint circles): {overlaps} body-frames into one ({people} by people, {animals} by animals, deepest {deepest:F2} m); the band walked {peopleWalked:F0} m, the animals {animalsWalked:F0} m, {passedTrunks / 60} body-seconds within 1.5 m of one";
        if (before) GD.Print($"  (BEFORE, woods kept out of the grid: {what})");
        else
        {
            Check(overlaps == 0, $"nothing passes through a tree or a rock: {what}");
            Check(peopleWalked > 30f && animalsWalked > 30f && passedTrunks > 60 * 20, $"and they really went through the wood to do it ({what})");
        }
        WildlifePatch.NoWoods = false;
        game.QueueFree();
        yield return 0;
    }

    IEnumerable<int> Capture(string file)
    {
        RenderingServer.FramePostDraw += Grab;
        bool done = false;
        void Grab()
        {
            RenderingServer.FramePostDraw -= Grab;
            Error e = GetViewport().GetTexture().GetImage().SavePng(file);
            GD.Print(e == Error.Ok ? $"saved {file}" : $"could not save {file}: {e}");
            done = true;
        }
        for (int i = 0; i < 30 && !done; i++) yield return 0;
    }
}
