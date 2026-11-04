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
        DepositKind.Titanium => Rgb.Hex("c8ccdc"),
        DepositKind.Cobalt => Rgb.Hex("2a58f0"),
        DepositKind.Sulfur => Rgb.Hex("f4d81c"),
        DepositKind.Thorium => Rgb.Hex("6aff5a"),
        DepositKind.Crystal => Rgb.Hex("e070ff"),
        _ => Rgb.Hex("ffa020"),                     // resin
    };

    /// <summary>Self-light, so deposits catch the eye; signature resources a little more.</summary>
    public static float Glow(DepositKind kind) => kind switch
    {
        DepositKind.Carbon => 0.05f,
        DepositKind.Thorium or DepositKind.Crystal => 0.6f,
        < DepositKind.Titanium => 0.25f,
        _ => 0.4f,
    };
}
