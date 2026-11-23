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
/// - <b>Star</b>: its class sets the sunlight's colour (Planck's law at its temperature) and how
///   big and bright the sun is. A red dwarf's world has orange light under a grey-gold sky.
/// - <b>Air</b>: its kind and thickness, through the world's palette (<see cref="WorldPalette"/>):
///   Rayleigh scattering of that starlight gives the clear sky (blue under a yellow star), its trace
///   gas, dust, ash and sulfur colour and thicken it, and the art direction makes it bold.
/// - <b>Bodies</b>: moons, a neighbouring gas giant (ringed if the planet's RingedSky modifier
///   says the system has one), and sometimes a nebula backdrop, sized and placed to make sense.
/// </summary>
public sealed record SkySystem
{
    public StarClass Star { get; init; }
    public Rgb StarColor { get; init; }
    public float SunEnergy { get; init; }
    public AirKind Air { get; init; }
    /// <summary>How thick its air is, from the world's size (<see cref="PlanetSpec.AirThickness"/>): 0 in vacuum, 0.5 thin to 2 dense.</summary>
    public float Thickness { get; init; }
    /// <summary>Its air kind's own density at that thickness: haze, horizon brightness, sunset strength, the air shell seen from space.</summary>
    public float Density { get; init; }
    public Rgb Zenith { get; init; }             // clear midday sky straight up
    public Rgb Horizon { get; init; }            // clear midday sky at the horizon
    public Rgb Sunset { get; init; }             // the glow toward a low sun
    public Rgb Haze { get; init; }               // aerosol colour: what fog and distance fade to
    public Rgb NightZenith { get; init; }
    public Rgb CloudTint { get; init; } = new(1f, 1f, 1f);   // what the clouds are made of: water white, ash brown, spores olive
    /// <summary>What lights the ground in shadow: the sky's own colour, or with no air a deep colour opposite its star's.</summary>
    public Rgb Shade { get; init; }
    public float StarsByDay { get; init; }       // thin air shows stars even in daylight
    public bool Nebula { get; init; }
    public Rgb NebulaA { get; init; }
    public Rgb NebulaB { get; init; }
    public IReadOnlyList<SkyBody> Bodies { get; init; } = Array.Empty<SkyBody>();
    /// <summary>The world's natural colours: this sky's are read from it, and so are the ground's, the water's, the plants' and the globe's.</summary>
    public WorldPalette Palette { get; init; }
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

    /// <summary>Tests and shots: every world rolled from here on circles this star (null: its own).</summary>
    public static StarClass? ForceStar;

    /// <summary>The real skies: every world's palette in the game is its sky's, made with its own star and air (a generator alone has none).</summary>
    [System.Runtime.CompilerServices.ModuleInitializer]
    internal static void UseRealSkies() => WorldPalette.Of = spec => For(spec).Palette;

    /// <param name="star">the star it really circles, where that isn't the one its own spec would pick (a moon in another world's system)</param>
    public static SkySystem For(PlanetSpec spec, StarClass? star = null)
    {
        var rng = new Rng(Hash.Of(spec.Seed, 0x534B59, (long)spec.Archetype, spec.Variant));
        StarClass picked = PickStar(spec, ref rng);
        StarClass sun = star ?? ForceStar ?? picked;
        AirKind air = AirOf(spec);
        float density = air switch
        {
            AirKind.Thin => 0.45f, AirKind.Breathable => 1f, AirKind.Humid => 1.35f, AirKind.Dusty => 1.1f,
            AirKind.Ash => 1.7f, AirKind.Sulfurous => 1.5f, _ => 1.2f,
        } * spec.AirThickness;

        // Every natural colour from one model: this star's light through this air onto this ground (WorldPalette).
        // The gas column is the air's density; its particles by kind, thicker in thicker air: clean air's sea salt
        // and sulfate, humid air's water haze, the ground's own dust in dry and thin air (Mars), ash over lava,
        // sulfuric acid and sulfur gas in sulfurous air (Venus).
        (Particles particles, float aerosol) = air switch
        {
            AirKind.Humid => (Particles.Haze, 0.12f), AirKind.Dusty => (Particles.Dust, 0.25f), AirKind.Thin => (Particles.Dust, 0.3f),
            AirKind.Ash => (Particles.Ash, 2.5f), AirKind.Sulfurous => (Particles.Sulfate, 1.2f),
            AirKind.Exotic when spec.Variant == 1 => (Particles.Haze, 0.15f), // a fungal world's spores
            _ => (Particles.Clean, 0.04f),
        };
        AirMix mix = spec.Airless ? AirMix.None : new AirMix(density, particles, aerosol * spec.AirThickness, air is AirKind.Breathable or AirKind.Humid);
        WorldPalette palette = WorldPalette.Make(spec, WorldPalette.Kelvins[(int)sun], mix);
        Rgb starColor = palette.Star;
        rng.NextFloat(); // the roll the exotic air's hand colours took, so every world's bodies and nebula stay as they were
        Rgb zenith = palette.Zenith, horizon = palette.Horizon, haze = palette.Haze, sunset = palette.Sunset, cloudTint = palette.CloudTint;
        bool nebula = rng.NextFloat() < (spec.Archetype == Archetype.Alien ? 0.75f : 0.18f) || spec.Has(Modifier.GlowingNights);
        float nh = rng.NextFloat();
        Rgb nebulaA = HueColor(nh, 0.75f), nebulaB = HueColor(Frac(nh + 0.18f + 0.3f * rng.NextFloat()), 0.65f);

        var bodies = MakeBodies(spec, ref rng, air);
        float energy = sun switch
        {
            StarClass.RedDwarf => 1.1f, StarClass.Orange => 1.25f, StarClass.Yellow => 1.35f, StarClass.White => 1.5f, _ => 1.6f,
        };
        if (air == AirKind.Ash) energy *= 0.75f;

        string body = bodies.Count == 0 ? "no moons" : string.Join(", ", bodies.ConvertAll(b => b.Name));
        return new SkySystem
        {
            Star = sun, StarColor = starColor, SunEnergy = energy, Palette = palette, // its disc's size is the system's (StarSystem.SunAngular)
            Air = air, Thickness = spec.AirThickness, Density = density, Zenith = zenith, Horizon = horizon, Sunset = sunset, Haze = haze,
            NightZenith = Rgb.Lerp(new Rgb(0.01f, 0.012f, 0.025f), zenith * 0.05f, 0.5f),
            CloudTint = cloudTint, Shade = palette.Shade,
            StarsByDay = air == AirKind.Thin ? 0.35f : 0f,
            Nebula = nebula, NebulaA = nebulaA, NebulaB = nebulaB, Bodies = bodies,
            DayMinutes = 18f + rng.NextFloat() * 14f,
            Summary = $"{StarName(sun)} star · {AirName(air)} air · {body}{(nebula ? " · nebula" : "")}",
        };
    }

