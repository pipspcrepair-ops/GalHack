using System;
using Planets.Gen;

namespace Planets.Look;

/// <summary>What a world's rock is, under its soil and in its sand: the mineral every ground colour starts from.</summary>
public enum RockType { Granite, Basalt, Andesite, Sandstone, Limestone, Shale, Anorthosite, Ice }

/// <summary>What hangs in a world's air besides its gas: sea salt and sulfate (clean), water haze, the ground's own dust, volcanic ash, sulfuric acid with sulfur.</summary>
public enum Particles { Clean, Haze, Dust, Ash, Sulfate }

/// <summary>A gas in a world's air that absorbs part of the visible light, and so colours its sky: ozone (violet-blue), chlorine (green), iodine (magenta), nitrogen dioxide (amber), methane (teal), sulfur vapour (gold), bromine (orange-red).</summary>
public enum Trace { None, Ozone, Chlorine, Iodine, NitrogenDioxide, Methane, Sulfur, Bromine }

/// <summary>The pigment kit a world's plants gather light with besides their reaction centre: what they throw back is their colour.</summary>
public enum Pigment { Chlorophyll, Phycocyanin, Phycoerythrin, Anthocyanin, Carotenoid, Retinal, Melanin }

/// <summary>What colours a world's water besides the water itself: what lives in it, what is dissolved in it, what hangs in it.</summary>
public enum Waters { Clear, Halophiles, Iron, Copper, Flour, PurpleSulfur, Cyanobacteria, Blackwater, Sulfur }

/// <summary>A world's commonest ore, weathered into its soil and dust beside the iron: sulfur, copper carbonates, manganese, cobalt bloom, glauconite, carbon.</summary>
public enum Stain { None, Sulfur, Malachite, Azurite, Manganese, Cobalt, Glauconite, Carbon }

/// <summary>
/// How a world reads (owner, 23 Nov): wholly earthy, in today's look (the hand colours the game had before the palette),
/// wholly vivid, or a mix of earthy and vivid parts. A wholly vivid world is candy (every part at full strength: owner, 23 Nov,
/// "Candy worlds are fine, as long as every world isn't a candy world") or pairs its strong parts against a deep one.
/// </summary>
public enum PaletteKind { Earthy, Today, Vivid, Candy, Mixed }

/// <summary>A world's four natural parts: its sky, its ground, its plants and its water.</summary>
[Flags]
public enum Parts { None = 0, Sky = 1, Ground = 2, Plants = 4, Water = 8 }

/// <summary>
/// A world's air as its colours need it: its gas column against Earth's (Rayleigh scattering grows with it;
/// 0 is vacuum), what particles it holds and how thick they are (optical depth at 550 nm), and whether it
/// has oxygen, and so ozone.
/// </summary>
public readonly record struct AirMix(float Column, Particles Particles, float Aerosol, bool Oxygen)
{
    public static readonly AirMix None = new(0f, Particles.Clean, 0f, false);
}

/// <summary>
/// One world's natural colours, all from one model: the starlight (Planck's law at the star's
/// temperature), what its air scatters and absorbs, what its rock and soil minerals reflect, what its
/// water absorbs and holds, and the pigments its plants evolved under that light. Every natural colour
/// in the game reads this: the sky from the ground, the air and seas of the globe from space, the
/// ground, rock and sand, the water and the plants; weather reads its haze, cloud, dust and water
/// colours. Colours are worked band by band (<see cref="Spectrum"/>) and only seen at the end, so a
/// red dwarf's light reddens everything it falls on for the same reason it does in life. Made things
/// (paint, roofs, cloth) take a world's rolled palette instead and never come from here.
///
/// The physics picks each world's hues: its trace gas, its plants' pigment kit, its water's chemistry and its
/// soil's ore, each rolled with real odds. Where the rolls allow, the land takes the kit or ore whose hue
/// stands furthest from its sky's, and the water the chemistry furthest from its land's. Each part's colour
/// is then as vivid as its own chemistry makes it (owner, 23 Nov): iron oxides, clays, basalt, carbon and dust
/// give earth tones, left as the light makes them and held to a natural strength; sulfur, copper, chlorine and
/// bromine compounds, dissolved minerals, vivid pigments in life and unusual air chemistry are pushed far past
/// realistic at their own hue (<see cref="Bold"/>). A world of earthy makers is wholly natural, one rich in vivid
/// makers wholly vivid (candy, or its strong parts paired against a deep one), and most mix the two; about one
/// in five keeps today's look instead (<see cref="IsToday"/>). Sources are cited in prototypes/planet-generator/README.md (Palette).
/// </summary>
public sealed record WorldPalette
{
    // ---- What the world is made of (rolled, so it can be printed and tested) ----
    /// <summary>How the world reads: earthy, today's look, vivid (paired against a deep part), candy or mixed.</summary>
    public PaletteKind Kind { get; init; }
    public bool Today => Kind == PaletteKind.Today;
    /// <summary>Its ground, rock, dust and globe from the hand tables (<see cref="BiomeLook.Ground"/>, <see cref="Hands"/>): today's look and every airless body (proto/vacuum's deep greys, which the palette's light and shade tint).</summary>
    public bool Hand { get; init; }
    /// <summary>
    /// The parts the world has (no plants where nothing lives, no water on a lava world); of them, those whose chemistry is vivid,
    /// and the one a wholly vivid world that isn't candy keeps deep against the rest.
    /// </summary>
    public Parts Has { get; init; }
    public Parts VividParts { get; init; }
    public Parts DeepParts { get; init; }
    public float Kelvin { get; init; }
    public AirMix Air { get; init; }
    public RockType Rock { get; init; }
    /// <summary>How much iron the rock carries, 0..1: what its soils can rust into.</summary>
    public float Iron { get; init; }
    /// <summary>Life, 0..1: none on a dead world, 1 on a lush one.</summary>
    public float Life { get; init; }
    /// <summary>Phytoplankton in the water, mg of pigment a m³ (0.05 a clear open sea, several in a green one).</summary>
    public float Plankton { get; init; }
    public Trace Gas { get; init; }
    public Pigment Kit { get; init; }
    /// <summary>Its plants carry a wax bloom or fine hairs that throw back every colour: silver.</summary>
    public bool Silver { get; init; }
    public Waters Water { get; init; }
    public Stain Ore { get; init; }

    // ---- Light and sky ----
    /// <summary>The starlight's colour at its brightest channel 1, as the adapted eye sees it on the ground: warm under a red dwarf, cool under a hot star.</summary>
    public Rgb Star { get; init; }
    /// <summary>The star's disc as it is: a small bright source no eye adapts to, so a red dwarf's disc stays red-orange.</summary>
    public Rgb Disc { get; init; }
    /// <summary>Clear midday sky straight up: starlight scattered by the gas, more for short waves (Rayleigh), and by the particles.</summary>
    public Rgb Zenith { get; init; }
    /// <summary>Clear sky toward the horizon: a long path, every wave scattered more fully, paler than the zenith.</summary>
    public Rgb Horizon { get; init; }
    /// <summary>The bright band along the horizon: light that crossed the whole air to reach the eye, coloured by what the long path lets through.</summary>
    public Rgb Band { get; init; }
    /// <summary>What distance fades to (fog's colour): the horizon's light where the path never ends.</summary>
    public Rgb Haze { get; init; }
    /// <summary>The sun's own light through 25 airmasses: the short waves scattered out, particles deepening it.</summary>
    public Rgb Sunset { get; init; }
    /// <summary>The air's rim seen from space: starlight scattered once, the gas's own colour at its strongest.</summary>
    public Rgb Limb { get; init; }
    /// <summary>What clouds are made of: water and ice white, mixed with the air's dust, ash or sulfur.</summary>
    public Rgb CloudTint { get; init; }
    /// <summary>The ground's fines lifted into the air: what dust storms and devils are.</summary>
    public Rgb Dust { get; init; }
    /// <summary>What lights the ground in shadow: the dome of the sky's own light, its colour at full strength (black with no air).</summary>
    public Rgb Shade { get; init; }

    // ---- Ground ----
    /// <summary>The bare rock: its mineral, stained by what iron its weather lets rust.</summary>
    public Rgb Bedrock { get; init; }
    /// <summary>Loose grains of the rock (beaches, dunes): black on a basalt world, white on a limestone one.</summary>
    public Rgb Sand { get; init; }
    public Rgb Snow { get; init; }

    // ---- Water ----
    /// <summary>Deep water seen from above: what comes back up after the water, its life, its dissolved matter and its silt absorb.</summary>
    public Rgb Deep { get; init; }
    /// <summary>Four metres of the same water over the world's own sand.</summary>
    public Rgb Shallow { get; init; }
    /// <summary>Sea ice under a skin of snow.</summary>
    public Rgb Ice { get; init; }

