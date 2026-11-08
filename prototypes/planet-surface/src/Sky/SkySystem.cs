using System;
using System.Collections.Generic;
using Planets.Gen;
using Planets.Look;

namespace PlanetSurface.Skies;

public enum StarClass { RedDwarf, Orange, Yellow, White, BlueWhite }

public enum AirKind { Thin, Breathable, Humid, Dusty, Ash, Sulfurous, Exotic }

public enum BodyKind { Moon, GasGiant }

/// <summary>Something in the sky besides the sun: a moon or a neighbouring giant, with or without rings.</summary>
public sealed record SkyBody
{
    public BodyKind Kind { get; init; }
    public string Name { get; init; } = "";
    public Rgb Color { get; init; }
    public Rgb Band { get; init; }               // giants: the colour of their cloud bands
    public float Radius { get; init; }           // angular radius, radians
    public float Azimuth { get; init; }          // where it rises, radians from north
    public float Inclination { get; init; }      // how high its path climbs
    public float Period { get; init; }           // days per orbit across the sky
    public float Phase0 { get; init; }           // where it starts on that orbit
    public bool Rings { get; init; }
    public Rgb RingColor { get; init; }
    public float RingTilt { get; init; }         // radians: how open the ring ellipse looks
}

/// <summary>
/// A world's place in its system, as far as the sky is concerned: the star it circles, the air it
/// breathes, and what else hangs overhead. Derived deterministically from the planet spec, so
/// every player sees the same sky, and the sky follows the place:
/// - <b>Star</b>: its class sets the sunlight's colour and how big and bright the sun is. A red
///   dwarf's world has orange light and a muted teal-grey sky; a blue-white star bleaches it.
/// - <b>Air</b>: Rayleigh scattering of that starlight gives the clear-sky colour (blue under a
///   yellow star), then dust, ash, sulfur or exotic chemistry tint and thicken it. Earth-like
///   worlds read Earth-like; dusty worlds read dusty; only exotic air (or a nebula behind it)
///   earns pastel and neon colours.
/// - <b>Bodies</b>: moons, a neighbouring gas giant (ringed if the planet's RingedSky modifier
///   says the system has one), and sometimes a nebula backdrop, sized and placed to make sense.
/// </summary>
public sealed record SkySystem
{
    public StarClass Star { get; init; }
    public Rgb StarColor { get; init; }
    public float SunEnergy { get; init; }
    public AirKind Air { get; init; }
    public float Density { get; init; }          // 0.3 thin .. 2 thick: haze, horizon brightness, sunset strength
    public Rgb Zenith { get; init; }             // clear midday sky straight up
    public Rgb Horizon { get; init; }            // clear midday sky at the horizon
    public Rgb Sunset { get; init; }             // the glow toward a low sun
    public Rgb Haze { get; init; }               // aerosol colour: what fog and distance fade to
    public Rgb NightZenith { get; init; }
    public Rgb CloudTint { get; init; } = new(1f, 1f, 1f);   // what the clouds are made of: water white, ash brown, spores olive
    public float StarsByDay { get; init; }       // thin air shows stars even in daylight
    public bool Nebula { get; init; }
    public Rgb NebulaA { get; init; }
    public Rgb NebulaB { get; init; }
    public IReadOnlyList<SkyBody> Bodies { get; init; } = Array.Empty<SkyBody>();
    public float DayMinutes { get; init; } = 24f; // a full day-night cycle, real minutes
    public string Summary { get; init; } = "";

    public bool HasRingedBody
    {
        get
        {
            foreach (SkyBody b in Bodies) if (b.Rings) return true;
            return false;
        }
    }

