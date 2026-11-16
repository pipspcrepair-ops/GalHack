using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using Slice.Surface;
using Wildlife;

namespace Slice.Tests;

public partial class PlacePerf
{
    /// <summary>
    /// --bands=N: the sleeper out in the open with N bands of the world's people round them, 40 to 90 m off and
    /// bound across, every third one rolled hostile (so bands meet, warn, keep off each other and fight), and two
    /// of the world's predators among them. With --reactions=ab, the same frames with nobody looking round for
    /// others, then with them again.
    /// </summary>
    IEnumerable<int> BandScene()
    {
        foreach (int x in Stand()) yield return x;
        WildlifePatch patch = _game.Zone.Wildlife;
        WildSim sim = patch.Sim;
        WorldLife life = patch.Life;
        var kinds = life.Signature.Kinds.Select(k => k.Species).Concat(life.RuinCast.Select(r => r.Species)).Append(life.Settlers)
            .Where(s => s is { Behaviour: Behaviour.Folk }).ToList();
        if (kinds.Count == 0) kinds.Add(life.People.Person(Wildlife.Rng.Mix(life.World.Seed, 901), PersonRole.Guard)); // (a world nobody lives on: some of its people anyway)
        Vector3 feet = _game.Survivor.WorldFeet;
        Vector3 Ground(Vector3 at) => _game.Zone.GroundAt(at.X, at.Z);
        int n = int.Parse(_args["bands"]), people = 0;
        var parties = new List<Party>();
        for (int i = 0; i < n; i++)
        {
            float a = i * MathF.Tau / n, r = 40f + 50f * (i % 3) / 2f;
            Vector3 dir = new(MathF.Cos(a), 0f, MathF.Sin(a));
            List<Creature> band = sim.SpawnGroup(kinds[i % kinds.Count], patch.PatchLocal(Ground(feet + dir * r)), 4);
            if (band.Count == 0) continue;
            people += band.Count;
            Herd h = band[0].Herd;
            if (i % 3 == 0) h.Party.Temper.Stance = Stance.Hostile;
            h.Home = h.Goal = patch.PatchLocal(Ground(feet - dir * r));
            h.GoalTimer = 120f;
            parties.Add(h.Party);
        }
        Species beast = life.Fauna.Species.FirstOrDefault(s => CrossReactions.Danger(s) && !s.Flies);
        if (beast != null) for (int i = 0; i < 2; i++) sim.Spawn(beast, patch.PatchLocal(Ground(feet + new Vector3(i == 0 ? 70f : -70f, 0f, 20f))));
        GD.Print($"PERF bands: {parties.Count} bands, {people} people of {kinds.Count} kinds, {(beast != null ? $"two {beast.CommonName}" : "no predator")}; {sim.Creatures.Count(c => !c.Removed && !c.Dead)} creatures in the patch");
        foreach (int x in _drive.Wait(300)) yield return x;
        foreach (int x in Measure("bands", 600)) yield return x;
        GD.Print($"PERF bands after: {string.Join(", ", parties.GroupBy(p => p.Mood).Select(g => $"{g.Count()} {g.Key}"))}; {sim.Creatures.Count(c => c.Dead && c.Species.Figure != null)} people down; shots by the wild's people {_game.Arms.People.Shots}");
    }
}
