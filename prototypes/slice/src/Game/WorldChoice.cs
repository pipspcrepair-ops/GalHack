using System;
using System.Collections.Generic;
using System.Linq;
using Planets.Gen;

namespace Slice.Game;

/// <summary>
/// Which world a game is played on: its seed and its planet type. A new game rolls a fresh seed
/// (and, if asked, a random type); a saved game keeps the choice it was started with (this record
/// is what a save stores); tests roll a fresh one every run, as a new game does, and anything can pin both (<c>--seed N</c>, <c>--planet type</c>).
/// </summary>
public sealed record WorldChoice(ulong Seed, Archetype Archetype, int Variant)
{
    /// <summary>The planet types the slice starts on, as the start screen offers them (temperate first: the easy one).</summary>
    public static readonly IReadOnlyList<(string Key, string Name, Archetype Archetype, int Variant)> Types = new[]
    {
        ("temperate", "Temperate (easy)", Archetype.Temperate, 0),
        ("ice", "Ice", Archetype.Ice, 0),
        ("swamp", "Alien swamp", Archetype.Alien, 1),
        ("arid", "Arid", Archetype.Arid, 0),
        ("lava", "Lava", Archetype.Lava, 0),
    };

    /// <summary>The world a test run plays: rolled fresh once per run, like a new game, so no two runs prove the same world. The run prints it; <c>--seed N</c> replays it.</summary>
    public static readonly WorldChoice Tested = Fresh();

    /// <summary>A fresh seed: one of about 9 quintillion, different for every new game (stays positive as a long, so saves can hold it).</summary>
    public static ulong FreshSeed() => (ulong)Random.Shared.NextInt64(1, long.MaxValue);

    /// <summary>A new game on a planet type (by its key; "random" or null rolls one of the types).</summary>
    public static WorldChoice Fresh(string type = "temperate", ulong? seed = null)
    {
        ulong s = seed ?? FreshSeed();
        var t = Lookup(type) ?? Types[(int)(s % (ulong)Types.Count)];
        return new WorldChoice(s, t.Archetype, t.Variant);
    }

    /// <summary>A planet type by key or name, or null for "random" or anything unknown.</summary>
    public static (string Key, string Name, Archetype Archetype, int Variant)? Lookup(string type)
    {
        if (string.IsNullOrWhiteSpace(type) || type.Equals("random", StringComparison.OrdinalIgnoreCase)) return null;
        foreach (var t in Types)
            if (t.Key.Equals(type, StringComparison.OrdinalIgnoreCase) || t.Archetype.ToString().Equals(type, StringComparison.OrdinalIgnoreCase))
                return t;
        return null;
    }

    public string Key => Types.FirstOrDefault(t => t.Archetype == Archetype && t.Variant == Variant).Key ?? Archetype.ToString().ToLowerInvariant();
    public string Name => Types.FirstOrDefault(t => t.Archetype == Archetype && t.Variant == Variant).Name ?? $"{Archetype} {Variant}";

    public PlanetSpec Spec() => PlanetSpec.Roll(Seed, Archetype, Variant);

    public override string ToString() => $"seed {Seed}, {Name}";

    /// <summary>
    /// What the command line pins: <c>--seed N</c> (or <c>--seed=N</c>) and <c>--planet type</c>.
    /// Null when it pins nothing, so the start screen (or a fresh roll) decides.
    /// </summary>
    public static WorldChoice FromArgs(IReadOnlyList<string> args, WorldChoice fallback = null)
    {
        string seedText = Value(args, "--seed"), planet = Value(args, "--planet");
        if (seedText == null && planet == null) return fallback;
        ulong? seed = ulong.TryParse(seedText, out ulong s) ? s : fallback?.Seed;
        if (planet == null && fallback != null) return fallback with { Seed = seed ?? fallback.Seed };
        return Fresh(planet ?? "temperate", seed);
    }

    static string Value(IReadOnlyList<string> args, string name)
    {
        for (int i = 0; i < args.Count; i++)
        {
            if (args[i].StartsWith(name + "=", StringComparison.Ordinal)) return args[i][(name.Length + 1)..];
            if (args[i] == name && i + 1 < args.Count) return args[i + 1];
        }
        return null;
    }

    /// <summary>A typed seed: digits as they are, anything else hashed to a number (so "red moon" replays too).</summary>
    public static ulong ParseSeed(string text)
    {
        text = text?.Trim() ?? "";
        if (ulong.TryParse(text, out ulong n)) return n;
        ulong h = 1469598103934665603UL;
        foreach (char c in text) h = (h ^ c) * 1099511628211UL;
        return h & long.MaxValue;
    }
}
