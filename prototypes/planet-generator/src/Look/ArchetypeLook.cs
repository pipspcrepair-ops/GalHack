using Planets.Gen;

namespace Planets.Look;

/// <summary>
/// Everything about how an archetype variant looks that isn't a biome: its liquid, its sky from the
/// ground, its atmosphere and clouds from orbit, and the space behind it. Visual-only modifiers
/// (ringed sky, perpetual storm, glowing nights) are applied here and nowhere else.
/// </summary>
public sealed record ArchetypeLook
{
    // Liquid
    public Rgb DeepSea { get; init; }
    public Rgb ShallowSea { get; init; }
    public Rgb FrozenSea { get; init; } = Rgb.Hex("b8d8ee");
    public float LiquidGlow { get; init; }        // lava seas light themselves

    // Sky from the surface
    public Rgb SkyTop { get; init; }
    public Rgb SkyHorizon { get; init; }
    public Rgb SunColor { get; init; } = Rgb.Hex("fff4dc");
    public float SunEnergy { get; init; } = 1.3f;
    public float SunElevation { get; init; } = 38f;  // degrees above the horizon
    public float Ambient { get; init; } = 0.55f;     // sky light; low on worlds that should read dark and glowing
    public float FogDensity { get; init; } = 0.00035f;
    public Rgb Moon { get; init; } = Rgb.Hex("e8dcc8");
    public float MoonSize { get; init; } = 0.05f;    // angular radius, radians
    public float Stars { get; init; }                 // 0-1: how visible stars are in daylight
    public bool Ring { get; init; }
    public Rgb RingColor { get; init; } = Rgb.Hex("f0d8b0");

    // Orbit
    public Rgb Atmosphere { get; init; }
    public Rgb CloudColor { get; init; } = Rgb.Hex("ffffff");
    public float CloudCover { get; init; } = 0.45f;   // 0 clear .. 1 overcast
    public Rgb Nebula { get; init; } = Rgb.Hex("2a1a5a");
    public bool GlowingNights { get; init; }

    public static ArchetypeLook For(PlanetSpec spec)
    {
        ArchetypeLook look = Base(spec.Archetype, spec.Variant);
        if (spec.Has(Modifier.RingedSky)) look = look with { Ring = true };
        if (spec.Has(Modifier.PerpetualStorm))
        {
            look = look with
            {
                SkyTop = Rgb.Lerp(look.SkyTop, Rgb.Hex("2a2a38"), 0.55f),
                SkyHorizon = Rgb.Lerp(look.SkyHorizon, Rgb.Hex("6a6a78"), 0.5f),
                SunEnergy = look.SunEnergy * 0.6f,
                FogDensity = look.FogDensity * 2.2f,
                CloudCover = 0.68f, // heavy, but the surface must still show through

                CloudColor = Rgb.Lerp(look.CloudColor, Rgb.Hex("8a8a98"), 0.4f),
            };
        }
        if (spec.Has(Modifier.GlowingNights))
        {
            look = look with
            {
                GlowingNights = true,
                SkyTop = Rgb.Lerp(look.SkyTop, Rgb.Hex("1a0a4a"), 0.35f),
                Stars = 1f,
            };
        }
        return look;
    }

    public Rgb Sea(Biome biome, float depth) => biome == Biome.FrozenSea
        ? FrozenSea
        : Rgb.Lerp(ShallowSea, DeepSea, Mathx.Smooth(2f, 90f, depth));