    public static SkySystem For(PlanetSpec spec)
    {
        var rng = new Rng(Hash.Of(spec.Seed, 0x534B59, (long)spec.Archetype, spec.Variant));
        StarClass star = PickStar(spec, ref rng);
        Rgb starColor = StarColorOf(star);
        AirKind air = AirOf(spec);

        // Clear sky: starlight scattered as 1/lambda^4 (red, green, blue at 680, 550, 440 nm).
        Rgb rayleigh = Normalize(starColor * new Rgb(0.21f, 0.5f, 1f));
        float density = air switch
        {
            AirKind.Thin => 0.45f, AirKind.Breathable => 1f, AirKind.Humid => 1.35f, AirKind.Dusty => 1.1f,
            AirKind.Ash => 1.7f, AirKind.Sulfurous => 1.5f, _ => 1.2f,
        };
        Rgb zenith = rayleigh * 0.62f, horizon = Rgb.Lerp(rayleigh, starColor, 0.55f) * 0.95f;
        Rgb haze = Rgb.Lerp(horizon, starColor, 0.3f);
        Rgb sunset = Rgb.Lerp(starColor, new Rgb(1f, 0.42f, 0.16f), 0.65f);
        float hue = rng.NextFloat();

        switch (air)
        {
            case AirKind.Thin:
                // Little air to scatter light: a deep, dark zenith over a pale horizon.
                zenith = zenith * 0.45f;
                horizon = Rgb.Lerp(horizon, starColor, 0.35f) * 0.85f;
                break;
            case AirKind.Humid:
                zenith = Rgb.Lerp(zenith, horizon, 0.3f);
                horizon = Rgb.Lerp(horizon, new Rgb(0.9f, 0.92f, 0.9f), 0.45f);
                haze = Rgb.Lerp(haze, new Rgb(0.82f, 0.86f, 0.84f), 0.6f);
                break;
            case AirKind.Dusty:
                // Fine dust scatters warm: an ochre horizon fading to a dusty blue.
                Rgb dust = Rgb.Lerp(new Rgb(0.86f, 0.62f, 0.38f), new Rgb(0.8f, 0.5f, 0.3f), hue);
                zenith = Rgb.Lerp(zenith, dust * 0.55f, 0.35f);
                horizon = Rgb.Lerp(horizon, dust, 0.75f);
                haze = dust;
                sunset = Rgb.Lerp(sunset, new Rgb(0.95f, 0.45f, 0.2f), 0.5f);
                break;
            case AirKind.Ash:
                // Volcanic ash: a dark, brown-grey lid, lit red from below by lava.
                zenith = new Rgb(0.16f, 0.13f, 0.13f);
                horizon = new Rgb(0.44f, 0.27f, 0.2f);
                haze = new Rgb(0.32f, 0.22f, 0.19f);
                sunset = new Rgb(1f, 0.35f, 0.1f);
                break;
            case AirKind.Sulfurous:
                zenith = new Rgb(0.42f, 0.36f, 0.2f);
                horizon = new Rgb(0.92f, 0.66f, 0.3f);
                haze = new Rgb(0.8f, 0.58f, 0.3f);
                sunset = new Rgb(1f, 0.38f, 0.12f);
                break;
            case AirKind.Exotic:
                (zenith, horizon, haze, sunset) = ExoticAir(spec, hue);
                break;
        }

        Rgb cloudTint = air switch
        {
            AirKind.Dusty => Rgb.Hex("e8d8c0"),
            AirKind.Ash => Rgb.Hex("5a4e4a"),
            AirKind.Sulfurous => Rgb.Hex("d8c890"),
            AirKind.Exotic => Rgb.Lerp(Rgb.Hex("f0f0f0"), horizon, 0.35f),
            _ => Rgb.Hex("ffffff"),
        };
        bool nebula = rng.NextFloat() < (spec.Archetype == Archetype.Alien ? 0.75f : 0.18f) || spec.Has(Modifier.GlowingNights);
        float nh = rng.NextFloat();
        Rgb nebulaA = HueColor(nh, 0.75f), nebulaB = HueColor(Frac(nh + 0.18f + 0.3f * rng.NextFloat()), 0.65f);
        if (nebula && air != AirKind.Exotic && spec.Archetype == Archetype.Alien)
        {
            // A nebula washing the sky: its glow tints the haze a little even by day.
            horizon = Rgb.Lerp(horizon, nebulaA, 0.25f);
        }

        var bodies = MakeBodies(spec, ref rng, air);
        float energy = star switch
        {
            StarClass.RedDwarf => 1.1f, StarClass.Orange => 1.25f, StarClass.Yellow => 1.35f, StarClass.White => 1.5f, _ => 1.6f,
        };
        if (air == AirKind.Ash) energy *= 0.75f;

        string body = bodies.Count == 0 ? "no moons" : string.Join(", ", bodies.ConvertAll(b => b.Name));
        return new SkySystem
        {
            Star = star, StarColor = starColor, SunEnergy = energy, // its disc's size is the system's (StarSystem.SunAngular)
            Air = air, Density = density, Zenith = zenith, Horizon = horizon, Sunset = sunset, Haze = haze,
            NightZenith = Rgb.Lerp(new Rgb(0.01f, 0.012f, 0.025f), zenith * 0.05f, 0.5f),
            CloudTint = cloudTint,
            StarsByDay = air == AirKind.Thin ? 0.35f : 0f,
            Nebula = nebula, NebulaA = nebulaA, NebulaB = nebulaB, Bodies = bodies,
            DayMinutes = 18f + rng.NextFloat() * 14f,
            Summary = $"{StarName(star)} star · {AirName(air)} air · {body}{(nebula ? " · nebula" : "")}",
        };
    }

