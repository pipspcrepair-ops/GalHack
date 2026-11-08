using System;

namespace Planets.Gen;

/// <summary>What fills the oceans and rivers.</summary>
public enum Liquid { Water, Lava, Exotic }

/// <summary>
/// Tuning for one archetype variant. Heights are metres, wavelengths metres unless noted,
/// climate values 0-1. Each archetype differs from the others in terrain shape, palette, sky,
/// flora, hazard and resources; the shape half of that lives here, the look half in Look.
/// </summary>
public sealed record ArchetypeDef
{
    public Archetype Archetype { get; init; }
    public int Variant { get; init; }
    public string VariantName { get; init; } = "";

    // Land fraction band. Each planet rolls a target inside it; sea level is set by quantile to hit it.
    public float LandMin { get; init; }
    public float LandMax { get; init; }

    // Large-scale shape
    public float ContinentScale { get; init; } = 0.45f;   // continent wavelength as a fraction of map width
    public float ContinentAmp { get; init; } = 240f;
    public float TerrainCeiling { get; init; } = 900f;     // metres: nothing stands higher. Clouds lie at 600-1,000, orbit starts at 1,060
    public float MountainHeight { get; init; } = 340f;     // most ranges top out about 500 m, base ground included
    public float SummitHeight { get; init; }               // the great peaks: added to crests only in the core of the biggest ranges
    public float MountainWavelength { get; init; } = 1600f; // short, so low ranges stay as steep as tall ones would be
    public float RidgeSharpness { get; init; } = 2f;
    public int MountainOctaves { get; init; } = 7;        // fewer octaves: massive faces instead of noisy bumps
    public float RangeCover { get; init; } = 0.5f;        // share of inland ground in mountain ranges; the rest is lowland
    public float HillHeight { get; init; } = 60f;
    public float HillWavelength { get; init; } = 900f;
    public float Billow { get; init; }                     // 0 = soft hills, 1 = rounded domes with creases
    public float Roughness { get; init; } = 2f;            // fine detail amplitude, only seen near the player

    // Landforms, all optional
    public float TerraceStep { get; init; }                // mesas, basalt steps, crystal plates
    public float TerraceCover { get; init; }               // how much of the land is terraced, 0-1
    public float DuneHeight { get; init; }
    public float DuneWavelength { get; init; } = 230f;
    public float CanyonDepth { get; init; }
    public float CanyonWavelength { get; init; } = 2600f;
    public float CanyonWidth { get; init; } = 0.075f;      // noise units: wider gives broad U valleys
    public int CanyonSteps { get; init; }                  // stepped walls (0 = one smooth wall)
    public float PlateauHeight { get; init; }              // escarpments, plateaus and tablelands (0 = none)
    public float PlateauCover { get; init; } = 0.35f;      // share of land raised onto the first tier
    public float PlateauWavelength { get; init; } = 1500f;
    public float VolcanoSpacing { get; init; }             // 0 = no volcanoes
    public float VolcanoHeight { get; init; }
    public float CraterSpacing { get; init; }              // 0 = no craters
    public float SpireHeight { get; init; }                // knife-edge crystal ridges
    public float ShelfHeight { get; init; }                // coastal ice cliffs
    public float HighlandLift { get; init; }

    // Water and climate
    public float RiverArea { get; init; } = 0.3f;          // catchment in km² before a stream counts as a river
    public float Rain { get; init; } = 1f;
    public float BaseTemperature { get; init; } = 0.6f;
    public float LatitudeRange { get; init; } = 0.5f;      // how much colder the poles are than the equator
    public float BaseHumidity { get; init; } = 0.5f;
    public Liquid Liquid { get; init; } = Liquid.Water;
    public bool SeaFreezes { get; init; } = true;
    public Biome RiverbankBiome { get; init; } = Biome.Riverbank;

    // Gameplay hooks: shown in the debug label and the README, used later by survival systems
    public string Hazard { get; init; } = "";
    public string Resources { get; init; } = "";

    /// <summary>The resource this variant is known for, scattered on top of the common materials.</summary>
    public DepositKind Signature { get; init; } = DepositKind.Magnesium;
    /// <summary>The world type's deposits against the ore table's: more of them (above 1) or fewer, and bigger or smaller.</summary>
    public float DepositCount { get; init; } = 1f;
    public float DepositSize { get; init; } = 1f;

