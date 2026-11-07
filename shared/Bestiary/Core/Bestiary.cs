using System;
using System.Collections.Generic;

namespace Wildlife;

/// <summary>
/// The one way in to everything that moves. Each world rolls one <see cref="WorldLife"/> from its
/// seed; anything a game wants alive comes out of that: the wild fauna, a town's animals. A game
/// never builds a generator of its own.
/// </summary>
public static class Bestiary
{
    /// <summary>A world's life, rolled from its seed: the same seed and alienness always give the same life.</summary>
    public static WorldLife Roll(WildlifeData data, ulong seed, float alienness, bool bloom = false) => new(data, seed, alienness, bloom);
}

/// <summary>Everything alive (and everything that only moves) on one world.</summary>
public sealed class WorldLife
{
    public readonly ulong Seed;
    public readonly float Alienness;
    public readonly WildlifeData Data;
    readonly FaunaGenerator _generator;

    /// <summary>The planet's wild species.</summary>
    public readonly PlanetFauna Fauna;

    internal WorldLife(WildlifeData data, ulong seed, float alienness, bool bloom)
    {
        Data = data;
        Seed = seed;
        Alienness = alienness;
        _generator = new FaunaGenerator(data.Fauna, data.Activity);
        Fauna = _generator.Generate(seed, alienness, bloom);
    }

    /// <summary>A settlement's own animal; <paramref name="n"/> tells a town's several kinds apart.</summary>
    public Species Domestic(DomesticKind kind, int n = 0) => _generator.RollDomestic(Seed + (ulong)n, kind, Alienness);
}
