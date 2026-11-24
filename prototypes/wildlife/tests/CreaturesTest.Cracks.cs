using System;
using System.Linq;
using Godot;

namespace Wildlife;

public partial class CreaturesTest
{
    static readonly ulong CrackSeed = OS.GetCmdlineUserArgs().FirstOrDefault(a => a.StartsWith("--seed=")) is { } s ? ulong.Parse(s[7..]) : (ulong)DateTime.UtcNow.Ticks;

    static bool Harmless(Creature c) => c.Species.Passive || c.Species.Role == DeepRole.Subsister;

    /// <summary>
    /// Wall cracks are off (owner, 18 Nov): no place makes one and none is drawn; as someone walks
    /// through, no creature goes into a wall crack and no insect into any hole, and its harmless
    /// crawlers stay on its surfaces, never inside them.
    /// </summary>
    void NoWallCracks()
    {
        GD.Print($"  (wall cracks: --seed={CrackSeed})");
        int cracks = 0, places = 0, into = 0, rescues = 0;
        float inside = 0f;
        for (ulong i = 0; i < 4; i++)
        {
            ulong seed = CrackSeed + i * 7919;
            BlockPlace place = i % 2 == 0 ? BlockPlace.Hold(seed) : BlockPlace.Outpost(seed);
            PoiPopulation pop = PoiFauna.Populate(place.Plan, new DeepFauna(_data, seed, 0.5f), _data.Deep);
            places++;
            cracks += pop.Cracks.Count(k => k.Kind == CrackKind.Crack);
            WildSim sim = PlaceSim(place);
            PoiFauna.Spawn(sim, pop, place.Space);
            sim.Slipped += (c, k, entering) => { if (entering && (k.Kind == CrackKind.Crack || Harmless(c))) into++; };
            // Someone walks every room in turn: what lives there scatters.
            var walker = new DummyTarget { Name = "Walker", Health = new Health(1e7f), Position = place.Plan.Rooms[0].FloorCenter };
            sim.Targets.Add(walker);
            var stops = place.Plan.Rooms.Select(r => r.FloorCenter + Vector3.Up * 0.2f).ToList();
            var watch = new Watch { Inside = BlockDepth(place) };
            int leg = 0, step = 0;
            for (float t = 0f; t < 180f; t += Dt, step++)
            {
                Vector3 to = stops[leg] - walker.Position;
                if (to.Length() < 0.3f) leg = (leg + 1) % stops.Count;
                else walker.Position += to.Normalized() * MathF.Min(to.Length(), 2f * Dt);
                sim.Step(Dt);
                if (step % 6 == 0) foreach (Creature c in sim.Creatures) if (Harmless(c)) watch.Look(c, place.Space);
            }
            rescues += sim.Rescues;
            inside = MathF.Max(inside, watch.WorstInside);
        }
        var empty = new WildSim(_data, new Terrain(5, 40f), new NavGrid(40f, 3f));
        empty.Cracks.Add(new Crack { Id = 0, Kind = CrackKind.Crack, Position = new Vector3(28f, 9.3f, 10f), Normal = Vector3.Right, Network = 1 });
        var view = new LairView();
        view.Build(empty);
        int drawn = view.GetChildCount();
        view.Free();
        Check(cracks == 0 && drawn == 0, $"no place makes a wall crack ({cracks} in {places} places) and none is drawn ({drawn} nodes)");
        Check(into == 0, $"nothing goes into a wall crack and no insect into any hole ({into} went in)");
        Check(inside <= 0.02f && rescues == 0, $"harmless crawlers stay on the place's surfaces, never inside them (deepest {MathF.Max(0f, inside):F2} m, {rescues} rescues)");
    }
}