    static StarClass PickStar(PlanetSpec spec, ref Rng rng)
    {
        float r = rng.NextFloat();
        return spec.Archetype switch
        {
            Archetype.Temperate => r < 0.7f ? StarClass.Yellow : StarClass.Orange,
            Archetype.Arid => r < 0.55f ? StarClass.Yellow : StarClass.White,
            Archetype.Ice => r < 0.5f ? StarClass.White : r < 0.75f ? StarClass.BlueWhite : StarClass.Orange,
            Archetype.Lava => r < 0.5f ? StarClass.Orange : StarClass.RedDwarf,
            _ => r < 0.3f ? StarClass.BlueWhite : r < 0.55f ? StarClass.RedDwarf : r < 0.8f ? StarClass.White : StarClass.Yellow,
        };
    }

    static Rgb StarColorOf(StarClass s) => s switch
    {
        StarClass.RedDwarf => Rgb.Hex("ff9050"),
        StarClass.Orange => Rgb.Hex("ffc488"),
        StarClass.Yellow => Rgb.Hex("fff1dc"),
        StarClass.White => Rgb.Hex("f4f6ff"),
        _ => Rgb.Hex("c8dcff"),
    };

    static AirKind AirOf(PlanetSpec spec) => (spec.Archetype, spec.Variant) switch
    {
        (Archetype.Temperate, 0) => AirKind.Breathable,
        (Archetype.Temperate, _) => AirKind.Humid,
        (Archetype.Arid, _) => AirKind.Dusty,
        (Archetype.Ice, 0) => AirKind.Thin,
        (Archetype.Ice, _) => AirKind.Humid,
        (Archetype.Lava, 0) => AirKind.Ash,
        (Archetype.Lava, _) => AirKind.Sulfurous,
        _ => AirKind.Exotic,
    };

    /// <summary>Alien chemistry: the colours a strange atmosphere earns, a family per variant with seeded drift.</summary>
    static (Rgb zenith, Rgb horizon, Rgb haze, Rgb sunset) ExoticAir(PlanetSpec spec, float hue)
    {
        if (spec.Variant == 0)
        {
            // Crystal worlds: a fluorescing upper atmosphere, deep violet to teal and rose.
            Rgb zenith = Rgb.Lerp(Rgb.Hex("2a1a5e"), Rgb.Hex("1a2c66"), hue);
            Rgb horizon = Rgb.Lerp(Rgb.Hex("7fd8c8"), Rgb.Hex("e0a0d0"), hue);
            return (zenith, horizon, Rgb.Lerp(horizon, Rgb.Hex("b0a0d0"), 0.4f), Rgb.Hex("ff70b0"));
        }
        // Fungal worlds: spore haze, olive and lime over a warm amber horizon.
        Rgb z = Rgb.Lerp(Rgb.Hex("3a4a2a"), Rgb.Hex("2a3c3a"), hue);
        Rgb h = Rgb.Lerp(Rgb.Hex("cfd88a"), Rgb.Hex("e0c070"), hue);
        return (z, h, Rgb.Lerp(h, Rgb.Hex("a0a070"), 0.4f), Rgb.Hex("ff9a40"));
    }