    /// <summary>
    /// A body with no air under a system's star: nothing scatters the light, so the sky is black by
    /// day as by night, the stars out in daylight, no haze, no clouds, no sunset glow. Its grey rock
    /// takes its colour from its light: the starlight's own on the sunlit faces, and in shadow a deep
    /// colour across the hue circle from it, held to blue, violet and magenta, the stronger the further the star is from white (a grey lit by a warm light
    /// reads cool in its shadows, the simultaneous contrast painters exaggerate; owner, 23 Nov: "deep grey
    /// moons, but saturated").
    /// </summary>
    public static SkySystem Vacuum(SkySystem star)
    {
        var black = new Rgb(0.004f, 0.004f, 0.006f);
        Oklab light = Oklab.Of(star.StarColor);
        float hue = light.Hue + MathF.PI, low = 250f * MathF.PI / 180f, high = 330f * MathF.PI / 180f;
        hue = (hue % MathF.Tau + MathF.Tau) % MathF.Tau;
        if (hue < low || hue > high) hue = Oklab.HueGap(hue, low) < Oklab.HueGap(hue, high) ? low : high;
        return star with
        {
            Air = AirKind.Thin, Thickness = 0f, Density = 0.02f, Zenith = black, Horizon = black, Haze = black, Sunset = black,
            NightZenith = black, StarsByDay = 1f, Nebula = star.Nebula, Shade = Oklab.Lch(0.55f, Math.Clamp(0.06f + 2f * light.Chroma, 0.06f, 0.17f), hue).ToRgb(),
            Summary = $"{StarName(star.Star)} star · no air",
        };
    }

    static StarClass PickStar(PlanetSpec spec, ref Rng rng)
    {
        float r = rng.NextFloat();
        return spec.Archetype switch
        {
            // Life as we know it under any long-lived star: red dwarfs (the commonest hosts of temperate worlds), orange,
            // yellow and white (F) stars; a blue-white A star burns out too soon for it.
            Archetype.Temperate => r < 0.2f ? StarClass.RedDwarf : r < 0.5f ? StarClass.Orange : r < 0.85f ? StarClass.Yellow : StarClass.White,
            Archetype.Arid => r < 0.2f ? StarClass.Orange : r < 0.65f ? StarClass.Yellow : StarClass.White,
            Archetype.Ice => r < 0.5f ? StarClass.White : r < 0.75f ? StarClass.BlueWhite : StarClass.Orange,
            Archetype.Lava => r < 0.5f ? StarClass.Orange : StarClass.RedDwarf,
            _ => r < 0.3f ? StarClass.BlueWhite : r < 0.55f ? StarClass.RedDwarf : r < 0.8f ? StarClass.White : StarClass.Yellow,
        };
    }


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
            var (color, band) = Giant(h, h < 0.4f);
            list.Add(new SkyBody
            {
                Kind = BodyKind.GasGiant, Name = rings ? "a ringed giant" : "a gas giant",
                Color = color, Band = band,
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

    static float Frac(float v) => v - MathF.Floor(v);

    /// <summary>
    /// A giant's cloud tops and its darker belts, by what colours them (<paramref name="tone"/> 0..1 picks): ammonia ice
    /// (cream), tholins (orange), the red chromophores of Jupiter's Great Red Spot (crimson), sulfur (gold), ammonium
    /// hydrosulfide (brown); an ice giant's methane drinking the red (azure, teal, deep blue, aquamarine, violet-blue under a
    /// high haze). The belts are another of the same kind, deeper: cream and brown giants as well as crimson and azure ones.
    /// </summary>
    public static (Rgb Color, Rgb Band) Giant(float tone, bool ice)
    {
        string[] tops = ice ? new[] { "3a9ae8", "2ab8b0", "2a50d0", "60d0c0", "6a60e0" } : new[] { "f2dca0", "e07a3a", "c83a3a", "e8b83a", "a8683a" };
        int i = Math.Clamp((int)(tone * tops.Length), 0, tops.Length - 1);
        return (Rgb.Hex(tops[i]), Oklab.Vivid(Rgb.Hex(tops[(i + 2) % tops.Length]), 1f, 0f, 0.72f));
    }

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
