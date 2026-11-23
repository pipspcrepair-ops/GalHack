using System;
using Planets.Gen;

namespace Planets.Look;

/// <summary>What a world's rock is, under its soil and in its sand: the mineral every ground colour starts from.</summary>
public enum Mineral { Granite, Basalt, Andesite, Sandstone, Limestone, Shale, Anorthosite, Ice }

/// <summary>What hangs in a world's air besides its gas: sea salt and sulfate (clean), water haze, the ground's own dust, volcanic ash, sulfuric acid with sulfur.</summary>
public enum Particles { Clean, Haze, Dust, Ash, Sulfate }

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
/// Sources are cited in prototypes/planet-generator/README.md (Palette).
/// </summary>
public sealed record WorldPalette
{
    // ---- What the world is made of (rolled, so it can be printed and tested) ----
    public float Kelvin { get; init; }
    public AirMix Air { get; init; }
    public Mineral Rock { get; init; }
    /// <summary>How much iron the rock carries, 0..1: what its soils can rust into.</summary>
    public float Iron { get; init; }
    /// <summary>Life, 0..1: none on a dead world, 1 on a lush one.</summary>
    public float Life { get; init; }
    /// <summary>Phytoplankton in the water, mg of pigment a m³ (0.05 a clear open sea, several in a green one).</summary>
    public float Plankton { get; init; }

    // ---- Light and sky ----
    /// <summary>The starlight's colour at its brightest channel 1, as the adapted eye sees it on the ground: a faint warmth under a red dwarf, a faint chill under a hot star.</summary>
    public Rgb Star { get; init; }
    /// <summary>The star's disc as it is: a small bright source no eye adapts to, so a red dwarf's disc stays red-orange.</summary>
    public Rgb Disc { get; init; }
    /// <summary>Clear midday sky straight up: starlight scattered by the gas, more for short waves (Rayleigh), and by the particles.</summary>
    public Rgb Zenith { get; init; }
    /// <summary>Clear sky at the horizon: so long a path that every wave is scattered almost fully, paler than the zenith.</summary>
    public Rgb Horizon { get; init; }
    /// <summary>What distance fades to (fog's colour): the horizon's light where the path never ends.</summary>
    public Rgb Haze { get; init; }
    /// <summary>The sun's own light through 25 airmasses: the short waves scattered out, particles deepening it.</summary>
    public Rgb Sunset { get; init; }
    /// <summary>The air's rim seen from space: starlight scattered once, unsaturated, the gas's own colour at its strongest.</summary>
    public Rgb Limb { get; init; }
    /// <summary>What clouds are made of: water and ice white, mixed with the air's dust, ash or sulfur.</summary>
    public Rgb CloudTint { get; init; }
    /// <summary>The ground's fines lifted into the air: what dust storms and devils are.</summary>
    public Rgb Dust { get; init; }

    // ---- Ground ----
    /// <summary>The bare rock: its mineral, stained by what iron its weather lets rust.</summary>
    public Rgb Bedrock { get; init; }
    /// <summary>Loose grains of the rock (beaches, dunes): black on a basalt world, white on a limestone one.</summary>
    public Rgb Sand { get; init; }
    public Rgb Snow { get; init; }

    // ---- Water ----
    /// <summary>Deep water seen from above: what comes back up after the water, its plankton, its dissolved matter and its silt absorb.</summary>
    public Rgb Deep { get; init; }
    /// <summary>Four metres of the same water over the world's own sand.</summary>
    public Rgb Shallow { get; init; }
    /// <summary>Sea ice under a skin of snow.</summary>
    public Rgb Ice { get; init; }