    // ---- Life ----
    /// <summary>The leaf colour its plants evolved under this light: their reaction centre at the light's photon peak and their kit beside it.</summary>
    public Rgb Leaf { get; init; }
    /// <summary>Where that pigment absorbs most, nm: where the light at the ground has most photons in the visible.</summary>
    public float Peak { get; init; }

    Rgb[] _soils;          // the climate's soils: warmth (rows) by wetness (columns)
    const int Grid = 6;

    /// <summary>
    /// A world's palette from its spec. A game that knows the world's real star and air replaces this
    /// (the sky model does, at load); a generator alone sees every world under a sunlike star in Earth's air.
    /// </summary>
    public static Func<PlanetSpec, WorldPalette> Of = spec => Make(spec, 5800f, spec.Airless ? AirMix.None : new AirMix(1f, Particles.Clean, 0.1f, true));

    /// <summary>Kelvin by star class, coolest first (red dwarf, orange, yellow, white, blue-white): M, K, G, F and A main-sequence stars.</summary>
    public static readonly float[] Kelvins = { 3300f, 4400f, 5800f, 7000f, 9000f };

    /// <summary>How much of a world lives, by type: what its plants, plankton and soils' humus come from.</summary>
    public static float LifeOf(PlanetSpec spec) => spec.Airless ? 0f : spec.Archetype switch
    {
        Archetype.Temperate => 1f, Archetype.Alien => 0.85f, Archetype.Arid => 0.45f, Archetype.Ice => 0.4f, _ => 0.12f,
    };