    // Landforms (see LANDFORMS.md): which this variant's worlds can roll and how likely each is,
    // which may make up a whole world on their own, and how often a world is one landform throughout.
    public LandformOdds[] Landforms { get; init; } = Array.Empty<LandformOdds>();
    public Landform[] SingleLandforms { get; init; } = Array.Empty<Landform>();
    public float SingleChance { get; init; } = 0.2f;

    /// <summary>The variant with this planet's terrain modifiers applied.</summary>
    public ArchetypeDef WithModifiers(Modifier mods)
    {
        ArchetypeDef d = this;
        if ((mods & Modifier.Highlands) != 0)
            d = d with { MountainHeight = d.MountainHeight * 1.25f, HillHeight = d.HillHeight * 1.25f, HighlandLift = 40f };
        // Natural craters only where they stay: a living world's weather and seas wore its old impacts away.
        if ((mods & Modifier.Cratered) != 0 && d.CraterSpacing == 0 && PlanetSpec.Craters(Archetype))
            d = d with { CraterSpacing = 650f };
        if ((mods & Modifier.Mesas) != 0)
            d = d with { TerraceStep = d.TerraceStep > 0 ? d.TerraceStep : 40f, TerraceCover = Math.Max(d.TerraceCover, 0.6f) };
        if ((mods & Modifier.Canyons) != 0)
            d = d with { CanyonDepth = Math.Max(d.CanyonDepth, 110f) };
        return d;
    }
}

public static class Archetypes
{
    public static readonly string[] Names = { "Temperate", "Arid", "Ice", "Lava", "Alien" };

    public static ArchetypeDef Get(Archetype archetype, int variant) =>
        Table[(int)archetype * PlanetSpec.VariantCount + Math.Clamp(variant, 0, PlanetSpec.VariantCount - 1)];

    public static ArchetypeDef For(PlanetSpec spec) => Get(spec.Archetype, spec.Variant).WithModifiers(spec.Modifiers);

    static LandformOdds[] Odds(params (Landform l, float w)[] odds)
    {
        var result = new LandformOdds[odds.Length];
        for (int i = 0; i < odds.Length; i++) result[i] = new LandformOdds(odds[i].l, odds[i].w);
        return result;
    }

