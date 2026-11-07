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
        _generator = new FaunaGenerator(data.Fauna, data.Activity);
        Fauna = _generator.Generate(world.Seed, world.Alienness, world.Bloom);
        People = new PeopleGenerator(data.People, world);
    }

    /// <summary>Every kind that moves on this world.</summary>
    public IEnumerable<Species> Moving => Fauna.Species;

    /// <summary>A settlement's own animal; <paramref name="n"/> tells a town's several kinds apart.</summary>
    public Species Domestic(DomesticKind kind, int n = 0) => _generator.RollDomestic(Seed + (ulong)n, kind, Alienness);
}