    public static WorldPalette Make(PlanetSpec spec, float kelvin, AirMix air)
    {
        if (IsToday(spec)) return TodayLook(spec, kelvin, air);
        (string handShallow, string handDeep, string handDust, string handRock) = Hands(spec);
        ulong h = Hash.Of(spec.Seed, 0x47454F);
        float U(int k) => Hash.Unit(h, k);
        RockType rock = RockOf(spec, U(0));
        // Iron: most rock carries some; one world in four is iron-rich (red beds, Mars).
        float iron = U(1) > 0.75f ? 0.75f + 0.25f * U(2) : 0.12f + 0.5f * U(2);
        float life = LifeOf(spec);
        // The air's particles come and go by world, as Earth's do from place to place and Mars' from season to season:
        // a third to two and a half times its kind's usual, evenly on a log scale.
        air = air with { Aerosol = air.Aerosol * MathF.Exp(MathF.Log(0.35f) + MathF.Log(2.5f / 0.35f) * U(3)) };
        bool wet = air.Column > 0f && !spec.Airless;

        Spectrum light = Spectrum.Blackbody(kelvin);
        light *= 1f / light.Y;
        // The lit ground is seen by an eye adapted part of the way to its star, the sky less: grey rock takes the star's
        // cast (warm under a red dwarf, cool under a hot star), and the sky keeps more of it still.
        Spectrum.Eye eye = light.Adapted(Adaptation), skyEye = light.Adapted(SkyAdaptation);
        float starOff = Math.Clamp(MathF.Abs(kelvin - 5800f) / 2600f, 0f, 1f); // a star far from the Sun's colour: its light and sky the more vivid
        // How rich the world is in the vivid makers (sulfur, copper, chlorine and iodine, vivid pigments, dissolved minerals) against
        // the earthy ones (iron oxides, clays, basalt, carbon, dust): each part's chemistry is drawn from one or the other by it, so a
        // poor world is wholly natural, a rich one wholly vivid, and most mix the two (owner, 23 Nov).
        // Drawn toward either end: wholly natural and wholly vivid worlds both common, the rest between.
        float rich = 0.5f + 0.5f * MathF.CopySign(MathF.Pow(MathF.Abs(2f * U(35) - 1f), 0.6f), U(35) - 0.5f);
        (T, float)[] Makers<T>((T, float)[] odds, int k, Func<T, float> vividness) => Sort(odds, U(36 + k) < rich, vividness);

        // ---- Minerals and soils (an airless body's are the hand tables': Hand) ----
        Spectrum stone = Stone(rock);
        // On a volcanic world the iron in fresh lava oxidises as it cools: glassy black flows where there's little of it,
        // red-brown scoria where there's much (Mars' and Hawaii's cinder cones).
        if (spec.Archetype == Archetype.Lava && !spec.Airless) stone = Rust(rock is RockType.Basalt or RockType.Andesite ? stone * (1f + 1.5f * iron) : stone, 0.6f * iron, 0.2f * iron); // dark lava's scoria is frothier, so brighter, than its flows
        float stain = wet ? iron : 0f;
        // Grains are brighter than the face they came from (small particles scatter more), and keep its iron; the finest
        // grains rust most (dust is the reddest of the ground), and take the world's ore with them where there is weather.
        float oreAmount = 0.5f + 0.5f * U(20);
        Spectrum Fines(Stain ore) => Stained(stone * 1.3f, ore, wet ? oreAmount : 0f);
        Spectrum DustOf(Stain ore) => Rust(Fines(ore), 0.85f * iron, 0.2f * iron);

        // ---- Air ----
        // The trace gas that colours the sky (its kind by what the air is) and its amount, a column's optical depth at its peak.
        float traceDepth = 0.25f + 1.1f * U(22);
        // Optical depths straight up, by wavelength (nm), so the visible bands and the near infrared the plants see agree.
        float column = air.Column;
        bool hasSky = column > 0f;
        float RayleighAt(float l)
        {
            float um = l / 1000f, u2 = 1f / (um * um);
            return 0.008569f * u2 * u2 * (1f + 0.0113f * u2 + 0.00013f * u2 * u2) * column; // Hansen & Travis 1974
        }
        // The particles: how much they scatter by wavelength (Ångström).
        float angstrom = air.Particles switch { Particles.Haze => 0.5f, Particles.Dust => 0.1f, Particles.Ash => 0.3f, Particles.Sulfate => 1.1f, _ => 1.3f };
        float ParticlesAt(float l) => air.Aerosol * MathF.Pow(l / 550f, -angstrom);
        // Gases that absorb: ozone's Chappuis band (deepens twilight blue) and oxygen's A band where there is oxygen;
        // water vapour's near-infrared bands on any world with seas under its air; over Venus-like sulfuric haze,
        // sulfur and its oxides drinking the blue; and the trace gas.
        float GasAt(float l, Trace gas) => column * ((air.Oxygen ? 0.03f * G(l, 600f, 45f) + G(l, 762f, 6f) : 0f)
            + (wet ? 0.2f * G(l, 722f, 12f) + 0.35f * G(l, 820f, 18f) + 1.5f * G(l, 940f, 25f) : 0f)
            + (air.Particles == Particles.Sulfate ? 0.35f / (1f + MathF.Exp((l - 470f) / 25f)) : 0f)
            + traceDepth * Absorbs(gas, l));
        Spectrum rayleigh = Spectrum.Of(RayleighAt), aerosol = Spectrum.Of(ParticlesAt);
        float even = 1f - MathF.Exp(-air.Aerosol);

        // The sky from its trace gas and the ground's dust (a dusty sky is its ground's colour lifted into the air), as vivid as its
        // chemistry: the more, the deeper its zenith, the brighter its graded horizon, the more its band's own colour, and the more
        // its own colour lights the shadows. A plain hazy or dusty sky stays as the light makes it.
        (Rgb Zenith, Rgb Horizon, Rgb Band, Rgb Haze, Rgb Sunset, Rgb Limb, Rgb Shade, Spectrum Albedo) SkyOf(Trace gas, Spectrum dust)
        {
            if (!hasSky) return default;
            // What share of the light the particles don't absorb (single-scattering albedo).
            Spectrum albedo = air.Particles switch
            {
                Particles.Haze => Spectrum.Flat(0.99f),
                Particles.Dust => Albedo(dust),
                Particles.Ash => Spectrum.Of(l => 0.5f + 0.06f * (l - 400f) / 300f), // dark, near grey: glass and rock shards
                Particles.Sulfate => Spectrum.Flat(0.99f), // Venus' clouds: sulfuric acid, nearly white
                _ => Spectrum.Flat(0.96f),
            };
            Spectrum extinction = rayleigh + aerosol + Spectrum.Of(l => GasAt(l, gas));
            Spectrum scatter = rayleigh + aerosol * albedo;
            // Single scattering of the sun 45° up: the gas's Rayleigh phase, the particles' forward lobe. Thick particles
            // scatter light many times over, which evens their lobe out toward the same in every direction.
            Spectrum Sky(float viewAirmass, float rayleighPhase, float particlePhase)
            {
                float lobe = particlePhase + (1f - particlePhase) * even;
                Spectrum share = (rayleigh * rayleighPhase + aerosol * albedo * lobe) / extinction.Map(v => MathF.Max(v, 1e-5f));
                return light * share * (Spectrum.Flat(1f) - Spectrum.Exp(extinction * -viewAirmass)) * Spectrum.Exp(extinction * -0.7f);
            }
            Rgb zenith = Sky(1f, 1f, 0.8f).Light(SkyGain, skyEye); // the sun higher than 45° at midday: further from its glare
            Spectrum horizon = Sky(10f, 0.8f, 0.5f);
            Rgb sunset = (light * Spectrum.Exp(extinction * -12f)).Hue(skyEye); // the sun about 5° up
            // An earthy sky (no trace gas, or a natural one: ozone's blue, nitrogen dioxide's amber, dust) is as the light makes
            // it, held to a natural strength, its horizon paler than its zenith and its band its horizon's own.
            if (Vivid(gas) < 0.5f)
            {
                Rgb pale = Held(horizon.Light(SkyGain, skyEye), EarthyHorizon);
                return (Held(zenith, EarthyMost), pale, Held((horizon * Spectrum.Exp(extinction * -2.5f)).Hue(skyEye), EarthyHorizon),
                        Held(Sky(40f, 0.8f, 0.5f).Light(SkyGain, skyEye), EarthyHorizon), sunset, (light * scatter).Hue(skyEye), Held(zenith, EarthyMost), albedo);
            }
            // A vivid sky, its gas's own colour pushed (haze and dust mute it a little, never back to earthy): a deep zenith over a
            // bright horizon, and along the horizon a glowing band of the colour the long path lets through, turned well away from
            // the zenith's hue (blue air, a warm band; air that drinks the red, a cyan one), so the sky reads graded, never one tint.
            float v = Math.Clamp((Vivid(gas) + 0.3f * starOff) * MathF.Exp(-0.4f * air.Aerosol), 0.8f, 1f);
            Rgb deep = Oklab.Vivid(zenith, 1f + 2f * v, 0.15f * v, 1f - 0.5f * v, most: SkyMost);
            Rgb bright = Oklab.Vivid(horizon.Light(SkyGain, skyEye), 1f + 1.8f * v, 0.13f * v, 1.1f, most: SkyMost);
            return (deep, Apart(bright, Oklab.Of(deep).Hue, MathF.PI / 4f),
                    Apart(Oklab.Vivid((horizon * Spectrum.Exp(extinction * -2.5f)).Hue(skyEye), 1f + 1.2f * v, 0.2f * v, 1f - 0.04f * v, most: SkyMost + 0.03f), Oklab.Of(deep).Hue, MathF.PI / 3f),
                    Oklab.Vivid(Sky(40f, 0.8f, 0.5f).Light(SkyGain, skyEye), 1f + 1.8f * v, 0.12f * v, most: SkyMost),
                    Oklab.Vivid(sunset, 1f + 0.6f * v, 0.2f * v, 1f - 0.05f * v),
                    Oklab.Vivid((light * scatter).Hue(skyEye), 1f + 0.6f * v, 0.12f * v),
                    Oklab.Vivid(zenith, 1f + 2.4f * v, 0.17f * v), albedo);
        }
        // The sky's hue as a whole: its zenith's and its band's together.
        static float HueOf(Rgb zenith, Rgb band)
        {
            Oklab z = Oklab.Of(zenith), b = Oklab.Of(band);
            return MathF.Atan2(z.B + b.B, z.A + b.A);
        }
        Rgb SoilOf(Stain ore, float warm, float wet) => Part(Stained(Soil(Fines(Stain.None), warm, wet, iron, life), ore, oreAmount), GroundBoost, Vivid(ore)); // the ore over all a soil's weathering

        // Where the air is the ground's own dust, the sky is that dust lofted: burnt orange and rust over rust as often as not (owner,
        // 23 Nov), a trace gas rolled plainly. Over ash the ground comes first and the trace gas is the one of a few rolled that
        // stands furthest in hue from it; elsewhere the sky comes first and the ore is the one of a few rolled that stands furthest
        // from the sky. (Each is one the world might hold: only which one shows is chosen.)
        Trace gas = Trace.None;
        var gases = Makers(TraceOdds(air), 0, g => Vivid(g));
        var ores = Makers(StainOdds(spec), 1, o => Vivid(o));
        Stain ore = wet ? Roll(ores, U(23)) : Stain.None; // no weather, no ore weathered out
        if (hasSky && air.Particles == Particles.Dust) gas = Roll(gases, U(21));
        else if (hasSky && air.Particles == Particles.Ash)
            gas = Contrast(gases, h, 21, g => { var s = SkyOf(g, DustOf(ore)); return Oklab.Lch(0.6f, 0.1f, HueOf(s.Zenith, s.Band)).ToRgb(); }, Oklab.Of(SoilOf(ore, 0.6f, 0.5f)).Hue);
        else if (hasSky)
        {
            gas = Roll(gases, U(21));
            var s = SkyOf(gas, DustOf(Stain.None));
            ore = Contrast(ores, h, 24, o => SoilOf(o, 0.6f, 0.5f), HueOf(s.Zenith, s.Band));
        }
        Spectrum fines = Fines(ore), dust = DustOf(ore);
        var sky = SkyOf(gas, dust);
        float skyHue = HueOf(sky.Zenith, sky.Band);
        Spectrum bedrock = Stained(Rust(stone, 0.35f * stain * (spec.Archetype == Archetype.Arid ? 1.6f : 1f), 0.25f * stain), ore, wet ? 0.5f * oreAmount : 0f);
        Spectrum sand = Rust(fines, 0.3f * stain, 0.2f * stain);
        var soils = new Rgb[Grid * Grid];
        for (int t = 0; t < Grid; t++)
            for (int w = 0; w < Grid; w++)
                soils[t * Grid + w] = SoilOf(ore, t / (Grid - 1f), w / (Grid - 1f));

        // ---- Plants ----
        // The light at the ground with the sun 45° up, into the near infrared: oxygen's and water vapour's bands carve it
        // there, which puts the Sun's photon peak at the ground near 685 nm, where chlorophyll absorbs (Kiang et al. 2007).
        // The kit beside it is the one of a few rolled for this starlight whose colour stands furthest from the sky.
        Func<float, float> through = l => MathF.Exp(-1.4f * (RayleighAt(l) + ParticlesAt(l) + GasAt(l, gas)));
        bool silver = U(25) < 0.15f;
        Spectrum LeafOf(Pigment kit) => Leaves(kelvin, through, k => U(4 + k), kit, silver, out _);
        var kits = Makers(PigmentOdds(kelvin), 2, k => Vivid(k));
        Pigment pigment = life > 0f && hasSky ? Contrast(kits, h, 26, k => LeafOf(k).Reflectance(), skyHue) : Roll(kits, U(26));
        Spectrum leaf = Leaves(kelvin, through, k => U(4 + k), pigment, silver, out float peak);
        // A broad dark pigment (life gathering every photon of dim or red light) stays deep: umber and charcoal, never lightened.
        Rgb leafColor = silver ? Held(leaf.Reflectance(), 0.05f) : Part(leaf, LeafBoost, Vivid(pigment)); // a wax bloom throws back every colour: silver

        // ---- Water ----
        // Phytoplankton by how much lives (clear seas to green ones), dissolved matter browning still water,
        // silt from the land's rivers, a little on every world with weather; and the water's own chemistry, the one of a
        // few rolled for this kind of world that stands furthest from the land's colour.
        float plankton = life * MathF.Pow(10f, -1.5f + 2.5f * U(12));
        float tannin = 0.01f + 0.5f * U(13) * U(13) * U(13) * (0.3f + life);
        float silt = wet ? 0.4f + 10f * U(14) * U(14) : 0f;
        float solute = 0.4f + 1.2f * U(30);
        Oklab land = Oklab.Of(life >= 0.8f ? leafColor : SoilOf(ore, 0.6f, 0.5f));
        // The open sea holds a tenth of the silt the shore does; a desert's sea takes wind-blown dust too (the Sahara's falls on the Atlantic).
        float openSilt = silt * (spec.Archetype == Archetype.Arid ? 0.45f : 0.1f);
        (Spectrum, Spectrum) WaterOf(Waters w) => Seas(plankton, tannin, silt, openSilt, leaf, dust, sand, w, solute);
        // Never the sky's own hue either: a sea that takes its sky's colour reads flat.
        Waters waters = Contrast(Makers(WaterOdds(spec), 3, w => Vivid(w)), h, 31, w => WaterOf(w).Item1.Reflectance(), land.Hue, Oklab.Of(sky.Zenith).Hue, Oklab.Of(sky.Horizon).Hue, Oklab.Of(sky.Band).Hue);
        (Spectrum deep, Spectrum shallow) = WaterOf(waters);
        Spectrum snow = Spectrum.Of(l => 0.93f - 0.08f * (l - 400f) / 300f);

        Parts has = PartsOf(spec, life), vivid = has & ((Vivid(gas) >= 0.5f ? Parts.Sky : 0) | (Vivid(ore) >= 0.5f ? Parts.Ground : 0)
            | (Vivid(pigment) >= 0.5f && !silver ? Parts.Plants : 0) | (Vivid(waters) >= 0.5f ? Parts.Water : 0));
        // A wholly vivid world is candy as often as not; otherwise one of its parts lies deep, dark and dull, against the strong rest.
        Parts deepPart = 0;
        if (vivid == has && vivid != 0 && U(52) >= CandyShare)
        {
            var parts = Array.FindAll(new[] { Parts.Sky, Parts.Ground, Parts.Plants, Parts.Water }, x => has.HasFlag(x));
            deepPart = parts[Math.Min((int)(U(53) * parts.Length), parts.Length - 1)];
            vivid &= ~deepPart;
        }
        Rgb D(Rgb c, Parts part) => part == deepPart ? Deepened(c) : c;
        if (deepPart == Parts.Ground) soils = Array.ConvertAll(soils, Deepened);
        return new WorldPalette
        {
            Kind = vivid == 0 ? PaletteKind.Earthy : vivid == has ? PaletteKind.Candy : (vivid | deepPart) == has ? PaletteKind.Vivid : PaletteKind.Mixed,
            Has = has, VividParts = vivid, DeepParts = deepPart, Hand = spec.Airless,
            Kelvin = kelvin, Air = air, Rock = rock, Iron = iron, Life = life, Plankton = plankton,
            Gas = gas, Kit = pigment, Silver = silver, Water = waters, Ore = ore,
            Star = light.Hue(eye), Disc = light.Hue(),
            Zenith = D(sky.Zenith, Parts.Sky), Horizon = D(sky.Horizon, Parts.Sky), Band = D(sky.Band, Parts.Sky), Haze = D(sky.Haze, Parts.Sky), Sunset = sky.Sunset,
            Limb = D(sky.Limb, Parts.Sky), Shade = D(sky.Shade, Parts.Sky),
            CloudTint = hasSky ? Rgb.Lerp(new Rgb(1f, 1f, 1f), Albedo(sky.Albedo).Reflectance(), Math.Clamp(air.Aerosol * 0.35f, 0f, 0.55f)) : new Rgb(1f, 1f, 1f),
            // An airless body keeps proto/vacuum's deep greys (its light and shadow colour it).
            Dust = spec.Airless ? Rgb.Hex(handDust) : D(Part(dust, GroundBoost, 0.85f * Vivid(ore)), Parts.Ground),
            Bedrock = spec.Airless ? Rgb.Hex(handRock) : D(Part(bedrock, RockBoost, Vivid(ore)), Parts.Ground),
            Sand = spec.Airless ? BiomeLook.Ground(Biome.Beach) : D(Part(sand, GroundBoost, Vivid(ore)), Parts.Ground), Snow = snow.Reflectance(),
            Deep = D(Part(deep, DeepBoost, Vivid(waters)), Parts.Water), Shallow = D(Part(shallow, ShallowBoost, Vivid(waters)), Parts.Water),
            Ice = (snow * 0.92f).Reflectance(),
            Leaf = D(leafColor, Parts.Plants), Peak = peak,
            _soils = soils,
        };
    }