    // ---- Life ----
    /// <summary>The leaf pigment its plants evolved under this light: green under a sunlike star, gold under a hot one, near black under a red dwarf.</summary>
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
        ulong h = Hash.Of(spec.Seed, 0x47454F);
        float U(int k) => Hash.Unit(h, k);
        Mineral rock = RockOf(spec, spec.Airless && spec.Archetype != Archetype.Ice ? U(15) : U(0));
        // Iron: most rock carries some; one world in four is iron-rich (red beds, Mars).
        float iron = U(1) > 0.75f ? 0.75f + 0.25f * U(2) : 0.12f + 0.5f * U(2);
        float life = LifeOf(spec);
        // The air's particles come and go by world, as Earth's do from place to place and Mars' from season to season:
        // a third to two and a half times its kind's usual, evenly on a log scale.
        air = air with { Aerosol = air.Aerosol * MathF.Exp(MathF.Log(0.35f) + MathF.Log(2.5f / 0.35f) * U(3)) };
        bool wet = air.Column > 0f && !spec.Airless;

        Spectrum light = Spectrum.Blackbody(kelvin);
        light *= 1f / light.Y;
        // Every light here (the sun on the ground, the sky, the sunset) is seen by an eye adapted most of the way to its star, so
        // grey rock reads grey under any star with only a faint cast; the star's colour still shows where the light differs
        // from its own (the sky's scattering, the sunset's reddening, leaf pigments) and in its own disc (Disc).
        Spectrum.Eye eye = light.Adapted(Adaptation), skyEye = light.Adapted(SkyAdaptation);

        // ---- Minerals and soils ----
        // An airless body's regolith is its dark mare basalt and bright highland anorthosite in its own mix (the Moon's).
        Spectrum stone = spec.Airless && rock != Mineral.Ice ? Spectrum.Lerp(Stone(Mineral.Basalt), Stone(Mineral.Anorthosite), U(15)) : Stone(rock);
        // An iron-rich airless body rusted while it still had air and water, and keeps it (Mars, had it lost all its air).
        if (spec.Airless && iron > 0.75f) stone = Rust(stone, 0.55f, 0.25f);
        // On a volcanic world the iron in fresh lava oxidises as it cools: glassy black flows where there's little of it,
        // red-brown scoria where there's much (Mars' and Hawaii's cinder cones).
        if (spec.Archetype == Archetype.Lava && !spec.Airless) stone = Rust(rock is Mineral.Basalt or Mineral.Andesite ? stone * (1f + 1.5f * iron) : stone, 0.6f * iron, 0.2f * iron); // dark lava's scoria is frothier, so brighter, than its flows
        // How long an airless surface has lain under the solar wind, from fresh (bright) to fully weathered (Lucey et al. 2000).
        float maturity = 0.3f + 0.7f * U(16);
        float stain = wet ? iron : 0f;
        Spectrum bedrock = spec.Airless ? Spaced(stone, iron, maturity) : Rust(stone, 0.35f * stain * (spec.Archetype == Archetype.Arid ? 1.6f : 1f), 0.25f * stain);
        // Grains are brighter than the face they came from (small particles scatter more), and keep its iron.
        Spectrum fines = (spec.Airless ? Spaced(stone, iron, maturity) : stone) * 1.3f;
        Spectrum sand = spec.Airless ? fines : Rust(fines, 0.3f * stain, 0.2f * stain);
        Spectrum dust = spec.Airless ? fines : Rust(fines, 0.85f * iron, 0.2f * iron); // the finest grains rust most: dust is the reddest of the ground
        var soils = new Rgb[Grid * Grid];
        for (int t = 0; t < Grid; t++)
            for (int w = 0; w < Grid; w++)
                soils[t * Grid + w] = (spec.Airless ? fines : Soil(fines, t / (Grid - 1f), w / (Grid - 1f), iron, life)).Reflectance(); // no air, no weather: regolith everywhere

