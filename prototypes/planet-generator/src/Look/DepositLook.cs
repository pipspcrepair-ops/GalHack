using Planets.Gen;

namespace Planets.Look;

/// <summary>
/// One colour per material, the same on every planet: the mineral as it looks in rock, weathered and
/// matte, so a prospector learns to read it by the rock first and its colour second.
/// </summary>
public static class DepositLook
{
    public static Rgb Color(DepositKind kind) => kind switch
    {
        DepositKind.Iron => Rgb.Hex("7c4f38"),      // rust
        DepositKind.Copper => Rgb.Hex("5f7a6c"),    // verdigris
        DepositKind.Silicon => Rgb.Hex("b4b0a6"),   // quartz
        DepositKind.Carbon => Rgb.Hex("35323a"),    // graphite
        DepositKind.Water => Rgb.Hex("a2b6bc"),     // ice
        DepositKind.Burnstone => Rgb.Hex("a3945a"), // dull sulphur
        DepositKind.Magnesium => Rgb.Hex("9a9ca4"), // dull silver
        DepositKind.Cobalt => Rgb.Hex("50586c"),
        DepositKind.Neodymium => Rgb.Hex("8a6a74"), // dusty rose
        DepositKind.Gold => Rgb.Hex("9a8048"),
        DepositKind.Gravium => Rgb.Hex("625678"),   // heavy violet
        DepositKind.Crystal => Rgb.Hex("7c6a86"),
        _ => Rgb.Hex("957548"),                     // resin
    };

    /// <summary>Self-light: barely any, and only on what would catch the sun or carries a charge.</summary>
    public static float Glow(DepositKind kind) => kind switch
    {
        DepositKind.Carbon => 0f,
        DepositKind.Gravium or DepositKind.Crystal => 0.2f,
        < DepositKind.Burnstone => 0.04f,
        _ => 0.08f,
    };
}