    /// <summary>
    /// About one world with air in five keeps today's look: the hand colours the game had before the palette (owner, 23 Nov: "what
    /// we have today would be something we could also see"). Airless bodies never do: theirs is the deep grey moon, never the
    /// old sandy dunes.
    /// </summary>
    public static bool IsToday(PlanetSpec spec) => !spec.Airless && Hash.Unit(Hash.Of(spec.Seed, 0x544F44), 0) < 0.2f;

    /// <summary>
    /// Today's look: every biome's ground from <see cref="BiomeLook.Ground"/> under what grows on it, the globe from
    /// <see cref="BiomeLook.Color"/> and the type's own hand colours (<see cref="ArchetypeLook"/>), the water, dust and rock the
    /// surface had by type. Its sky here is the globe's; the sky model gives the ground its own (SkySystem), and its plants take
    /// their colour from the starlight as they did (FloraClimate.Light).
    /// </summary>
    static WorldPalette TodayLook(PlanetSpec spec, float kelvin, AirMix air)
    {
        ArchetypeLook globe = ArchetypeLook.Base(spec.Archetype, spec.Variant);
        (string shallow, string deep, string dust, string rock) = Hands(spec);
        return new WorldPalette
        {
            Kind = PaletteKind.Today, Hand = true, Kelvin = kelvin, Air = air, Life = LifeOf(spec), Has = PartsOf(spec, LifeOf(spec)),
            Star = globe.SunColor, Disc = globe.SunColor, Zenith = globe.SkyTop, Horizon = globe.SkyHorizon, Band = globe.SkyHorizon,
            Haze = globe.SkyHorizon, Sunset = globe.SkyHorizon, Limb = globe.Atmosphere, CloudTint = globe.CloudColor, Shade = globe.SkyTop,
            Dust = Rgb.Hex(dust), Bedrock = Rgb.Hex(rock), Sand = BiomeLook.Ground(Biome.Beach), Snow = Rgb.Hex("eef2f6"),
            Deep = Rgb.Hex(deep), Shallow = Rgb.Hex(shallow), Ice = globe.FrozenSea, Leaf = BiomeLook.Color(Biome.Grassland),
        };
    }

    /// <summary>
    /// The hand tables by type: the water, dust and rock the surface had before the palette (today's look), and an airless body's
    /// (proto/vacuum's barren greys: dust is regolith).
    /// </summary>
    static (string Shallow, string Deep, string Dust, string Rock) Hands(PlanetSpec spec) => (spec.Archetype, spec.Variant) switch
    {
        (Archetype.Temperate, 0) => ("2e7a80", "0c2c40", "9c7e5a", "726b61"),
        (Archetype.Temperate, _) => ("2f7c6c", "0c3236", "9c7e5a", "726b61"),
        (Archetype.Arid, 0) => ("3a8c8a", "12384a", "b98a56", "a2725a"),
        (Archetype.Arid, _) => ("3a8c8a", "12384a", "b98a56", "9c5436"),
        (Archetype.Ice, 0) => ("2c5a78", "0a1c34", "c8ccd0", "5d646e"),
        (Archetype.Ice, _) => ("2a6a7c", "0a2236", "c0c4c8", "5d646e"),
        (Archetype.Lava, _) => ("8a1a0a", "ff6a18", "4a4442", "2f2a2b"),
        (Archetype.Alien, 0) => ("2a8a86", "0c2a3a", "a898b8", "6f6185"),
        (Archetype.Barren, _) => ("505052", "303032", "8c8b88", "505050"),
        _ => ("5a3a7a", "1a0c30", "8a8a5a", "6b5a52"),
    };

    /// <summary>The parts a world has: ground always; with air, a sky, water but on a lava world, and plants where anything lives.</summary>
    static Parts PartsOf(PlanetSpec spec, float life) => spec.Airless ? Parts.Ground
        : Parts.Sky | Parts.Ground | (life > 0f ? Parts.Plants : 0) | (spec.Archetype == Archetype.Lava ? 0 : Parts.Water);

    /// <summary>
    /// How far the eye adapts to its star's light on lit ground: half way, so the star's cast shows on everything it lights, an
    /// orange (K) dwarf's golden amber most of all (owner, 23 Nov) (<see cref="Spectrum.Adapted"/>).
    /// </summary>
    const float Adaptation = 0.5f;


    /// <summary>The most chroma (Oklab) a sky takes: the paintings' skies, never neon.</summary>
    const float SkyMost = 0.22f;

    /// <summary>
    /// The most chroma (Oklab) an earthy part takes, and an earthy sky's horizon: a natural world reads natural (owner, 23 Nov: "our
    /// desaturated and natural looking worlds"), and a part labelled earthy never looks vivid.
    /// </summary>
    public const float EarthyMost = 0.085f, EarthyHorizon = 0.06f;

    /// <summary>How often a wholly vivid world is candy, every part at full strength; the rest pair their strong parts against a deep one.</summary>
    const float CandyShare = 0.5f;

    /// <summary>How far the sky is seen adapted: under half way, so skies under M, K, G, F and A stars differ plainly.</summary>
    const float SkyAdaptation = 0.45f;

    /// <summary>How bright a sky is against its sunlight: Earth's clear zenith comes out at Earth's (luminance about 0.22 of white).</summary>
    const float SkyGain = 1.4f;

    /// <summary>
    /// The art direction's push for each kind of surface at its most vivid: chroma times this, at least that (Oklab), at its own hue,
    /// lightness too (vivid plants and water brighter than life, deep water most: it would be near black). How far each element
    /// goes is its own chemistry's (<see cref="Vivid(Stain)"/> and the rest): earth tones stay as the light makes them.
    /// </summary>
    static readonly (float Gain, float Floor, float Light) GroundBoost = (2.7f, 0.14f, 1f), RockBoost = (2.3f, 0.1f, 1f), LeafBoost = (3.1f, 0.15f, 1.3f), ShallowBoost = (2.8f, 0.14f, 1.25f), DeepBoost = (2.8f, 0.13f, 1.6f);