    static ArchetypeLook Base(Archetype archetype, int variant) => (archetype, variant) switch
    {
        (Archetype.Temperate, 0) => new()
        {
            DeepSea = Rgb.Hex("0b3a6e"), ShallowSea = Rgb.Hex("1c96b0"),
            SkyTop = Rgb.Hex("2468d8"), SkyHorizon = Rgb.Hex("a8e2f2"), SunColor = Rgb.Hex("fff1d8"),
            Atmosphere = Rgb.Hex("6ab8ff"), CloudCover = 0.38f, Nebula = Rgb.Hex("1a2a6a"),
            Moon = Rgb.Hex("f0e2c8"), MoonSize = 0.04f,
        },
        (Archetype.Temperate, _) => new()
        {
            DeepSea = Rgb.Hex("0a4a60"), ShallowSea = Rgb.Hex("16b0a0"),
            SkyTop = Rgb.Hex("2a86a8"), SkyHorizon = Rgb.Hex("d4f2e2"), SunColor = Rgb.Hex("fff6e0"),
            FogDensity = 0.0006f, Atmosphere = Rgb.Hex("70e0d0"), CloudCover = 0.5f,
            Nebula = Rgb.Hex("0a3a4a"), Moon = Rgb.Hex("d0f0e0"), MoonSize = 0.06f,
        },
        (Archetype.Arid, 0) => new()
        {
            DeepSea = Rgb.Hex("10507e"), ShallowSea = Rgb.Hex("2cc0b8"),
            SkyTop = Rgb.Hex("1c4aa6"), SkyHorizon = Rgb.Hex("f8d09a"), SunColor = Rgb.Hex("fff8e6"),
            SunEnergy = 1.25f, SunElevation = 44f, FogDensity = 0.00028f,
            Atmosphere = Rgb.Hex("f0b070"), CloudColor = Rgb.Hex("f8e0c0"), CloudCover = 0.12f,
            Nebula = Rgb.Hex("5a2a1a"), Moon = Rgb.Hex("8cc4dc"), MoonSize = 0.11f,
        },
        (Archetype.Arid, _) => new()
        {
            DeepSea = Rgb.Hex("10507e"), ShallowSea = Rgb.Hex("2cc0b8"),
            SkyTop = Rgb.Hex("244c98"), SkyHorizon = Rgb.Hex("f4ac72"), SunColor = Rgb.Hex("fff0d0"),
            SunEnergy = 1.3f, SunElevation = 30f, FogDensity = 0.00026f,
            Atmosphere = Rgb.Hex("ff9a5a"), CloudColor = Rgb.Hex("f8d8b8"), CloudCover = 0.18f,
            Nebula = Rgb.Hex("4a1a2a"), Moon = Rgb.Hex("f0e0d0"), MoonSize = 0.035f,
        },
        (Archetype.Ice, 0) => new()
        {
            DeepSea = Rgb.Hex("0c2250"), ShallowSea = Rgb.Hex("2a62a2"), FrozenSea = Rgb.Hex("a6cbea"),
           
            SkyTop = Rgb.Hex("2a2c72"), SkyHorizon = Rgb.Hex("f6c0d4"), SunColor = Rgb.Hex("ffd8bc"),
            SunEnergy = 1.05f, SunElevation = 14f, FogDensity = 0.0003f, Stars = 0.45f,
            Atmosphere = Rgb.Hex("b8b0ff"), CloudCover = 0.4f, Nebula = Rgb.Hex("2a1a5a"),
            Moon = Rgb.Hex("c8b8f0"), MoonSize = 0.09f,
        },
        (Archetype.Ice, _) => new()
        {
            DeepSea = Rgb.Hex("0c2c5a"), ShallowSea = Rgb.Hex("2a7aaa"), FrozenSea = Rgb.Hex("aed2ee"),
           
            SkyTop = Rgb.Hex("34488e"), SkyHorizon = Rgb.Hex("e6dcf2"), SunColor = Rgb.Hex("fff0e0"),
            SunEnergy = 1.15f, SunElevation = 22f, FogDensity = 0.0005f, Stars = 0.2f,
            Atmosphere = Rgb.Hex("a0d0ff"), CloudCover = 0.55f, Nebula = Rgb.Hex("1a2a5a"),
            Moon = Rgb.Hex("f0d0b0"), MoonSize = 0.05f,
        },
        (Archetype.Lava, 0) => new()
        {
            // Lava is hottest far from shore and crusts over darkly at the edges.
            DeepSea = Rgb.Hex("ff6414"), ShallowSea = Rgb.Hex("8a1a0a"), LiquidGlow = 1.6f,
           
            SkyTop = Rgb.Hex("2a0a12"), SkyHorizon = Rgb.Hex("d4502a"), SunColor = Rgb.Hex("ffa070"),
            SunEnergy = 0.9f, SunElevation = 26f, FogDensity = 0.0005f, Stars = 0.3f, Ambient = 0.25f,
            Atmosphere = Rgb.Hex("ff5a2a"), CloudColor = Rgb.Hex("4a3a3a"), CloudCover = 0.45f,
            Nebula = Rgb.Hex("4a0a0a"), Moon = Rgb.Hex("ffd0a0"), MoonSize = 0.07f,
        },
        (Archetype.Lava, _) => new()
        {
            DeepSea = Rgb.Hex("ff7a1a"), ShallowSea = Rgb.Hex("a0200a"), LiquidGlow = 1.9f,
           
            SkyTop = Rgb.Hex("3a0a08"), SkyHorizon = Rgb.Hex("ff7a2a"), SunColor = Rgb.Hex("ffb080"),
            SunEnergy = 0.85f, SunElevation = 20f, FogDensity = 0.0006f, Stars = 0.2f, Ambient = 0.25f,
            Atmosphere = Rgb.Hex("ff7a1a"), CloudColor = Rgb.Hex("3a2a2a"), CloudCover = 0.35f,
            Nebula = Rgb.Hex("5a1a0a"), Moon = Rgb.Hex("f0a080"), MoonSize = 0.12f,
        },
        (Archetype.Alien, 0) => new()
        {
            DeepSea = Rgb.Hex("0a4a5a"), ShallowSea = Rgb.Hex("2ad8c4"),
            SkyTop = Rgb.Hex("140a3c"), SkyHorizon = Rgb.Hex("46e2c4"), SunColor = Rgb.Hex("f0d8ff"),
            SunEnergy = 1.2f, SunElevation = 32f, FogDensity = 0.0003f, Stars = 0.7f, Ring = true, Ambient = 0.45f,
            RingColor = Rgb.Hex("ffb0e0"),
            Atmosphere = Rgb.Hex("50ffd0"), CloudColor = Rgb.Hex("e0c0ff"), CloudCover = 0.3f,
            Nebula = Rgb.Hex("3a0a5a"), Moon = Rgb.Hex("ff9a4a"), MoonSize = 0.16f,
        },
        _ => new()
        {
            DeepSea = Rgb.Hex("2a1050"), ShallowSea = Rgb.Hex("7a3aaa"),
            SkyTop = Rgb.Hex("3a2a72"), SkyHorizon = Rgb.Hex("d2ea6a"), SunColor = Rgb.Hex("fffad0"),
            SunEnergy = 1.2f, SunElevation = 34f, FogDensity = 0.0006f, Stars = 0.25f,
            Atmosphere = Rgb.Hex("c0ff60"), CloudColor = Rgb.Hex("f0e0a0"), CloudCover = 0.5f,
            Nebula = Rgb.Hex("1a3a1a"), Moon = Rgb.Hex("a0f080"), MoonSize = 0.08f,
        },
    };
}
