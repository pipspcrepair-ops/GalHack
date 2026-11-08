#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace PoiGenerator.Gen;

/// <summary>
/// What a new game (or a test) forces onto its world before the roll: a depth in the galaxy, the
/// theme, the fall, mid-collapse or not, and the two tech eras. Anything left null is rolled as it
/// would be. The planet's type, size and seed are its spec's and the mode is the game's, so they
/// aren't here. Plain data: the same values the start screen and the command line offer.
/// </summary>
public sealed record WorldForce
{
    /// <summary>How deep in the galaxy to start, 0 on the rim to 1 at the core; null: the rim, at an arm's outer tip.</summary>
    public float? Depth { get; init; }
    public string? Theme { get; init; }
    public string? Fall { get; init; }
    /// <summary>True: the world is falling right now; false: it isn't; null: rolled.</summary>
    public bool? MidCollapse { get; init; }
    /// <summary>The old world's tech era and the living's.</summary>
    public TechLevel? Before { get; init; }
    public TechLevel? Now { get; init; }

    /// <summary>
    /// The force on the world being built now. The slice sets it with <c>PoiSiting.StartWorld</c>,
    /// before the world is built, and the planet's build reads it.
    /// </summary>
    public static WorldForce Current = new();

    public bool Empty => this == new WorldForce();

    /// <summary>The galaxy zone of a depth (data/world-layers.txt): rim, arm, inner or core, at the galaxy map's bands.</summary>
    public static string ZoneAt(float depth) => depth < 0.25f ? "rim" : depth < 0.5f ? "arm" : depth < 0.8f ? "inner" : "core";

    /// <summary>A profile with the eras forced.</summary>
    public WorldProfile Apply(WorldProfile p) => p with { Before = Before ?? p.Before, Now = Now ?? p.Now };

    /// <summary>What the roll is given: the theme, fall and phase asked for (the zone comes from the world's real depth, in the roll).</summary>
    public void Into(WorldRoll.Given given)
    {
        if (Theme != null) given.Set(LayerKind.Theme, Theme);
        if (Fall != null) given.Set(LayerKind.Fall, Fall);
        if (MidCollapse == true) given.Set(LayerKind.Phase, "falling");
        else if (MidCollapse == false) given.Without(LayerKind.Phase, "falling");
    }

    /// <summary>The values a picker offers for a layer: every value of it in the library, by id.</summary>
    public static List<string> Values(PoiLibrary lib, LayerKind kind) => lib.Layers.Where(l => l.Kind == kind).Select(l => l.Id).ToList();

    /// <summary>A tech era from a word (low, mid, high), or null.</summary>
    public static TechLevel? Era(string? word) => word?.ToLowerInvariant() switch { "low" => TechLevel.Low, "mid" => TechLevel.Mid, "high" => TechLevel.High, _ => null };

    public override string ToString()
    {
        var parts = new List<string>();
        if (Depth is float d) parts.Add($"depth {d.ToString("0.00", CultureInfo.InvariantCulture)}");
        if (Theme != null) parts.Add($"theme {Theme}");
        if (Fall != null) parts.Add($"fall {Fall}");
        if (MidCollapse is bool m) parts.Add(m ? "mid-collapse" : "not mid-collapse");
        if (Before is TechLevel b) parts.Add($"era-old {WorldProfile.Tag(b)}");
        if (Now is TechLevel n) parts.Add($"era-now {WorldProfile.Tag(n)}");
        return parts.Count == 0 ? "nothing forced" : string.Join(", ", parts);
    }
}
