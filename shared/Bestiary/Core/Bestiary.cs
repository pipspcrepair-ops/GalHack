using System;
using System.Collections.Generic;

namespace Wildlife;

/// <summary>
/// The one way in to everything that moves. Each world rolls one <see cref="WorldLife"/> from its
/// seed and conditions; anything a game wants alive (or merely moving) comes out of that: the wild
/// fauna, a town's animals, its people. A game never builds a generator of its own.
/// </summary>
public static class Bestiary
{
    /// <summary>A world's life: the same world always gives the same life.</summary>
    public static WorldLife Roll(WildlifeData data, WorldConditions world) => new(data, world);
}

/// <summary>Everything alive (and everything that only moves) on one world.</summary>
public sealed class WorldLife
{
    public ulong Seed => World.Seed;
    public float Alienness => World.Alienness;
    public readonly WorldConditions World;
    public readonly WildlifeData Data;
    readonly FaunaGenerator _generator;

    /// <summary>The planet's wild species.</summary>
    public readonly PlanetFauna Fauna;
    /// <summary>The planet's people.</summary>
    public readonly PeopleGenerator People;

    internal WorldLife(WildlifeData data, WorldConditions world)
    {
        Data = data;
        World = world;
        _generator = new FaunaGenerator(data.Fauna, data.Activity) { Allow = f => Fit.Allows(data.Fit, world, f) };
        Fauna = _generator.Generate(world.Seed, world.Alienness, world.Bloom);
        People = new PeopleGenerator(data.People, world);
        FitToWorld();
    }

    /// <summary>
    /// What the world does with what was rolled: bodies fit its climate, what it twists is
    /// twisted, one sex differs from the other; machines walk where they walk; and on a world with
    /// no air, nothing that breathes is left.
    /// </summary>
    void FitToWorld()
    {
        var rng = Rng.For(Seed, "fit");
        List<Species> species = Fauna.Species;
        FitTables t = Data.Fit;
        if (World.Vacuum)
        {
            Species hardy = rng.Chance(t.VacuumLife) ? species.Find(s => s.Rig is RigKind.Legged && !s.Flies) : null;
            species.Clear();
            if (hardy != null)
            {
                Fit.Harden(t, hardy, rng);
                species.Add(hardy);
            }
            species.Add(Robots.Roll(World, 0));
            species.Add(Robots.Roll(World, 1));
            if (rng.Chance(0.5f)) species.Add(Robots.Roll(World, 2));
            return;
        }
        for (int i = 0; i < species.Count; i++)
        {
            Fit.Apply(t, World, species[i], Rng.For(species[i].Seed, "fit"));
            if (World.Mutagen > 0f) species[i] = _generator.Mutate(species[i], World.Mutagen * rng.Range(0.4f, 1f));
            Fit.Throat(species[i], rng);
            Fit.Dimorph(species[i], Rng.For(species[i].Seed, "dimorph"));
            if (species[i].Behaviour == Behaviour.Herd)
            {
                Species young = Fit.Young(species[i], Rng.For(species[i].Seed, "young"));
                species[i].Young = young;
                if (species[i].Mate != null) species[i].Mate.Young = young;
            }
        }
        if (rng.Chance(t.MachineWorld)) species.Add(Robots.Roll(World, rng.Int(0, 2)));
        // Now and then a world has something that breaks blocks.
        if (rng.Chance(t.BreakerWorld) && Breaker((BreakerKind)rng.Int(1, 4), rng) is { } breaker) species.Add(breaker);
        // Where the world has twisted its people far enough, some have gone wild.
        if (World.Mutagen >= 0.3f) species.Add(Feral(rng));
        if (World.Bloom) species.Add(Husk(rng));
    }

    /// <summary>What lives in this world's caves and places, with its husks.</summary>
    public DeepFauna Deep()
    {
        var deep = new DeepFauna(Data, Fauna);
        deep.Husks = theme => Husk(Rng.For(Seed, "husk", (long)theme));
        return deep;
    }

    /// <summary>
    /// A block-breaker of a kind, built by the usual rules from a body that can do the job: a
    /// heavy, armoured head for a ram, a digger for a burrower, a spitter for the corroder, a giant
    /// for the siege beast. Bodies the world bans fall back to a stalker.
    /// </summary>
    public Species Breaker(BreakerKind kind, Rng rng)
    {
        string form = kind switch { BreakerKind.Siege => "colossus", BreakerKind.Burrower => "burrower", BreakerKind.Ram => "strider", _ => "stalker" };
        FormDef def = Data.Fauna.Forms.Find(f => f.Id == form);
        if (def == null || !Fit.Allows(Data.Fit, World, def)) form = "stalker";
        ulong seed = Rng.Mix(Seed, 0xB4EA + (ulong)kind);
        Species s = _generator.RollOne(seed, form, Behaviour.Predator, Alienness);
        s.Breaker = kind;
        s.Index = 400 + (int)kind;
        s.Id = $"{Seed:x}-breaker-{kind}";
        s.Scarcity = Data.Fit.BreakerScarcity;
        s.Stats.Hunts = true;
        s.Stats.Retaliates = true;
        s.Stats.Damage = MathF.Max(s.Stats.Damage, 18f);
        s.Temperament = Temperament.Aggressive;
        if (kind == BreakerKind.Ram) { if (Data.Fauna.FindPart("horns", "ram") != null) s.Parts["horns"] = "ram"; if (Data.Fauna.FindPart("back", "plates") != null) s.Parts["back"] = "plates"; s.Attack = s.Attack.With(AttackStyle.Charge, "Ram"); }
        if (kind == BreakerKind.Corroder) s.Spit = SpitKind.Bile;
        if (kind == BreakerKind.Burrower) s.Burrows = true;
        Fit.Apply(Data.Fit, World, s, Rng.For(seed, "fit"));
        return s;
    }

    /// <summary>People gone wild: the same people, feral, with the same world in them. Both sexes run together.</summary>
    Species Feral(Rng rng)
    {
        Species a = Wild(rng, Rng.Mix(Seed, 0xFE4A), PersonRole.Worker, World.Mutagen * rng.Range(0.5f, 1f), husk: false);
        Species b = Wild(rng, Rng.Mix(Seed, 0xFE4B), PersonRole.Scavenger, World.Mutagen * rng.Range(0.5f, 1f), husk: false);
        a.Mate = b;
        b.Mate = a;
        return a;
    }

    /// <summary>What the Hollow leaves walking. Nothing says so.</summary>
    Species Husk(Rng rng) => Wild(rng, Rng.Mix(Seed, 0x4057), PersonRole.Worker, rng.Range(0.1f, 0.35f), husk: true);

    Species Wild(Rng rng, ulong seed, PersonRole role, float mutation, bool husk)
    {
        Species s = People.Person(seed, role, Occupancy.Feral, mutation, husk);
        s.Scarcity = 0.5f;
        s.Senses = new SenseProfile { DarkVision = People.Folk.DarkVision, Hearing = 1.2f, Scent = 0.3f };
        Fit.Throat(s, rng);
        return s;
    }

    /// <summary>Every kind that moves on this world.</summary>
    public IEnumerable<Species> Moving => Fauna.Species;

    /// <summary>A settlement's own animal; <paramref name="n"/> tells a town's several kinds apart.</summary>
    public Species Domestic(DomesticKind kind, int n = 0) => _generator.RollDomestic(Seed + (ulong)n, kind, Alienness);
}
