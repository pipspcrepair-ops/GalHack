using Planets.Gen;

namespace Planets.Look;

/// <summary>
/// One colour per material, the same on every planet, so players learn to spot iron or water at a
/// glance. Bright accents against the biome palettes, like the single saturated detail on a cover.
/// </summary>
public static class DepositLook
{
    public static Rgb Color(DepositKind kind) => kind switch
    {
        DepositKind.Iron => Rgb.Hex("d0582c"),      // rust
        DepositKind.Copper => Rgb.Hex("2cc8a0"),    // verdigris
        DepositKind.Silicon => Rgb.Hex("f4f2ff"),   // quartz
        DepositKind.Carbon => Rgb.Hex("34303c"),    // graphite
        DepositKind.Water => Rgb.Hex("7cd4ff"),     // ice
        DepositKind.Burnstone => Rgb.Hex("f4d81c"), // sulphur yellow
        DepositKind.Magnesium => Rgb.Hex("c8ccdc"), // bright silver
        DepositKind.Cobalt => Rgb.Hex("2a58f0"),
        DepositKind.Neodymium => Rgb.Hex("d87090"), // rose
        DepositKind.Gold => Rgb.Hex("e0a83c"),      // gold
        DepositKind.Gravium => Rgb.Hex("8a3cd0"),   // heavy violet
        DepositKind.Crystal => Rgb.Hex("e070ff"),
        _ => Rgb.Hex("ffa020"),                     // resin
    };

    /// <summary>Self-light, so deposits catch the eye; rarer resources a little more.</summary>
    public static float Glow(DepositKind kind) => kind switch
    {
        DepositKind.Carbon => 0.05f,
        DepositKind.Gold or DepositKind.Gravium or DepositKind.Crystal => 0.6f,
        < DepositKind.Burnstone => 0.25f,
        _ => 0.4f,
    };
}