    static readonly ArchetypeDef[] Table =
    {
        new()
        {
            Archetype = Archetype.Temperate, Variant = 0, VariantName = "meadow",
            LandMin = 0.36f, LandMax = 0.52f,
            ContinentScale = 0.5f, ContinentAmp = 260f, MountainHeight = 385f, SummitHeight = 350f, MountainWavelength = 1150f, RidgeSharpness = 1.1f, HillHeight = 70f,
            Roughness = 2.2f, RiverArea = 0.22f, Rain = 1f,
            PlateauHeight = 58f, PlateauCover = 0.3f, CanyonDepth = 70f, CanyonWidth = 0.05f, CanyonWavelength = 2800f,
            BaseTemperature = 0.62f, LatitudeRange = 0.6f, BaseHumidity = 0.5f,
            Hazard = "mild: thunderstorms, predators", Resources = "timber, iron, copper, fresh water", Signature = DepositKind.Iron,
            Landforms = Odds((Landform.Plains, 3.2f), (Landform.Hills, 1.8f), (Landform.Mountains, 2f), (Landform.Steppe, 1.6f),
                (Landform.RiverValleys, 1.2f), (Landform.LakeDistrict, 0.9f), (Landform.Delta, 0.9f), (Landform.Plateau, 0.7f),
                (Landform.CoastalCliffs, 0.8f), (Landform.Fjords, 0.6f), (Landform.Wetlands, 0.5f), (Landform.Archipelago, 0.4f),
                (Landform.Karst, 0.3f), (Landform.DuneSea, 0.25f), (Landform.RockyDesert, 0.2f), (Landform.Badlands, 0.2f)),
            SingleLandforms = new[] { Landform.Plains, Landform.Steppe, Landform.Archipelago }, SingleChance = 0.17f,
        },
        new()
        {
            Archetype = Archetype.Temperate, Variant = 1, VariantName = "rainforest",
            LandMin = 0.40f, LandMax = 0.56f,
            ContinentScale = 0.4f, ContinentAmp = 220f, MountainHeight = 330f, SummitHeight = 280f, MountainWavelength = 1700f, RidgeSharpness = 1.6f,
            HillHeight = 95f, HillWavelength = 650f, Billow = 0.45f, Roughness = 2.8f,
            PlateauHeight = 40f, PlateauCover = 0.2f, PlateauWavelength = 1700f, CanyonDepth = 60f, CanyonWidth = 0.05f,
            RiverArea = 0.12f, Rain = 1.6f,
            BaseTemperature = 0.84f, LatitudeRange = 0.4f, BaseHumidity = 0.78f,
            Hazard = "humidity, aggressive fauna, flash floods", Resources = "timber, rubber resin, medicinal plants, water", Signature = DepositKind.Resin,
            Landforms = Odds((Landform.Hills, 2.5f), (Landform.Plains, 2f), (Landform.Wetlands, 2f), (Landform.RiverValleys, 1.5f),
                (Landform.Mountains, 1.5f), (Landform.Delta, 1.2f), (Landform.Karst, 1.2f), (Landform.Plateau, 0.7f),
                (Landform.LakeDistrict, 0.6f), (Landform.Archipelago, 0.6f), (Landform.CoastalCliffs, 0.5f)),
            SingleLandforms = new[] { Landform.Wetlands, Landform.Karst, Landform.Archipelago }, SingleChance = 0.18f,
        },
        new()
        {
            Archetype = Archetype.Arid, Variant = 0, VariantName = "dunes",
            LandMin = 0.86f, LandMax = 0.95f,
            ContinentScale = 0.38f, ContinentAmp = 150f, MountainHeight = 250f, SummitHeight = 200f, RidgeSharpness = 2.6f,
            HillHeight = 30f, Roughness = 1.2f,
            DuneHeight = 36f, DuneWavelength = 240f, TerraceStep = 28f, TerraceCover = 0.3f,
            PlateauHeight = 30f, PlateauCover = 0.2f, PlateauWavelength = 1800f,
            RiverArea = 1.2f, Rain = 0.3f, SeaFreezes = false,
            BaseTemperature = 0.88f, LatitudeRange = 0.35f, BaseHumidity = 0.1f, RiverbankBiome = Biome.Oasis,
            Hazard = "heat, sandstorms, dehydration", Resources = "silicon, salt, magnesium sand", Signature = DepositKind.Magnesium,
            Landforms = Odds((Landform.DuneSea, 4f), (Landform.RockyDesert, 2.5f), (Landform.Steppe, 1f), (Landform.Mesas, 1f),
                (Landform.Badlands, 0.8f), (Landform.Plateau, 0.8f), (Landform.Mountains, 0.8f), (Landform.Canyonlands, 0.4f),
                (Landform.CoastalCliffs, 0.4f), (Landform.Delta, 0.35f), (Landform.Plains, 0.4f)),
            SingleLandforms = new[] { Landform.DuneSea, Landform.DuneSea, Landform.RockyDesert }, SingleChance = 0.28f,
        },
        new()
        {
            Archetype = Archetype.Arid, Variant = 1, VariantName = "canyon",
            LandMin = 0.78f, LandMax = 0.90f,
            ContinentScale = 0.42f, ContinentAmp = 210f, MountainHeight = 210f, SummitHeight = 160f, HillHeight = 25f,
            Roughness = 1.8f, TerraceStep = 42f, TerraceCover = 0.85f,
            CanyonDepth = 220f, CanyonWavelength = 2400f, CanyonWidth = 0.07f, CanyonSteps = 3,
            PlateauHeight = 60f, PlateauCover = 0.4f,
            RiverArea = 0.45f, Rain = 0.45f, SeaFreezes = false,
            BaseTemperature = 0.8f, LatitudeRange = 0.35f, BaseHumidity = 0.16f, RiverbankBiome = Biome.Oasis,
            Hazard = "heat, rockfalls, flash floods", Resources = "copper, iron oxide, magnesium", Signature = DepositKind.Magnesium,
            Landforms = Odds((Landform.Canyonlands, 3f), (Landform.Mesas, 3f), (Landform.Plateau, 2f), (Landform.Badlands, 1.5f),
                (Landform.RockyDesert, 1.5f), (Landform.Steppe, 1.2f), (Landform.DuneSea, 0.8f), (Landform.Mountains, 0.8f),
                (Landform.CoastalCliffs, 0.6f), (Landform.RiverValleys, 0.5f), (Landform.Delta, 0.3f)),
            SingleLandforms = new[] { Landform.Mesas, Landform.Canyonlands }, SingleChance = 0.18f,
        },
        new()
        {
            Archetype = Archetype.Ice, Variant = 0, VariantName = "glacier",
            LandMin = 0.52f, LandMax = 0.68f,
            ContinentScale = 0.45f, ContinentAmp = 240f, MountainHeight = 425f, SummitHeight = 320f, MountainWavelength = 1250f, RidgeSharpness = 1.35f,
            HillHeight = 45f, Roughness = 0.9f, ShelfHeight = 32f,
            CanyonDepth = 110f, CanyonWidth = 0.11f, CanyonWavelength = 3200f, PlateauHeight = 40f, PlateauCover = 0.25f,
            RiverArea = 0.9f, Rain = 0.5f,
            BaseTemperature = 0.12f, LatitudeRange = 0.35f, BaseHumidity = 0.4f,
            RiverbankBiome = Biome.IceTundra,
            Hazard = "extreme cold, crevasses, whiteouts", Resources = "ice, cobalt, frozen gases", Signature = DepositKind.Cobalt,
            Landforms = Odds((Landform.IcePlain, 3.5f), (Landform.Mountains, 2.5f), (Landform.Fjords, 2f), (Landform.CoastalCliffs, 1.2f),
                (Landform.Hills, 1f), (Landform.Plateau, 1f), (Landform.LakeDistrict, 0.6f), (Landform.Steppe, 0.6f),
                (Landform.Plains, 0.6f), (Landform.Archipelago, 0.5f)),
            SingleLandforms = new[] { Landform.IcePlain }, SingleChance = 0.25f,
        },
        new()
        {
            Archetype = Archetype.Ice, Variant = 1, VariantName = "hot spring",
            LandMin = 0.56f, LandMax = 0.72f,
            ContinentScale = 0.4f, ContinentAmp = 220f, MountainHeight = 300f, SummitHeight = 270f, HillHeight = 80f,
            Billow = 0.35f, Roughness = 1.6f, TerraceStep = 5f, TerraceCover = 0.35f,
            PlateauHeight = 30f, PlateauCover = 0.3f, CanyonDepth = 60f, CanyonWidth = 0.06f,
            RiverArea = 0.4f, Rain = 0.8f,
            BaseTemperature = 0.2f, LatitudeRange = 0.35f, BaseHumidity = 0.5f,
            RiverbankBiome = Biome.IceTundra,
            Hazard = "cold, scalding geysers, steam", Resources = "geothermal energy, burnstone, minerals, ice", Signature = DepositKind.Burnstone,
            Landforms = Odds((Landform.IcePlain, 2f), (Landform.Hills, 2f), (Landform.VolcanicField, 2f), (Landform.Mountains, 1.5f),
                (Landform.LakeDistrict, 1.2f), (Landform.Plains, 1f), (Landform.Fjords, 1f), (Landform.Plateau, 0.8f),
                (Landform.Wetlands, 0.6f), (Landform.CoastalCliffs, 0.6f)),
            SingleLandforms = new[] { Landform.IcePlain, Landform.LakeDistrict }, SingleChance = 0.18f,
        },
        new()
        {
            Archetype = Archetype.Lava, Variant = 0, VariantName = "volcanic",
            LandMin = 0.68f, LandMax = 0.82f,
            ContinentScale = 0.4f, ContinentAmp = 200f, MountainHeight = 250f, SummitHeight = 210f, RidgeSharpness = 3f,
            HillHeight = 45f, Roughness = 3f, TerraceStep = 22f, TerraceCover = 0.35f,
            VolcanoSpacing = 3200f, VolcanoHeight = 470f,
            PlateauHeight = 35f, PlateauCover = 0.3f, CanyonDepth = 50f, CanyonWidth = 0.04f, CanyonSteps = 2,
            RiverArea = 0.6f, Rain = 0.6f, SeaFreezes = false,
            BaseTemperature = 0.95f, LatitudeRange = 0.2f, BaseHumidity = 0.1f,
            Liquid = Liquid.Lava, RiverbankBiome = Biome.Obsidian,
            Hazard = "heat, eruptions, toxic air", Resources = "iron, burnstone, obsidian, gold", Signature = DepositKind.Gold,
            Landforms = Odds((Landform.VolcanicField, 4f), (Landform.BasaltPlains, 3f), (Landform.Mountains, 1f), (Landform.Badlands, 1f),
                (Landform.Plateau, 1f), (Landform.Canyonlands, 0.6f), (Landform.RockyDesert, 0.5f), (Landform.Mesas, 0.5f),
                (Landform.CoastalCliffs, 0.5f)),
            SingleLandforms = new[] { Landform.BasaltPlains, Landform.VolcanicField }, SingleChance = 0.2f,
        },
        new()
        {
            Archetype = Archetype.Lava, Variant = 1, VariantName = "lava ocean",
            LandMin = 0.30f, LandMax = 0.45f,
            ContinentScale = 0.28f, ContinentAmp = 180f, MountainHeight = 200f, SummitHeight = 140f, RidgeSharpness = 3f,
            HillHeight = 40f, Roughness = 2.6f,
            VolcanoSpacing = 2300f, VolcanoHeight = 400f, PlateauHeight = 25f, PlateauCover = 0.25f,
            RiverArea = 0.5f, Rain = 0.5f, SeaFreezes = false,
            BaseTemperature = 0.98f, LatitudeRange = 0.15f, BaseHumidity = 0.08f,
            Liquid = Liquid.Lava, RiverbankBiome = Biome.Obsidian,
            Hazard = "extreme heat, lava surges", Resources = "rare metals, obsidian, heat", Signature = DepositKind.Gold,
            Landforms = Odds((Landform.Archipelago, 3f), (Landform.VolcanicField, 3f), (Landform.BasaltPlains, 2f), (Landform.CoastalCliffs, 1.5f),
                (Landform.Plateau, 0.5f), (Landform.Badlands, 0.5f)),
            SingleLandforms = new[] { Landform.Archipelago }, SingleChance = 0.22f,
        },
        new()
        {
            Archetype = Archetype.Alien, Variant = 0, VariantName = "crystal",
            LandMin = 0.50f, LandMax = 0.66f,
            ContinentScale = 0.45f, ContinentAmp = 230f, MountainHeight = 350f, SummitHeight = 290f, MountainWavelength = 1700f, RidgeSharpness = 4f,
            HillHeight = 40f, Roughness = 1.4f, SpireHeight = 130f, TerraceStep = 18f, TerraceCover = 0.4f,
            PlateauHeight = 50f, PlateauCover = 0.35f, CanyonDepth = 80f, CanyonWidth = 0.05f, CanyonSteps = 2,
            RiverArea = 0.4f, Rain = 0.7f, SeaFreezes = false,
            BaseTemperature = 0.55f, LatitudeRange = 0.4f, BaseHumidity = 0.35f,
            Liquid = Liquid.Exotic, RiverbankBiome = Biome.GlowMoss,
            Hazard = "radiation, resonant shard storms", Resources = "exotic crystals, quartzite, helium-3", Signature = DepositKind.Crystal,
            Landforms = Odds((Landform.ShardFields, 3f), (Landform.Plains, 2f), (Landform.Ringlands, 1.5f), (Landform.Mesas, 1.5f),
                (Landform.Canyonlands, 1.2f), (Landform.Mountains, 1.2f), (Landform.Plateau, 1f), (Landform.RockyDesert, 0.6f),
                (Landform.DuneSea, 0.4f), (Landform.CoastalCliffs, 0.4f)),
            SingleLandforms = new[] { Landform.ShardFields, Landform.Ringlands }, SingleChance = 0.2f,
        },
        new()
        {
            Archetype = Archetype.Alien, Variant = 1, VariantName = "fungal",
            LandMin = 0.45f, LandMax = 0.60f,
            ContinentScale = 0.42f, ContinentAmp = 280f, MountainHeight = 230f, SummitHeight = 200f, HillHeight = 65f,
            HillWavelength = 1100f, Billow = 0.85f, Roughness = 2f, TerraceStep = 9f, TerraceCover = 0.3f,
            PlateauHeight = 25f, PlateauCover = 0.25f, CanyonDepth = 45f, CanyonWidth = 0.06f,
            RiverArea = 0.18f, Rain = 1.3f, SeaFreezes = false,
            BaseTemperature = 0.66f, LatitudeRange = 0.35f, BaseHumidity = 0.78f,
            Liquid = Liquid.Exotic, RiverbankBiome = Biome.SporeMarsh,
            Hazard = "toxic spores, hostile fauna", Resources = "biomass, luminous resin, nitrates", Signature = DepositKind.Resin,
            Landforms = Odds((Landform.DomeFields, 3.5f), (Landform.Wetlands, 2f), (Landform.Plains, 1.5f), (Landform.Karst, 1f),
                (Landform.Ringlands, 1f), (Landform.Hills, 1f), (Landform.LakeDistrict, 1f), (Landform.Delta, 0.6f),
                (Landform.Archipelago, 0.5f), (Landform.RiverValleys, 0.5f)),
            SingleLandforms = new[] { Landform.DomeFields, Landform.Wetlands }, SingleChance = 0.2f,
        },
    };
}