        // ---- Air ----
        // Optical depths straight up, by wavelength (nm), so the visible bands and the near infrared the plants see agree.
        float column = air.Column;
        float RayleighAt(float l)
        {
            float um = l / 1000f, u2 = 1f / (um * um);
            return 0.008569f * u2 * u2 * (1f + 0.0113f * u2 + 0.00013f * u2 * u2) * column; // Hansen & Travis 1974
        }
        // The particles: how much they scatter by wavelength (Ångström) and what share they don't absorb (single-scattering albedo).
        (float angstrom, Spectrum albedo) = air.Particles switch
        {
            Particles.Haze => (0.5f, Spectrum.Flat(0.99f)),
            Particles.Dust => (0.1f, Albedo(dust)),
            Particles.Ash => (0.3f, Spectrum.Of(l => 0.5f + 0.06f * (l - 400f) / 300f)), // dark, near grey: glass and rock shards
            Particles.Sulfate => (1.1f, Spectrum.Flat(0.99f)), // Venus' clouds: sulfuric acid, nearly white
            _ => (1.3f, Spectrum.Flat(0.96f)),
        };
        float ParticlesAt(float l) => air.Aerosol * MathF.Pow(l / 550f, -angstrom);
        static float G(float l, float c, float w) => MathF.Exp(-0.5f * (l - c) * (l - c) / (w * w));
        // Gases that absorb: ozone's Chappuis band (deepens twilight blue) and oxygen's A band where there is oxygen;
        // water vapour's near-infrared bands on any world with seas under its air; over Venus-like sulfuric haze,
        // sulfur and its oxides drinking the blue.
        float GasAt(float l) => column * ((air.Oxygen ? 0.03f * G(l, 600f, 45f) + G(l, 762f, 6f) : 0f)
            + (wet ? 0.2f * G(l, 722f, 12f) + 0.35f * G(l, 820f, 18f) + 1.5f * G(l, 940f, 25f) : 0f)
            + (air.Particles == Particles.Sulfate ? 0.35f / (1f + MathF.Exp((l - 470f) / 25f)) : 0f));
        Spectrum rayleigh = Spectrum.Of(RayleighAt), aerosol = Spectrum.Of(ParticlesAt);
        Spectrum extinction = rayleigh + aerosol + Spectrum.Of(GasAt);
        Spectrum scatter = rayleigh + aerosol * albedo;

        // Single scattering of the sun 45° up: the gas's Rayleigh phase, the particles' forward lobe. Thick particles
        // scatter light many times over, which evens their lobe out toward the same in every direction.
        float even = 1f - MathF.Exp(-air.Aerosol);
        Spectrum Sky(float viewAirmass, float rayleighPhase, float particlePhase)
        {
            float lobe = particlePhase + (1f - particlePhase) * even;
            Spectrum share = (rayleigh * rayleighPhase + aerosol * albedo * lobe) / extinction.Map(v => MathF.Max(v, 1e-5f));
            return light * share * (Spectrum.Flat(1f) - Spectrum.Exp(extinction * -viewAirmass)) * Spectrum.Exp(extinction * -0.7f);
        }
        Rgb zenith = default, horizon = default, haze = default, sunset = default, limb = default;
        if (column > 0f)
        {
            zenith = Sky(1f, 1f, 0.8f).Light(SkyGain, skyEye); // the sun higher than 45° at midday: further from its glare
            horizon = Sky(10f, 0.8f, 0.5f).Light(SkyGain, skyEye);
            haze = Sky(40f, 0.8f, 0.5f).Light(SkyGain, skyEye);
            sunset = (light * Spectrum.Exp(extinction * -12f)).Hue(skyEye); // the sun about 5° up
            limb = (light * scatter).Hue(skyEye);
        }

        // ---- Plants ----
        // The light at the ground with the sun 45° up, into the near infrared: oxygen's and water vapour's bands carve it
        // there, which puts the Sun's photon peak at the ground near 685 nm, where chlorophyll absorbs (Kiang et al. 2007).
        Spectrum leaf = Leaves(kelvin, l => MathF.Exp(-1.4f * (RayleighAt(l) + ParticlesAt(l) + GasAt(l))), k => U(4 + k), out float peak);

