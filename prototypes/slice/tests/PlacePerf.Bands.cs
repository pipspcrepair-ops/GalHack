using System;
using System.Collections.Generic;
using System.Linq;
using GalHack.Physics.Engine;
using Godot;
using Slice.Surface;
using Wildlife;

namespace Slice.Tests;

public partial class PlacePerf
{
    /// <summary>
    /// --bands=N: the sleeper out in the open, 150 m from the bunker, with N bands of the world's people round them,
    /// 30 to 60 m off and bound across, every third one rolled hostile (so bands meet, warn, keep off each other and
    /// fight), and two of the world's predators among them. With --reactions=ab, the same frames with nobody looking
    /// round for others, then with them again.
    /// </summary>
    IEnumerable<int> BandScene()
    {
        foreach (int x in Stand()) yield return x;
        var play = new Playthrough(_game);
        Vector3 stand = play.FlatSpot(_game.Survivor.WorldFeet, 150f, span: 10f);
        _game.Survivor.PlaceOutside((stand + Vector3.Up * 0.2f).ToNumerics());
        WildlifePatch patch = _game.Zone.Wildlife;
        // A long move swaps the patch's sim: until the one round you now has stood a few seconds.
        for (int i = 0, same = 0; i < 60 * 20 && same < 180; i++)
        {
            WildSim was = patch.Sim;
            yield return 0;
            same = patch.Sim == was ? same + 1 : 0;
        }
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
            float a = i * MathF.Tau / n, r = 30f + 15f * (i % 3);
            Vector3 dir = new(MathF.Cos(a), 0f, MathF.Sin(a));
            Species s = kinds[i % kinds.Count];
            Vector3 at = patch.PatchLocal(Ground(feet + dir * r));
            List<Creature> band = Staging.PutBand(sim, s, at, 4);
            people += band.Count;
            Herd h = band[0].Herd;
            if (i % 3 == 0) h.Party.Temper.Stance = Stance.Hostile;
            h.Home = h.Goal = patch.PatchLocal(Ground(feet - dir * r));
            h.GoalTimer = 1e9f; // (a new herd's first update counts its time from long ago: anything shorter runs out at once)
            parties.Add(h.Party);
        }
        Species beast = life.Fauna.Species.FirstOrDefault(s => CrossReactions.Danger(s) && !s.Flies);
        if (beast != null) for (int i = 0; i < 2; i++) sim.Spawn(beast, patch.PatchLocal(Ground(feet + new Vector3(i == 0 ? 50f : -50f, 0f, 20f))));
        GD.Print($"PERF bands: {parties.Count} bands, {people} people of {kinds.Count} kinds, {(beast != null ? $"two {beast.CommonName}" : "no predator")}; {sim.Creatures.Count(c => !c.Removed && !c.Dead)} creatures in the patch");
        int shots = _game.Arms.People.Shots;
        foreach (int x in _drive.Wait(300)) yield return x;
        foreach (int x in Measure("bands", 600)) yield return x;
        GD.Print($"PERF bands after: {string.Join(", ", parties.GroupBy(p => p.Mood).Select(g => $"{g.Count()} {g.Key}"))}; {sim.Creatures.Count(c => c.Dead && c.Species.Figure != null)} people down; {_game.Arms.People.Shots - shots} shots by the wild's people");
    }
}