    static Rgb Bold(Spectrum s, (float Gain, float Floor, float Light) boost, float vivid)
    {
        Rgb c = s.Reflectance();
        float gain = 1f + (boost.Gain - 1f) * vivid, light = 1f + (boost.Light - 1f) * vivid;
        // Where the screen can't hold that chroma so light, the lightness gives way toward the middle, never the colour; a dark
        // colour stays dark (dim light's leaves, broad dark pigments: never lightened).
        Rgb bold = Oklab.Vivid(c, gain, boost.Floor, light);
        for (int i = 0; i < 4 && Oklab.Of(bold).Chroma < boost.Floor * 0.95f && Oklab.Of(bold).L > 0.6f; i++)
            bold = Oklab.Vivid(c, gain, boost.Floor, (light = 0.6f / MathF.Max(Oklab.Of(c).L, 0.05f) * 0.35f + light * 0.65f));
        return bold;
    }

    /// <summary>A part as its chemistry makes it: a vivid maker's pushed at its own hue (<see cref="Bold"/>), an earthy one's as the light makes it, held natural.</summary>
    static Rgb Part(Spectrum s, (float Gain, float Floor, float Light) boost, float vivid) => vivid >= 0.5f ? Bold(s, boost, vivid) : Held(s.Reflectance(), EarthyMost);

    /// <summary>
    /// A colour held to at most <paramref name="most"/> chroma (Oklab) at its own hue, or <see cref="EarthTone"/> where its hue is an
    /// earth tone's (rust, burnt orange, umber, ochre, mustard: Oklab hue 20° to 100°), which keep their strength (owner's bases, 23 Nov).
    /// </summary>
    static Rgb Held(Rgb c, float most)
    {
        Oklab o = Oklab.Of(c);
        float deg = (o.Hue * 180f / MathF.PI + 360f) % 360f;
        return Oklab.Vivid(c, 1f, most: deg is >= 20f and <= 100f ? MathF.Max(most, EarthTone) : most);
    }

    /// <summary>The most chroma an earth tone takes, earthy as it is: a burnt orange dust sky, rust ground, mustard light.</summary>
    public const float EarthTone = 0.14f;

    /// <summary>A part laid deep against a vivid world's strong ones: darker and duller at its own hue (a dark vivid colour keeps its chroma; this doesn't).</summary>
    static Rgb Deepened(Rgb c)
    {
        Oklab o = Oklab.Of(c);
        return Oklab.Lch(o.L * 0.62f, MathF.Min(o.Chroma * 0.45f, 0.055f), o.Hue).ToRgb();
    }

    // How vivid each element's chemistry makes it, 0 an earth tone to 1 a colour as strong as it comes (owner, 23 Nov: "a mix of
    // vibrancies and earth tones available based on elements"). Iron oxides, clays, basalt, carbon and dust give earth tones; sulfur,
    // copper and cobalt minerals, chlorine and iodine in the air, vivid pigments in life and the chemistry of water give the vivid.
    static float Vivid(Trace gas) => gas switch
    {
        Trace.Chlorine or Trace.Iodine or Trace.Bromine => 0.9f, Trace.Sulfur or Trace.Methane => 0.8f, Trace.NitrogenDioxide or Trace.Ozone => 0.3f, _ => 0.1f, // ozone's deep blue and NO2's amber haze are natural
    };

    static float Vivid(Stain ore) => ore switch
    {
        Stain.Sulfur or Stain.Azurite => 0.9f, Stain.Malachite => 0.85f, Stain.Cobalt => 0.8f, Stain.Glauconite => 0.4f, Stain.Manganese => 0.35f, Stain.Carbon => 0.05f, _ => 0.1f,
    };

    static float Vivid(Pigment kit) => kit switch
    {
        Pigment.Retinal => 0.9f, Pigment.Phycocyanin or Pigment.Phycoerythrin => 0.85f, Pigment.Anthocyanin or Pigment.Carotenoid => 0.8f, Pigment.Chlorophyll => 0.3f, _ => 0.05f,
    };

    static float Vivid(Waters w) => w switch
    {
        Waters.Halophiles => 0.95f, Waters.Copper or Waters.PurpleSulfur => 0.9f, Waters.Cyanobacteria => 0.85f, Waters.Sulfur => 0.8f, Waters.Flour => 0.35f, Waters.Iron => 0.3f, Waters.Clear => 0.2f, _ => 0.1f, // rock flour and rust water are Earth's own
    };

    /// <summary>
    /// A graded sky, never one flat tint: a colour turned round the hue circle, the way it already leans, until it stands at least
    /// <paramref name="gap"/> (radians) from <paramref name="from"/>. The art direction's, where the air alone would leave the band
    /// along the horizon the zenith's own hue.
    /// </summary>
    static Rgb Apart(Rgb c, float from, float gap)
    {
        Oklab o = Oklab.Of(c);
        float d = MathF.IEEERemainder(o.Hue - from, MathF.Tau);
        if (MathF.Abs(d) >= gap) return c;
        return Oklab.Lch(o.L, o.Chroma, from + (d < 0f ? -gap : gap)).ToRgb();
    }

    /// <summary>Of a kind's odds, its vivid makers or its earthy ones (all of them, if it has none of that sort).</summary>
    static (T, float)[] Sort<T>((T, float)[] odds, bool vivid, Func<T, float> vividness)
    {
        var kept = Array.FindAll(odds, o => vividness(o.Item1) >= 0.5f == vivid);
        return kept.Length > 0 ? kept : odds;
    }

    /// <summary>One of a few things, by its odds.</summary>
    static T Roll<T>((T, float)[] odds, float u)
    {
        float total = 0f;
        foreach (var (_, p) in odds) total += p;
        u *= total;
        foreach (var (m, p) in odds)
            if ((u -= p) < 0f) return m;
        return odds[^1].Item1;
    }


    /// <summary>
    /// Of four rolled by their odds, the one whose colour stands furthest in hue from <paramref name="away"/> (and each of <paramref name="also"/>), a near-grey
    /// counting for less: the basis allows each, and the look takes the one that contrasts most.
    /// </summary>
    static T Contrast<T>((T, float)[] odds, ulong h, int salt, Func<T, Rgb> color, float away, params float[] also)
    {
        T best = odds[0].Item1;
        float score = -1f;
        for (int i = 0; i < 4; i++)
        {
            T c = Roll(odds, Hash.Unit(h, salt + 40 * i));
            Oklab o = Oklab.Of(color(c));
            float s = Oklab.HueGap(o.Hue, away);
            foreach (float a in also) s = MathF.Min(s, Oklab.HueGap(o.Hue, a));
            s *= MathF.Min(1f, o.Chroma / 0.03f);
            if (s > score) (best, score) = (c, s);
        }
        return best;
    }

    /// <summary>
    /// The trace gas a kind of air may carry. Oxygen air: a thick ozone layer, or traces of methane, chlorine, iodine, nitrogen
    /// dioxide or sulfur from its life and its volcanoes; dusty air the same without ozone; volcanic air sulfur, chlorine and
    /// nitrogen dioxide; air the model doesn't otherwise hold any of them.
    /// </summary>
    static (Trace, float)[] TraceOdds(AirMix air) => air.Oxygen
        ? new[] { (Trace.None, 0.25f), (Trace.Ozone, 0.18f), (Trace.Methane, 0.16f), (Trace.Iodine, 0.08f), (Trace.Chlorine, 0.08f), (Trace.NitrogenDioxide, 0.1f), (Trace.Sulfur, 0.07f), (Trace.Bromine, 0.08f) }
        : air.Particles switch
        {
            Particles.Dust => new[] { (Trace.None, 0.32f), (Trace.Methane, 0.16f), (Trace.Iodine, 0.08f), (Trace.Chlorine, 0.08f), (Trace.NitrogenDioxide, 0.15f), (Trace.Sulfur, 0.08f), (Trace.Bromine, 0.1f), (Trace.Ozone, 0.06f) },
            Particles.Ash => new[] { (Trace.Sulfur, 0.2f), (Trace.Chlorine, 0.12f), (Trace.NitrogenDioxide, 0.15f), (Trace.Iodine, 0.1f), (Trace.Methane, 0.12f), (Trace.Bromine, 0.16f), (Trace.None, 0.15f) },
            Particles.Sulfate => new[] { (Trace.Sulfur, 0.22f), (Trace.Chlorine, 0.15f), (Trace.Iodine, 0.1f), (Trace.Methane, 0.15f), (Trace.Bromine, 0.18f), (Trace.None, 0.2f) },
            _ => new[] { (Trace.None, 0.16f), (Trace.Ozone, 0.16f), (Trace.Methane, 0.16f), (Trace.Chlorine, 0.1f), (Trace.Iodine, 0.1f), (Trace.NitrogenDioxide, 0.12f), (Trace.Sulfur, 0.08f), (Trace.Bromine, 0.12f) },
        };

