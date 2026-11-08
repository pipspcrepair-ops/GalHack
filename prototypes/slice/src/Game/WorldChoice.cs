using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Planets.Gen;
using PoiGenerator.Gen;

namespace Slice.Game;

/// <summary>
/// Which world a game is played on: its seed, its planet type and size, and what a new game forced
/// onto its roll (depth, theme, fall, mid-collapse, the two eras: <see cref="WorldForce"/>; null
/// when nothing is). A new game rolls a fresh seed (and, if asked, a random type); a saved game keeps
/// the choice it was started with (this record is what a save stores); tests roll a fresh one every
/// run, as a new game does, and the command line can pin any of it (<see cref="FromArgs"/>).
/// </summary>
public sealed record WorldChoice(ulong Seed, Archetype Archetype, int Variant, int WidthKm = 16, WorldForce Force = null)
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

    /// <summary>
    /// Every world type of the world-layer data whose terrain the planet generator makes (its
    /// <c>arch</c> names an archetype's variant): what the new-game screen and <c>--type</c> offer.
    /// </summary>
    public static IReadOnlyList<(string Id, Archetype Archetype, int Variant)> LayerTypes => _layerTypes ??= ReadLayerTypes();
    static List<(string Id, Archetype Archetype, int Variant)> _layerTypes;

    static List<(string Id, Archetype Archetype, int Variant)> ReadLayerTypes()
    {
        var made = new Dictionary<string, (Archetype, int)>();
        foreach (Archetype a in Enum.GetValues<Archetype>())
            for (int v = 0; v < PlanetSpec.VariantCount; v++)
                made[$"{a.ToString().ToLowerInvariant()}.{Archetypes.Get(a, v).VariantName.Replace(' ', '_')}"] = (a, v);
        var list = new List<(string, Archetype, int)>();
        foreach (WorldLayer l in Library.Layers)
            if (l.Kind == LayerKind.Type && l.Arch != null && made.TryGetValue(l.Arch, out var av)) list.Add((l.Id, av.Item1, av.Item2));
        return list;
    }

    /// <summary>The world-layer data (the POI catalogue's library, loaded once for the game).</summary>
    public static PoiLibrary Library => Surface.SurfaceZone.PoiCatalog.Library;

    /// <summary>The world a test run plays: rolled fresh once per run, like a new game, so no two runs prove the same world. The run prints it; <c>--seed N</c> replays it.</summary>
    public static readonly WorldChoice Tested = Fresh();

    /// <summary>A fresh seed: one of about 9 quintillion, different for every new game (stays positive as a long, so saves can hold it).</summary>
    public static ulong FreshSeed() => (ulong)Random.Shared.NextInt64(1, long.MaxValue);

    /// <summary>A new game on a planet type (by its key, a world-layer type or an archetype; "random", null or anything unknown rolls one of <see cref="Types"/>).</summary>
    public static WorldChoice Fresh(string type = "temperate", ulong? seed = null)
    {
        ulong s = seed ?? FreshSeed();
        if (Lookup(type) is { } t) return new WorldChoice(s, t.Archetype, t.Variant);
        if (LayerType(type) is { } l) return new WorldChoice(s, l.Archetype, l.Variant);
        var r = Types[(int)(s % (ulong)Types.Count)];
        return new WorldChoice(s, r.Archetype, r.Variant);
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

    /// <summary>A world-layer type by id (meadow, dune, hot_spring_ice...), or null.</summary>
    public static (string Id, Archetype Archetype, int Variant)? LayerType(string id)
    {
        if (string.IsNullOrWhiteSpace(id) || id.Equals("random", StringComparison.OrdinalIgnoreCase)) return null;
        foreach (var t in LayerTypes)
            if (t.Id.Equals(id.Replace('-', '_'), StringComparison.OrdinalIgnoreCase)) return t;
        return null;
    }

    public string Key => Types.FirstOrDefault(t => t.Archetype == Archetype && t.Variant == Variant).Key ?? Archetype.ToString().ToLowerInvariant();
    public string Name => Types.FirstOrDefault(t => t.Archetype == Archetype && t.Variant == Variant).Name ?? $"{Archetype} {Archetypes.Get(Archetype, Variant).VariantName}";

    /// <summary>The world-layer type this planet is, as its roll takes it.</summary>
    public string TypeId => LayerTypes.FirstOrDefault(t => t.Archetype == Archetype && t.Variant == Variant).Id;

    public PlanetSpec Spec() => PlanetSpec.Roll(Seed, Archetype, Variant, WidthKm);

    public override string ToString() => $"seed {Seed}, {Name}{(WidthKm != 16 ? $", {WidthKm}x{WidthKm / 2} km" : "")}{(Force is { Empty: false } f ? $", {f}" : "")}";

    /// <summary>
    /// What the command line pins, rolling everything it leaves out: <c>--seed N</c>, <c>--planet</c>
    /// or <c>--type</c> (a key such as temperate, or a world-layer type such as meadow), <c>--size 32x16</c>,
    /// <c>--depth 0.7</c> (or random), <c>--theme id</c>, <c>--fall id</c>, <c>--mid-collapse</c> or
    /// <c>--mid-collapse=no</c>, <c>--era-old high</c> and <c>--era-now low</c>. Each takes
    /// <c>--name value</c> or <c>--name=value</c>. Null when it pins nothing, so the start screen
    /// (or a fresh roll) decides. Unknown values are printed and left to the roll.
    /// </summary>
    public static WorldChoice FromArgs(IReadOnlyList<string> args, WorldChoice fallback = null)
    {
        string seedText = Value(args, "--seed"), planet = Value(args, "--type") ?? Value(args, "--planet");
        string size = Value(args, "--size");
        WorldForce force = ForceFromArgs(args);
        if (seedText == null && planet == null && size == null && force == null) return fallback;
        ulong? seed = ulong.TryParse(seedText, out ulong s) ? s : fallback?.Seed;
        WorldChoice picked = planet == null && fallback != null ? fallback with { Seed = seed ?? fallback.Seed } : Fresh(planet ?? "temperate", seed);
        if (planet != null && Lookup(planet) == null && LayerType(planet) == null && !planet.Equals("random", StringComparison.OrdinalIgnoreCase))
            Godot.GD.PrintErr($"--type {planet}: no such world type (one of {string.Join(", ", Types.Select(t => t.Key).Concat(LayerTypes.Select(t => t.Id)))}); rolled one");
        if (int.TryParse(Value(args, "--variant"), out int variant)) picked = picked with { Variant = variant };
        if (size != null)
        {
            if (Size(size) is int width) picked = picked with { WidthKm = width };
            else Godot.GD.PrintErr($"--size {size}: one of {string.Join(", ", PlanetSpec.Widths.Select(w => $"{w}x{w / 2}"))}");
        }
        return force == null ? picked : picked with { Force = force };
    }

    /// <summary>The forced options on the command line, checked against the world-layer data; null when there are none.</summary>
    public static WorldForce ForceFromArgs(IReadOnlyList<string> args)
    {
        var force = new WorldForce();
        if (Value(args, "--depth") is string d)
        {
            if (d.Equals("random", StringComparison.OrdinalIgnoreCase)) force = force with { Depth = Random.Shared.NextSingle() };
            else if (float.TryParse(d, NumberStyles.Float, CultureInfo.InvariantCulture, out float depth) && depth is >= 0f and <= 1f) force = force with { Depth = depth };
            else Godot.GD.PrintErr($"--depth {d}: a number from 0 (the rim) to 1 (the core), or random");
        }
        force = force with { Theme = Known(args, "--theme", LayerKind.Theme), Fall = Known(args, "--fall", LayerKind.Fall) };
        int mid = IndexOf(args, "--mid-collapse");
        if (mid >= 0)
        {
            string said = args[mid].Contains('=') ? args[mid][(args[mid].IndexOf('=') + 1)..] : "yes";
            force = force with { MidCollapse = said is not ("no" or "false" or "0") };
        }
        if (IndexOf(args, "--no-mid-collapse") >= 0) force = force with { MidCollapse = false };
        force = force with { Before = Era(args, "--era-old"), Now = Era(args, "--era-now") };
        return force.Empty ? null : force;
    }

    static string Known(IReadOnlyList<string> args, string name, LayerKind kind)
    {
        if (Value(args, name) is not string id) return null;
        if (Library.Layers.Any(l => l.Kind == kind && l.Id == id)) return id;
        Godot.GD.PrintErr($"{name} {id}: no such {kind.ToString().ToLowerInvariant()} in the world-layer data; rolled one");
        return null;
    }

    static TechLevel? Era(IReadOnlyList<string> args, string name)
    {
        if (Value(args, name) is not string word) return null;
        if (WorldForce.Era(word) is TechLevel t) return t;
        Godot.GD.PrintErr($"{name} {word}: low, mid or high");
        return null;
    }

    /// <summary>A size class from "32x16" or "32": the width in km, or null.</summary>
    public static int? Size(string text)
    {
        string w = text.Split('x', 'X')[0].Trim();
        return int.TryParse(w, out int width) && PlanetSpec.Widths.Contains(width) ? width : null;
    }

    static int IndexOf(IReadOnlyList<string> args, string name)
    {
        for (int i = 0; i < args.Count; i++)
            if (args[i] == name || args[i].StartsWith(name + "=", StringComparison.Ordinal)) return i;
        return -1;
    }

    static string Value(IReadOnlyList<string> args, string name)
    {
        for (int i = 0; i < args.Count; i++)
        {
            if (args[i].StartsWith(name + "=", StringComparison.Ordinal)) return args[i][(name.Length + 1)..];
            if (args[i] == name && i + 1 < args.Count && !args[i + 1].StartsWith("--", StringComparison.Ordinal)) return args[i + 1];
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
