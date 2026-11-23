using System;
using Planets.Gen;

namespace Planets.Look;

/// <summary>
/// One colour per material, the same on every planet and in every look (owner, 23 Nov: "ore deposits we find keep their colors
/// regardless of palette, so the player can tell the ore at a glance"): the mineral as it looks in rock, weathered and matte, so a
/// prospector learns to read it by the rock first and its colour second. Nothing of a world's palette touches it; where a world's
/// ground would hide it, the ground gives way (<see cref="Around"/>).
/// </summary>
public static class DepositLook
{
    /// <summary>How far apart (Oklab) a deposit and the ground round it always stand.</summary>
    public const float Margin = 0.12f;

    /// <summary>
    /// The ground round a deposit: its own colour, moved off the ore's lightness where the two would blend, so the deposit keeps
    /// its colour and still stands <see cref="Margin"/> apart (the ground gives way, never the ore).
    /// </summary>
    public static Rgb Around(Rgb ground, DepositKind kind)
    {
        Oklab g = Oklab.Of(ground), o = Oklab.Of(Color(kind));
        if (Gap(g, o) >= Margin) return ground;
        // Lighter or darker by as little as will do, on the side the ground already lies first; the screen's gamut may take some
        // of its chroma there, so each step is checked as it will show.
        float side = g.L >= o.L ? 1f : -1f;
        foreach (float s in new[] { side, -side })
            for (float l = o.L + s * 0.02f; l is > 0.03f and < 0.99f; l += s * 0.02f)
            {
                Rgb c = new Oklab(l, g.A, g.B).ToRgb();
                if (Gap(Oklab.Of(c), o) >= Margin) return c;
            }
        return ground;
    }

    static float Gap(Oklab a, Oklab b) => MathF.Sqrt((a.L - b.L) * (a.L - b.L) + (a.A - b.A) * (a.A - b.A) + (a.B - b.B) * (a.B - b.B));

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
        DepositKind.WarpFuel => Rgb.Hex("5f8a8c"),  // cold teal
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