    /// <summary>
    /// A trace gas's absorption by wavelength (nm), 1 at its strongest in the visible: ozone's Chappuis band near 600 nm (a thick
    /// layer turns the sky violet-blue); chlorine's band from the ultraviolet across the violet and blue (a yellow-green gas: blue
    /// air turns green); iodine's band in the green near 525 nm (a violet gas: magenta skies); nitrogen dioxide's across the blue
    /// (amber); methane's red bands from 619 nm on (Uranus' and Neptune's cyan); sulfur's allotropes drinking the violet (gold);
    /// bromine's broad band from the violet into the green (a red-brown gas: orange-red skies).
    /// </summary>
    static float Absorbs(Trace gas, float l) => gas switch
    {
        Trace.Ozone => G(l, 600f, 50f),
        Trace.Chlorine => G(l, 380f, 55f),
        Trace.Iodine => G(l, 525f, 38f),
        Trace.NitrogenDioxide => Below(l, 470f, 30f),
        Trace.Methane => 0.9f * Mathx.Smooth(560f, 700f, l) + 0.5f * G(l, 619f, 10f),
        Trace.Sulfur => Below(l, 485f, 18f),
        Trace.Bromine => Below(l, 545f, 25f),
        _ => 0f,
    };

    /// <summary>A soft absorption band, 1 at <paramref name="c"/> nm, <paramref name="w"/> nm wide.</summary>
    static float G(float l, float c, float w) => MathF.Exp(-0.5f * (l - c) * (l - c) / (w * w));

    /// <summary>A soft absorption edge, 1 below <paramref name="edge"/> nm.</summary>
    static float Below(float l, float edge, float w) => 1f / (1f + MathF.Exp((l - edge) / w));

    /// <summary>
    /// The pigment kits a starlight favours (Kiang et al. 2007: pigments absorb where the light is richest). Every kit is possible
    /// anywhere; under a cool red star the orange-red-drinking phycobilins (blue-green plants) and broad dark pigments (black ones)
    /// do best, under a hot white star the blue-drinking carotenoids (gold and orange) and the screening anthocyanins (magenta and
    /// purple); chlorophyll's green antenna is a sunlike star's.
    /// </summary>
    static (Pigment, float)[] PigmentOdds(float kelvin)
    {
        float hot = Mathx.Smooth(5800f, 8500f, kelvin), cool = Mathx.Smooth(5800f, 3600f, kelvin), sunny = 1f - hot - cool;
        return new[]
        {
            (Pigment.Chlorophyll, 0.25f + 0.5f * sunny), (Pigment.Phycocyanin, 0.15f + 0.35f * cool), (Pigment.Phycoerythrin, 0.12f + 0.1f * sunny),
            (Pigment.Anthocyanin, 0.1f + 0.35f * hot), (Pigment.Carotenoid, 0.4f + 0.6f * hot), (Pigment.Retinal, 0.2f), (Pigment.Melanin, 0.05f + 0.6f * cool * cool),
        };
    }

    /// <summary>
    /// What may colour a kind of world's water: salt pans' halophile blooms (pink to red), acid iron (orange-red), copper (turquoise),
    /// glacial rock flour (milky cyan), purple sulfur bacteria (violet), cyanobacteria (lime), tannins (black tea), sulfur (milky gold-green).
    /// </summary>
    static (Waters, float)[] WaterOdds(PlanetSpec spec) => spec.Archetype switch
    {
        Archetype.Arid => new[] { (Waters.Halophiles, 0.3f), (Waters.Iron, 0.2f), (Waters.Copper, 0.15f), (Waters.Sulfur, 0.1f), (Waters.PurpleSulfur, 0.1f), (Waters.Clear, 0.15f) },
        Archetype.Ice => new[] { (Waters.Flour, 0.35f), (Waters.Clear, 0.25f), (Waters.Copper, 0.15f), (Waters.PurpleSulfur, 0.1f), (Waters.Halophiles, 0.15f) },
        Archetype.Lava => new[] { (Waters.Sulfur, 0.4f), (Waters.Iron, 0.35f), (Waters.Copper, 0.25f) },
        Archetype.Alien => new[] { (Waters.Clear, 0.08f), (Waters.Halophiles, 0.08f), (Waters.Iron, 0.12f), (Waters.Copper, 0.13f), (Waters.Flour, 0.12f), (Waters.PurpleSulfur, 0.09f), (Waters.Cyanobacteria, 0.11f), (Waters.Blackwater, 0.12f), (Waters.Sulfur, 0.12f) },
        _ => new[] { (Waters.Clear, 0.22f), (Waters.Cyanobacteria, 0.12f), (Waters.PurpleSulfur, 0.08f), (Waters.Halophiles, 0.06f), (Waters.Copper, 0.12f), (Waters.Flour, 0.08f), (Waters.Blackwater, 0.1f), (Waters.Iron, 0.1f), (Waters.Sulfur, 0.1f) },
    };

    /// <summary>The ores a world's ground may weather into its soil: volcanic worlds' sulfur most of all, an icy world's ground plain as often as any one ore.</summary>
    static (Stain, float)[] StainOdds(PlanetSpec spec) => spec.Archetype switch
    {
        Archetype.Lava => new[] { (Stain.Sulfur, 0.35f), (Stain.None, 0.15f), (Stain.Manganese, 0.15f), (Stain.Malachite, 0.1f), (Stain.Cobalt, 0.1f), (Stain.Carbon, 0.15f) },
        Archetype.Ice => new[] { (Stain.None, 0.25f), (Stain.Azurite, 0.15f), (Stain.Manganese, 0.2f), (Stain.Glauconite, 0.2f), (Stain.Cobalt, 0.1f), (Stain.Carbon, 0.1f) },
        _ => new[] { (Stain.None, 0.12f), (Stain.Sulfur, 0.12f), (Stain.Malachite, 0.14f), (Stain.Azurite, 0.13f), (Stain.Manganese, 0.12f), (Stain.Cobalt, 0.09f), (Stain.Glauconite, 0.14f), (Stain.Carbon, 0.12f) },
    };

    /// <summary>
    /// Ground carrying an ore beside its iron: native sulfur and jarosite yellow; malachite green and azurite blue (copper
    /// carbonates); manganese oxides violet-black; erythrite (cobalt bloom) pink; glauconite green (the "green sands"); carbon
    /// (organic-rich shale and coal measures, graphite) umber to charcoal.
    /// </summary>
    static Spectrum Stained(Spectrum r, Stain ore, float amount) => ore == Stain.None || amount <= 0f ? r : r * (Spectrum.Flat(1f) - Spectrum.Of(l => ore switch
    {
        Stain.Sulfur => 0.85f * Below(l, 500f, 14f),
        Stain.Malachite => 0.75f * G(l, 660f, 60f) + 0.5f * Below(l, 450f, 20f),
        Stain.Azurite => 0.85f * Mathx.Smooth(480f, 580f, l),
        Stain.Manganese => 0.2f * G(l, 560f, 55f) + 0.5f,
        Stain.Cobalt => 0.7f * G(l, 530f, 35f),
        Stain.Carbon => 0.75f - 0.1f * (l - 400f) / 300f,
        _ => 0.55f * G(l, 640f, 70f) + 0.45f * Below(l, 470f, 25f),
    }) * amount);

    /// <summary>
    /// Weathered with water and oxygen, iron rusts: hematite reds hot, dry ground, goethite browns cool, wet ground
    /// (Schwertmann): each absorbs below its edge, hematite's near 565 nm, goethite's near 505 nm.
    /// </summary>
    static Spectrum Rust(Spectrum r, float hematite, float goethite) =>
        r * (Spectrum.Flat(1f) - Spectrum.Below(565f, 12f) * (0.78f * hematite) - Spectrum.Below(505f, 18f) * (0.6f * goethite));