    static List<SkyBody> MakeBodies(PlanetSpec spec, ref Rng rng, AirKind air)
    {
        var list = new List<SkyBody>();
        // A neighbouring giant: certain when the planet's modifiers say the sky has rings, likely on
        // alien worlds (they orbit giants), occasional elsewhere.
        float giantChance = spec.Has(Modifier.RingedSky) ? 1f : spec.Archetype == Archetype.Alien ? 0.6f : 0.22f;
        if (rng.NextFloat() < giantChance)
        {
            bool rings = spec.Has(Modifier.RingedSky) || rng.NextFloat() < 0.35f;
            float h = rng.NextFloat();
            // Exotic systems get any hue; elsewhere one of the familiar giant looks (a lerp between two
            // of them would land on grey), shaded a little either way so no two are quite alike.
            Rgb color = air == AirKind.Exotic ? HueColor(h, 0.45f)
                : Rgb.Lerp(GiantLooks[(int)(h * GiantLooks.Length) % GiantLooks.Length], Rgb.Hex("b0a898"), Frac(h * 7f) * 0.25f);
            list.Add(new SkyBody
            {
                Kind = BodyKind.GasGiant, Name = rings ? "a ringed giant" : "a gas giant",
                Color = color, Band = Rgb.Lerp(color, Rgb.Hex("5a4a3a"), 0.45f),
                Radius = 0.09f + rng.NextFloat() * 0.1f,
                Azimuth = rng.NextFloat() * MathF.Tau, Inclination = 0.25f + rng.NextFloat() * 0.4f,
                Period = 3f + rng.NextFloat() * 4f, Phase0 = 0.15f + rng.NextFloat() * 0.25f,
                Rings = rings, RingColor = Rgb.Lerp(Rgb.Hex("e8d8b8"), color, 0.3f), RingTilt = 0.18f + rng.NextFloat() * 0.3f,
            });
        }
        int moons = rng.Next(4); // 0-3
        for (int m = 0; m < moons; m++)
        {
            float tone = rng.NextFloat();
            list.Add(new SkyBody
            {
                Kind = BodyKind.Moon, Name = moons == 1 ? "a moon" : $"moon {m + 1} of {moons}",
                Color = Rgb.Lerp(Rgb.Hex("d8d2c8"), Rgb.Hex("b89a80"), tone),
                Radius = 0.012f + rng.NextFloat() * 0.03f,
                Azimuth = rng.NextFloat() * MathF.Tau, Inclination = 0.3f + rng.NextFloat() * 0.6f,
                Period = 0.6f + rng.NextFloat() * 2.5f, Phase0 = rng.NextFloat(),
            });
        }
        return list;
    }

    static Rgb Normalize(Rgb c)
    {
        float m = MathF.Max(c.R, MathF.Max(c.G, c.B));
        return m > 0f ? c * (1f / m) : c;
    }

    static float Frac(float v) => v - MathF.Floor(v);

    /// <summary>A soft colour from a hue in [0, 1).</summary>
    static readonly Rgb[] GiantLooks =
    {
        Rgb.Hex("d6ab78"),   // banded ochre
        Rgb.Hex("e3cd96"),   // pale gold
        Rgb.Hex("8fb4d8"),   // ice blue
        Rgb.Hex("86c4b4"),   // methane teal
        Rgb.Hex("c98f6a"),   // rust
    };

    static Rgb HueColor(float h, float saturation)
    {
        float r = MathF.Abs(h * 6f - 3f) - 1f, g = 2f - MathF.Abs(h * 6f - 2f), b = 2f - MathF.Abs(h * 6f - 4f);
        var c = new Rgb(Math.Clamp(r, 0f, 1f), Math.Clamp(g, 0f, 1f), Math.Clamp(b, 0f, 1f));
        return Rgb.Lerp(new Rgb(0.8f, 0.8f, 0.8f), c, saturation);
    }

    static string StarName(StarClass s) => s switch
    {
        StarClass.RedDwarf => "red dwarf", StarClass.Orange => "orange", StarClass.Yellow => "yellow", StarClass.White => "white", _ => "blue-white",
    };

    static string AirName(AirKind a) => a switch
    {
        AirKind.Thin => "thin", AirKind.Breathable => "breathable", AirKind.Humid => "humid", AirKind.Dusty => "dusty",
        AirKind.Ash => "ash-laden", AirKind.Sulfurous => "sulfurous", _ => "exotic",
    };
}