        // ---- Water ----
        // Phytoplankton by how much lives (clear blue seas to green ones), dissolved matter browning still water,
        // silt from the land's rivers, a little on every world with weather.
        float plankton = life * MathF.Pow(10f, -1.5f + 2.5f * U(12));
        float tannin = 0.01f + 0.5f * U(13) * U(13) * U(13) * (0.3f + life);
        float silt = wet ? 0.4f + 10f * U(14) * U(14) : 0f;
        // The open sea holds a tenth of the silt the shore does; a desert's sea takes wind-blown dust too (the Sahara's falls on the Atlantic).
        (Spectrum deep, Spectrum shallow) = Water(plankton, tannin, silt, silt * (spec.Archetype == Archetype.Arid ? 0.45f : 0.1f), leaf, dust, sand);
        Spectrum snow = Spectrum.Of(l => 0.93f - 0.08f * (l - 400f) / 300f);

        return new WorldPalette
        {
            Kelvin = kelvin, Air = air, Rock = rock, Iron = iron, Life = life, Plankton = plankton,
            Star = light.Hue(eye), Disc = light.Hue(),
            Zenith = zenith, Horizon = horizon, Haze = haze, Sunset = sunset, Limb = limb,
            CloudTint = Rgb.Lerp(new Rgb(1f, 1f, 1f), Albedo(albedo).Reflectance(), Math.Clamp(air.Aerosol * 0.35f, 0f, 0.55f)),
            Dust = dust.Reflectance(),
            Bedrock = bedrock.Reflectance(), Sand = sand.Reflectance(), Snow = snow.Reflectance(),
            Deep = deep.Reflectance(), Shallow = shallow.Reflectance(),
            Ice = (snow * 0.92f).Reflectance(),
            Leaf = leaf.Reflectance(), Peak = peak,
            _soils = soils,
        };
    }

    /// <summary>How far an eye adapts to its star's light: most of the way, never all of it (<see cref="Spectrum.Adapted"/>).</summary>
    const float Adaptation = 0.85f;

    /// <summary>
    /// How far the sky is seen adapted: two thirds of the way (the sky also lights the ground, so a red dwarf's sky kept any warmer would tint grey rock). A sky fills half the view but the eye sets its white by the lit
    /// ground it stands on, so the sky keeps more of its star's cast: skies under M, K, G and F stars differ.
    /// </summary>
    const float SkyAdaptation = 0.65f;

    /// <summary>How bright a sky is against its sunlight: Earth's clear zenith comes out at Earth's (luminance about 0.22 of white).</summary>
    const float SkyGain = 1.4f;

    /// <summary>
    /// Weathered with water and oxygen, iron rusts: hematite reds hot, dry ground, goethite browns cool, wet ground
    /// (Schwertmann): each absorbs below its edge, hematite's near 565 nm, goethite's near 505 nm.
    /// </summary>
    static Spectrum Rust(Spectrum r, float hematite, float goethite) =>
        r * (Spectrum.Flat(1f) - Spectrum.Below(565f, 12f) * (0.78f * hematite) - Spectrum.Below(505f, 18f) * (0.6f * goethite));

    /// <summary>
    /// With no air, iron can't rust: space weathering only darkens the grains and reddens their slope (Hapke 2001),
    /// the more so the more iron there is to make into nanophase metal (iron-rich mare darker than highland) and the
    /// longer it has lain exposed (<paramref name="maturity"/>, 0 fresh to 1 mature).
    /// </summary>
    static Spectrum Spaced(Spectrum r, float iron, float maturity) =>
        r * Spectrum.Of(l => 1f - maturity * (0.38f + 0.3f * iron) + maturity * (0.12f + 0.2f * iron) * (l - 400f) / 300f);

    /// <summary>A world's rock by its type: lava worlds are basalt, airless bodies mare basalt or bright highland (or ice), the rest any of the common crustal rocks.</summary>
    static Mineral RockOf(PlanetSpec spec, float u)
    {
        (Mineral, float)[] odds = spec.Airless
            ? spec.Archetype == Archetype.Ice ? new[] { (Mineral.Ice, 0.6f), (Mineral.Basalt, 0.2f), (Mineral.Anorthosite, 0.2f) }
            : new[] { (Mineral.Basalt, 0.5f), (Mineral.Anorthosite, 0.5f) } // the more of the two in its mix: Make blends them
            : spec.Archetype switch
            {
                Archetype.Lava => new[] { (Mineral.Basalt, 0.45f), (Mineral.Andesite, 0.3f), (Mineral.Granite, 0.25f) }, // granite's volcanic twin, rhyolite: pale and pink
                Archetype.Arid => new[] { (Mineral.Sandstone, 0.3f), (Mineral.Granite, 0.2f), (Mineral.Basalt, 0.25f), (Mineral.Limestone, 0.25f) }, // red beds, grus, black sand, white gypsum and lime
                Archetype.Ice => new[] { (Mineral.Granite, 0.35f), (Mineral.Basalt, 0.35f), (Mineral.Shale, 0.3f) },
                _ => new[] { (Mineral.Granite, 0.25f), (Mineral.Basalt, 0.2f), (Mineral.Sandstone, 0.2f), (Mineral.Limestone, 0.15f), (Mineral.Shale, 0.2f) },
            };
        foreach (var (m, p) in odds)
            if ((u -= p) < 0f) return m;
        return odds[^1].Item1;
    }

    /// <summary>
    /// Fresh rock's reflectance, 400 to 700 nm (from USGS spectral library shapes, simplified): granite pale with
    /// feldspar's pink, basalt dark, quartz sandstone bright before its iron coat, limestone near white, shale
    /// grey, andesite (between basalt and rhyolite) mid-grey, lunar-highland anorthosite bright, ice near white with a little red absorbed.
    /// </summary>
    static Spectrum Stone(Mineral m) => m switch
    {
        Mineral.Granite => Spectrum.Of(l => 0.26f + 0.14f * (l - 400f) / 300f),
        Mineral.Basalt => Spectrum.Of(l => 0.065f + 0.025f * (l - 400f) / 300f),
        Mineral.Andesite => Spectrum.Of(l => 0.15f + 0.05f * (l - 400f) / 300f),
        Mineral.Sandstone => Spectrum.Of(l => 0.34f + 0.16f * (l - 400f) / 300f),
        Mineral.Limestone => Spectrum.Of(l => 0.5f + 0.1f * (l - 400f) / 300f),
        Mineral.Shale => Spectrum.Of(l => 0.11f + 0.05f * (l - 400f) / 300f),
        Mineral.Anorthosite => Spectrum.Of(l => 0.3f + 0.1f * (l - 400f) / 300f),
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
    /// Leaves under a light. Pigments absorb where the light reaching the ground has most photons (Kiang et al. 2007):
    /// in the red under a sunlike star (so green is thrown back), toward the blue under a hot one, out past the
    /// visible under a red dwarf. Where photons are scarce in the visible, as under a red dwarf, leaves take all
    /// they can and go dark; under a hot star light is plenty and they go pale. A porphyrin's blue band always, with the carotenoids beside it that every leaf on Earth carries.
    /// Each world's pigment sits a little either side of the peak and is laid on more or less thickly (Earth's own
    /// chlorophylls a, b, d and f span 640 to 720 nm), and some worlds add helpers: carotenoid-like (absorbing
    /// blue-green), anthocyanin-like (green) and phycobilin-like (orange).
    /// </summary>
    static Spectrum Leaves(float kelvin, Func<float, float> through, Func<int, float> u, out float peak)
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
        // The reaction centre at the peak, its antenna pigment 50 nm to the blue of it (chlorophyll b's 650 nm beside a's
        // 680 nm), and the blue band.
        Spectrum a = Spectrum.Flat(body) + Spectrum.Band(peak, 30f) * 1.4f + Spectrum.Band(peak - 50f, 35f) + Spectrum.Band(450f, 45f) * 1.3f;
        // About one world in three has each helper.
        float Helper(float v) => v < 0.67f ? 0f : (v - 0.67f) * 3f;
        a += Spectrum.Band(480f, 22f) * Helper(u(2)) + Spectrum.Band(545f, 25f) * Helper(u(3)) + Spectrum.Band(615f, 25f) * Helper(u(4));
        return Spectrum.Exp(a * -1f) * (0.45f + 0.1f * u(5));
    }

    /// <summary>
    /// Water's colour from above, deep and over four metres to sand: irradiance reflectance about 0.33 b_b/(a + b_b)
    /// (Morel and Prieur 1977). Water itself absorbs red (Pope and Fry 1997; Smith and Baker 1981) and scatters blue
    /// (Morel 1974): blue. Plankton absorb as the land's leaves do (the same chemistry), dissolved matter absorbs the
    /// blue (Bricaud et al. 1981), and silt scatters the colour of the land's dust.
    /// </summary>
    static (Spectrum Deep, Spectrum Shallow) Water(float plankton, float tannin, float silt, float openSilt, Spectrum leaf, Spectrum dust, Spectrum sand)
    {
        Spectrum water = Spectrum.Of(PureWater);
        Spectrum pigment = leaf.Map(r => -MathF.Log(MathF.Max(r, 1e-3f)));
        float top = 0f;
        for (int i = 0; i < Spectrum.Bands; i++) top = MathF.Max(top, pigment[i]);
        Spectrum grains = Albedo(dust);
        // The light coming up from under the surface (the water's glint off the surface is the renderer's).
        (Spectrum r, Spectrum k) Body(float mud)
        {
            Spectrum a = water + pigment * (0.06f * MathF.Pow(plankton, 0.65f) / MathF.Max(top, 1e-3f))
                         + Spectrum.Of(l => tannin * MathF.Exp(-0.014f * (l - 440f)))
                         + grains.Map(w => (1f - w) * 0.04f * mud);
            Spectrum bb = Spectrum.Of(l => 0.5f * 0.00222f * MathF.Pow(500f / l, 4.32f) + 0.003f * MathF.Pow(plankton, 0.62f) * 550f / l)
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
        Rgb ground = Ground(biome, warm, wet);
        Rgb? green = growth ?? (Life > 0f ? Leaf : null);
        return green is Rgb g ? Rgb.Lerp(ground, g, Cover(biome)) : ground;
    }

    /// <summary>How much of a biome's ground is hidden by what grows on it: a meadow's grass all but hides its soil.</summary>
    public static float Cover(Biome b) => b switch
    {
        Biome.Grassland or Biome.Meadow => 0.75f,
        Biome.Jungle or Biome.Riverbank or Biome.Oasis => 0.65f,
        Biome.Forest or Biome.Rainforest => 0.55f,
        Biome.PineForest or Biome.CloudForest => 0.5f,
        Biome.Swamp or Biome.SporeMarsh or Biome.GlowMoss => 0.45f,
        Biome.Tundra or Biome.Scrub or Biome.HotSpring => 0.3f,
        _ => 0f,
    };

    /// <summary>
    /// The ground's own colour for a biome in a climate (warmth and wetness 0..1, a map cell's): the soil its climate
    /// weathers from the world's rock, or where the biome is a mineral in itself, that mineral (sand, snow, salt,
    /// basalt). Grounds whose chemistry this model doesn't hold keep their own (<see cref="BiomeLook.Ground"/>).
    /// </summary>
    public Rgb Ground(Biome biome, float warm, float wet)
    {
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
            case Biome.BlueIce: case Biome.HotSpring: case Biome.Sinter:
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