    /// <summary>A world's rock by its type: lava worlds are basalt, airless bodies mare basalt or bright highland (or ice), the rest any of the common crustal rocks.</summary>
    static RockType RockOf(PlanetSpec spec, float u) => Roll(spec.Airless
        ? spec.Archetype == Archetype.Ice ? new[] { (RockType.Ice, 0.6f), (RockType.Basalt, 0.2f), (RockType.Anorthosite, 0.2f) }
        : new[] { (RockType.Basalt, 0.5f), (RockType.Anorthosite, 0.5f) } // the more of the two in its mix: Make blends them
        : spec.Archetype switch
        {
            Archetype.Lava => new[] { (RockType.Basalt, 0.45f), (RockType.Andesite, 0.3f), (RockType.Granite, 0.25f) }, // granite's volcanic twin, rhyolite: pale and pink
            Archetype.Arid => new[] { (RockType.Sandstone, 0.3f), (RockType.Granite, 0.2f), (RockType.Basalt, 0.25f), (RockType.Limestone, 0.25f) }, // red beds, grus, black sand, white gypsum and lime
            Archetype.Ice => new[] { (RockType.Granite, 0.35f), (RockType.Basalt, 0.35f), (RockType.Shale, 0.3f) },
            _ => new[] { (RockType.Granite, 0.25f), (RockType.Basalt, 0.2f), (RockType.Sandstone, 0.2f), (RockType.Limestone, 0.15f), (RockType.Shale, 0.2f) },
        }, u);

    /// <summary>
    /// Fresh rock's reflectance, 400 to 700 nm (from USGS spectral library shapes, simplified): granite pale with
    /// feldspar's pink, basalt dark, quartz sandstone bright before its iron coat, limestone near white, shale
    /// grey, andesite (between basalt and rhyolite) mid-grey, lunar-highland anorthosite bright, ice near white with a little red absorbed.
    /// </summary>
    static Spectrum Stone(RockType m) => m switch
    {
        RockType.Granite => Spectrum.Of(l => 0.26f + 0.14f * (l - 400f) / 300f),
        RockType.Basalt => Spectrum.Of(l => 0.065f + 0.025f * (l - 400f) / 300f),
        RockType.Andesite => Spectrum.Of(l => 0.15f + 0.05f * (l - 400f) / 300f),
        RockType.Sandstone => Spectrum.Of(l => 0.34f + 0.16f * (l - 400f) / 300f),
        RockType.Limestone => Spectrum.Of(l => 0.5f + 0.1f * (l - 400f) / 300f),
        RockType.Shale => Spectrum.Of(l => 0.11f + 0.05f * (l - 400f) / 300f),
        RockType.Anorthosite => Spectrum.Of(l => 0.3f + 0.1f * (l - 400f) / 300f),
        _ => Spectrum.Of(l => 0.88f - 0.12f * (l - 400f) / 300f),
    };

    /// <summary>
    /// A soil from the rock's fines in a climate (warmth and wetness 0..1). Iron rusts red where it is hot and
    /// not too wet, yellow-brown where cool and damp (soil redness falls as it gets colder and wetter); humus
    /// darkens cool, damp, living ground most (prairie black earth) and hot, wet ground less (it rots fast);
    /// dry ground keeps pale salts and carbonates at the surface.
    /// </summary>
    static Spectrum Soil(Spectrum fines, float warm, float wet, float iron, float life)
    {
        float red = Math.Clamp(warm * 1.5f - wet * 0.6f, 0f, 1f);
        Spectrum s = Rust(fines, iron * (0.25f + 0.75f * red), iron * (1f - red) * 0.8f);
        float humus = life * Mathx.Smooth(0.15f, 0.7f, wet) * (1f - 0.55f * warm) * 0.75f;
        s = Spectrum.Lerp(s, Spectrum.Of(l => 0.035f + 0.04f * (l - 400f) / 300f), humus);
        float crust = Mathx.Smooth(0.3f, 0.05f, wet) * 0.25f;
        return Spectrum.Lerp(s, Spectrum.Of(l => 0.62f + 0.06f * (l - 400f) / 300f), crust);
    }

    /// <summary>A particle's single-scattering albedo from the mineral it is made of: the colour it absorbs, it doesn't scatter.</summary>
    static Spectrum Albedo(Spectrum mineral)
    {
        float top = 0f;
        for (int i = 0; i < Spectrum.Bands; i++) top = MathF.Max(top, mineral[i]);
        return mineral.Map(v => 0.4f + 0.57f * v / MathF.Max(top, 1e-4f)); // Mars dust: about 0.65 in the blue, 0.97 in the red
    }

    /// <summary>
    /// Leaves under a light. The reaction centre absorbs where the light reaching the ground has most photons (Kiang et al. 2007):
    /// in the red under a sunlike star, toward the blue under a hot one, out past the visible under a red dwarf, where photons
    /// are scarce in the visible and leaves take all they can and go dark; under a hot star light is plenty and they go pale.
    /// Each world's centre sits a little either side of the peak (Earth's chlorophylls a, b, d and f span 640 to 720 nm), with a
    /// porphyrin's blue band, and its <paramref name="kit"/> beside it: chlorophyll b's antenna 50 nm to the blue (green
    /// leaves), cyanobacteria's phycocyanin drinking the orange-red (blue-green), red algae's phycoerythrin the green (red),
    /// anthocyanins screening the green (magenta), carotenoids screening the blue (gold, orange), a retinal pigment like
    /// haloarchaea's bacteriorhodopsin drinking the yellow-green (purple: DasSarma and Schwieterman 2021), or a broad dark one
    /// (black). Some worlds' plants grow a wax bloom or fine hairs that throw back every colour (silver).
    /// </summary>
    static Spectrum Leaves(float kelvin, Func<float, float> through, Func<int, float> u, Pigment kit, bool silver, out float peak)
    {
        // Photons at the ground (Planck's law over the photon's energy, through the air), 400 to 1100 nm, and the share past the visible.
        float Photons(float k, float l) => MathF.Pow(l / 550f, -4f) / (MathF.Exp(1.4388e7f / (l * k)) - 1f);
        float most = 0f, vis = 0f, nir = 0f, sunVis = 0f, sunNir = 0f;
        peak = 400f;
        for (float l = 400f; l < 1100f; l += 5f)
        {
            float n = Photons(kelvin, l) * through(l), s = Photons(5800f, l);
            if (n > most) (most, peak) = (n, l);
            if (l < 700f) { vis += n; sunVis += s; } else { nir += n; sunNir += s; }
        }
        float scarce = nir / (vis + nir) - sunNir / (sunVis + sunNir);
        peak += (u(0) - 0.5f) * 50f;
        float body = Math.Clamp(1.2f + 5f * scarce + (u(1) - 0.5f) * 0.6f, 0.6f, 2.8f);
        Spectrum a = Spectrum.Flat(body) + Spectrum.Band(peak, 30f) * 1.4f + kit switch
        {
            Pigment.Phycocyanin => Spectrum.Band(615f, 28f) * 2.2f + Spectrum.Band(570f, 25f) + Spectrum.Band(450f, 45f) * 0.4f,
            Pigment.Phycoerythrin => Spectrum.Band(555f, 25f) * 2.4f + Spectrum.Band(500f, 20f) * 0.8f + Spectrum.Band(450f, 45f) * 0.6f,
            Pigment.Anthocyanin => Spectrum.Band(535f, 35f) * 2.2f + Spectrum.Band(450f, 45f) * 0.8f,
            Pigment.Carotenoid => Spectrum.Band(470f, 30f) * 2.5f + Spectrum.Band(500f, 18f) + Spectrum.Band(440f, 30f),
            Pigment.Retinal => Spectrum.Band(568f, 38f) * 2.4f + Spectrum.Band(450f, 45f) * 0.5f,
            Pigment.Melanin => Spectrum.Flat(2f) + Spectrum.Below(600f, 60f),
            _ => Spectrum.Band(peak - 50f, 35f) + Spectrum.Band(450f, 45f) * 1.3f,
        };
        Spectrum leaf = Spectrum.Exp(a * -1f) * (0.45f + 0.1f * u(5));
        return silver ? Spectrum.Lerp(leaf, Spectrum.Flat(0.6f), 0.4f) : leaf;
    }

