using System;

namespace GalHack.Stars;

/// <summary>
/// Generated names, worked out from an id when asked for. A star's id is spread over 16 bits by a
/// multiply that never repeats, then spelled out in three syllables, so no two stars share a name
/// and none of it is stored. Names that come out as a real star or anything of Earth are respelled.
/// </summary>
public static class GalaxyNames
{
    static readonly string[] Syllables =
    {
        "ka", "ve", "tor", "ish", "um", "dra", "sel", "nor", "vak", "ith", "ora", "mek", "zan", "tul", "ren", "kes", "yon", "ath",
        "bry", "qua", "lum", "dor", "hes", "ova", "gri", "sen", "tav", "ule", "mir", "xan", "por", "ik", "eth", "vor", "nal", "cor",
        "sku", "ryn", "ald", "jen", "fen",
    };

    /// <summary>Real stars, and Earth's own names: nothing in the galaxy is called these.</summary>
    public static readonly string[] Forbidden =
    {
        "sol", "sun", "earth", "terra", "luna", "moon", "mars", "venus", "mercury", "jupiter", "saturn", "uranus", "neptune", "pluto",
        "sirius", "vega", "rigel", "altair", "deneb", "betelgeuse", "antares", "polaris", "arcturus", "capella", "procyon", "aldebaran",
        "spica", "canopus", "fomalhaut", "regulus", "pollux", "castor", "bellatrix", "mira", "achernar", "centauri", "tau ceti", "barnard",
        "orion", "andromeda", "cygnus", "lyra", "carina", "crab", "eagle", "horsehead", "lagoon", "rosette", "trifid", "pleiades", "magellan",
    };

    public static bool IsForbidden(string name)
    {
        string low = name.ToLowerInvariant();
        foreach (string f in Forbidden)
            if (low == f || low.StartsWith(f + " ", StringComparison.Ordinal) || low.Contains(" " + f, StringComparison.Ordinal)) return true;
        return false;
    }

    public static string Star(ulong seed, int id)
    {
        uint multiplier = (uint)(Mix.Of(seed, 0x4E414D45) | 1), shift = (uint)(Mix.Of(seed, 0x4E414D46) & 0xFFFF);
        uint code = ((uint)id * multiplier + shift) & 0xFFFF; // 65,536 names, each once
        int n = Syllables.Length;
        string a = Syllables[code % n], b = Syllables[code / n % n], c = Syllables[code / n / n % n];
        string name = char.ToUpperInvariant(a[0]) + a[1..] + b + c;
        return IsForbidden(name) ? name + "a" : name;
    }

    static readonly string[] Adjectives = { "Ashen", "Drowned", "Cinder", "Silent", "Pale", "Ember", "Sunken", "Thorn", "Rust", "Glass", "Bitter", "Veiled", "Far", "Ninth", "Wandering", "Broken", "Salt", "Lantern", "Iron", "Quiet" };
    static readonly string[] Gas = { "Veil", "Drift", "Reach", "Mantle", "Tide", "Hearth", "Banner", "Choir" };
    static readonly string[] Dust = { "Shroud", "Pall", "Blind", "Hollow", "Shadow", "Cowl", "Murk", "Fold" };
    static readonly string[] Blue = { "Mirror", "Lace", "Gleam", "Shoal", "Sheen", "Halo", "Pool", "Ripple" };
    static readonly string[] Shell = { "Scar", "Wake", "Ruin", "Husk", "Ring", "Breach", "Cinders", "Wound" };

    public static string Nebula(ulong seed, int id, NebulaKind kind)
    {
        ulong h = Mix.Of(seed, 0x4E45424C, id);
        string[] nouns = kind switch { NebulaKind.Dark => Dust, NebulaKind.Reflection => Blue, NebulaKind.Remnant => Shell, _ => Gas };
        return $"{Adjectives[(int)(h % (ulong)Adjectives.Length)]} {nouns[(int)((h >> 16) % (ulong)nouns.Length)]}";
    }

    public static string Kind(StarKind k) => k switch
    {
        StarKind.BrownDwarf => "Brown dwarf", StarKind.RedDwarf => "Red dwarf", StarKind.OrangeDwarf => "Orange dwarf", StarKind.YellowDwarf => "Yellow dwarf",
        StarKind.WhiteStar => "White star", StarKind.BlueGiant => "Blue giant", StarKind.Protostar => "Young star", StarKind.RedGiant => "Red giant",
        StarKind.WhiteDwarf => "White dwarf", StarKind.Pulsar => "Pulsar", StarKind.Magnetar => "Magnetar", _ => "Black hole",
    };

    public static string Of(Region r) => r switch
    {
        Region.Core => "Core", Region.InnerArm => "Inner arm", Region.OuterArm => "Outer arm", Region.Void => "Mid-galaxy void", Region.Periphery => "Periphery", _ => "Satellite galaxy",
    };

    public static string Satellite(ulong seed, int id) => $"{Star(seed, 60000 + id)} Cloud";
}