    /// <summary>
    /// Water's colour from above, deep and over four metres to sand: irradiance reflectance about 0.33 b_b/(a + b_b)
    /// (Morel and Prieur 1977). Water itself absorbs red (Pope and Fry 1997; Smith and Baker 1981) and scatters blue
    /// (Morel 1974): blue. Plankton absorb as the land's leaves do (the same chemistry), dissolved matter absorbs the
    /// blue (Bricaud et al. 1981), and silt scatters the colour of the land's dust. Its chemistry (<paramref name="kind"/>,
    /// <paramref name="solute"/> as strong) adds its own: halophile carotenoids drinking the blue-green (Lake Hillier,
    /// Lake Retba: Oren 2009), dissolved ferric iron the blue (Rio Tinto), cupric ions the red (turquoise), glacial rock
    /// flour scattering white over water's own red absorption (Lake Louise's milky cyan), purple sulfur bacteria's okenone
    /// the green, a cyanobacterial bloom's phycocyanin and chlorophyll the orange and blue (lime), tannins the blue to green
    /// (blackwater: the Rio Negro), colloidal sulfur scattering pale over its blue absorption.
    /// </summary>
    static (Spectrum Deep, Spectrum Shallow) Seas(float plankton, float tannin, float silt, float openSilt, Spectrum leaf, Spectrum dust, Spectrum sand, Waters kind, float solute)
    {
        Spectrum water = Spectrum.Of(PureWater);
        Spectrum pigment = leaf.Map(r => -MathF.Log(MathF.Max(r, 1e-3f)));
        float top = 0f;
        for (int i = 0; i < Spectrum.Bands; i++) top = MathF.Max(top, pigment[i]);
        Spectrum grains = Albedo(dust);
        float s = solute;
        if (kind == Waters.Blackwater) tannin += 1.5f * s;
        // What the chemistry absorbs (m⁻¹) and scatters back.
        float Absorb(float l) => kind switch
        {
            Waters.Halophiles => s * 0.8f * (G(l, 470f, 25f) + 1.1f * G(l, 500f, 22f) + 0.8f * G(l, 535f, 20f)),
            Waters.Iron => s * 1.5f * Below(l, 570f, 22f),
            Waters.Copper => s * 0.6f * Mathx.Smooth(540f, 680f, l),
            Waters.PurpleSulfur => s * 0.9f * (1.2f * G(l, 520f, 28f) + 0.6f * G(l, 480f, 20f) + 0.3f * G(l, 590f, 15f)),
            Waters.Cyanobacteria => s * 0.8f * (G(l, 440f, 25f) + 1.3f * G(l, 620f, 20f) + G(l, 675f, 15f)),
            Waters.Sulfur => s * 0.8f * Below(l, 480f, 20f),
            _ => 0f,
        };
        float Back(float l) => kind switch
        {
            Waters.Halophiles or Waters.PurpleSulfur => s * 0.004f,
            Waters.Iron => s * 0.006f * (1f - Below(l, 560f, 25f)),
            Waters.Copper => s * 0.003f,
            Waters.Flour => s * 0.03f * 550f / l,
            Waters.Cyanobacteria => s * 0.008f * 550f / l,
            Waters.Sulfur => s * 0.02f,
            _ => 0f,
        };
        // The light coming up from under the surface (the water's glint off the surface is the renderer's).
        (Spectrum r, Spectrum k) Body(float mud)
        {
            Spectrum a = water + pigment * (0.06f * MathF.Pow(plankton, 0.65f) / MathF.Max(top, 1e-3f))
                         + Spectrum.Of(l => tannin * MathF.Exp(-0.014f * (l - 440f)) + Absorb(l))
                         + grains.Map(w => (1f - w) * 0.04f * mud);
            Spectrum bb = Spectrum.Of(l => 0.5f * 0.00222f * MathF.Pow(500f / l, 4.32f) + 0.003f * MathF.Pow(plankton, 0.62f) * 550f / l + Back(l))
                          + grains * (0.006f * mud);
            return (bb / (a + bb) * 0.33f, a + bb);
        }
        Spectrum deep = Body(openSilt).r;
        (Spectrum near, Spectrum kd) = Body(silt);
        Spectrum shallow = near + (sand - near) * Spectrum.Exp(kd * (-2f * 4f * 1.2f));
        return (deep, shallow);
    }

    /// <summary>Pure water's absorption, m⁻¹, by wavelength (Pope and Fry 1997 to 580 nm, Smith and Baker 1981 beyond).</summary>
    static float PureWater(float l)
    {
        ReadOnlySpan<float> a = stackalloc float[] { 0.0066f, 0.0045f, 0.0064f, 0.0098f, 0.0150f, 0.0257f, 0.0477f, 0.0638f, 0.0708f, 0.0896f, 0.2224f, 0.2755f, 0.3112f, 0.4f, 0.465f, 0.624f };
        int i = Math.Clamp((int)MathF.Round((l - 400f) / 20f), 0, a.Length - 1);
        return a[i];
    }

    /// <summary>
    /// The land's colour for a biome in a climate, as it reads from a distance: its ground under what grows on it
    /// (<see cref="Cover"/>), <paramref name="growth"/>'s colour or, given none, the world's leaf pigment; bare
    /// ground where nothing lives.
    /// </summary>
    public Rgb Land(Biome biome, float warm, float wet, Rgb? growth = null)
    {
        if (Hand) return growth is Rgb t ? Rgb.Lerp(BiomeLook.Ground(biome), t, Cover(biome, true)) : BiomeLook.Color(biome);
        Rgb ground = Ground(biome, warm, wet);
        Rgb? green = growth ?? (Life > 0f ? Leaf : null);
        return green is Rgb g ? Rgb.Lerp(ground, g, Cover(biome)) : ground;
    }

    /// <summary>How much of a biome's ground is hidden by what grows on it: a meadow's grass all but hides its soil (less in today's look).</summary>
    public static float Cover(Biome b, bool today = false) => b switch
    {
        Biome.Grassland or Biome.Meadow => today ? 0.55f : 0.75f,
        Biome.Jungle or Biome.Riverbank or Biome.Oasis => today ? 0.5f : 0.65f,
        Biome.Forest or Biome.Rainforest => today ? 0.5f : 0.55f,
        Biome.PineForest or Biome.CloudForest => today ? 0.45f : 0.5f,
        Biome.Swamp or Biome.SporeMarsh or Biome.GlowMoss => today ? 0.35f : 0.45f,
        Biome.Tundra or Biome.Scrub or Biome.HotSpring => today ? 0.22f : 0.3f,
        _ => 0f,
    };

    /// <summary>Ice two fifths of the way to its meltwater's lightness (the shallows), in that water's hue and a little more chroma: ice worlds differ in their land as in their seas.</summary>
    Rgb MeltTint(Rgb ice)
    {
        Oklab i = Oklab.Of(ice), w = Oklab.Of(Shallow);
        return Oklab.Lch(i.L + (w.L - i.L) * 0.4f, MathF.Max(i.Chroma, MathF.Min(w.Chroma * 1.3f, 0.16f)), w.Hue).Fit().ToRgb();
    }

    /// <summary>
    /// The ground's own colour for a biome in a climate (warmth and wetness 0..1, a map cell's): the soil its climate
    /// weathers from the world's rock, or where the biome is a mineral in itself, that mineral (sand, snow, salt,
    /// basalt). Grounds whose chemistry this model doesn't hold keep their own (<see cref="BiomeLook.Ground"/>).
    /// </summary>
    public Rgb Ground(Biome biome, float warm, float wet)
    {
        if (Hand) return BiomeLook.Ground(biome);
        switch (biome)
        {
            case Biome.Beach: case Biome.Dunes: case Biome.Erg: return biome == Biome.Beach ? Sand : Rgb.Lerp(Sand, Dust, 0.35f);
            case Biome.Cliff: return Bedrock;
            case Biome.Snow: case Biome.Snowfield: case Biome.Glacier: return Snow;
            case Biome.SaltFlat: return Rgb.Lerp(Snow, Dust, 0.12f);
            case Biome.RedRock: case Biome.Badlands: case Biome.MesaTop: return Rgb.Lerp(Bedrock, Dust, biome == Biome.MesaTop ? 0.4f : 0.65f);
            // Volcanic ground is the world's basalt: fresh and black, or reddened scoria where its iron has rusted.
            case Biome.Basalt: return Bedrock;
            case Biome.Ash: return Rgb.Lerp(Sand, Dust, 0.3f);
            case Biome.LavaField: return Bedrock * 0.7f;
            case Biome.Obsidian: case Biome.Sulfur:
            // Ice and hot springs take their own meltwater's tint (the shallows), sinter the world's dust, so ice worlds differ in the land as in the sea.
            case Biome.BlueIce: return MeltTint(BiomeLook.Ground(biome));
            case Biome.HotSpring: return MeltTint(BiomeLook.Ground(biome));
            case Biome.Sinter: return Rgb.Lerp(BiomeLook.Ground(biome), Dust, 0.7f); // its crust is the world's own minerals
            case Biome.CrystalPlain: case Biome.CrystalSpires: case Biome.Chalk: case Biome.GlowMoss:
            case Biome.FungalForest: case Biome.SporeMarsh: case Biome.Mycelium:
                return BiomeLook.Ground(biome);
        }
        // The climate's soils, blended between the grid's corners.
        float ft = Math.Clamp(warm, 0f, 1f) * (Grid - 1), fw = Math.Clamp(wet, 0f, 1f) * (Grid - 1);
        int t0 = Math.Min((int)ft, Grid - 2), w0 = Math.Min((int)fw, Grid - 2);
        float tt = ft - t0, tw = fw - w0;
        Rgb a = Rgb.Lerp(_soils[t0 * Grid + w0], _soils[t0 * Grid + w0 + 1], tw), b = Rgb.Lerp(_soils[(t0 + 1) * Grid + w0], _soils[(t0 + 1) * Grid + w0 + 1], tw);
        return Rgb.Lerp(a, b, tt);
    }
}
