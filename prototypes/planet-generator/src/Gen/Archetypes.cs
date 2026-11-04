using System;

namespace Planets.Gen;

/// <summary>What fills the oceans and rivers.</summary>
public enum Liquid { Water, Lava, Exotic }

/// <summary>What stops you at the poles.</summary>
public enum CapStyle { StormWall, IceCliff }

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
    public float MountainHeight { get; init; } = 500f;
    public float MountainWavelength { get; init; } = 3000f;
    public float RidgeSharpness { get; init; } = 2f;
    public float HillHeight { get; init; } = 60f;
    public float HillWavelength { get; init; } = 900f;
    public float Billow { get; init; }                     // 0 = soft hills, 1 = rounded domes with creases
    public float Roughness { get; init; } = 2f;            // fine detail amplitude, only seen near the player

    // Landforms, all optional
    public float TerraceStep { get; init; }                // mesas, basalt steps, crystal plates
    public float TerraceCover { get; init; }               // how much of the land is terraced, 0-1
    public float DuneHeight { get; init; }
    public float DuneWavelength { get; init; } = 140f;
    public float CanyonDepth { get; init; }
    public float CanyonWavelength { get; init; } = 2600f;
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
    public CapStyle Cap { get; init; } = CapStyle.StormWall;
    public Biome RiverbankBiome { get; init; } = Biome.Riverbank;

    // Gameplay hooks: shown in the debug label and the README, used later by survival systems
    public string Hazard { get; init; } = "";
    public string Resources { get; init; } = "";

    /// <summary>The resource this variant is known for, scattered on top of the common materials.</summary>
    public DepositKind Signature { get; init; } = DepositKind.Titanium;

    /// <summary>The variant with this planet's terrain modifiers applied.</summary>
    public ArchetypeDef WithModifiers(Modifier mods)
    {
        ArchetypeDef d = this;
        if ((mods & Modifier.Highlands) != 0)
            d = d with { MountainHeight = d.MountainHeight * 1.5f, HillHeight = d.HillHeight * 1.5f, HighlandLift = 70f };
        if ((mods & Modifier.Cratered) != 0 && d.CraterSpacing == 0)
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

    static readonly ArchetypeDef[] Table =
    {
        new()
        {
            Archetype = Archetype.Temperate, Variant = 0, VariantName = "meadow",
            LandMin = 0.36f, LandMax = 0.52f,
            ContinentScale = 0.5f, ContinentAmp = 260f, MountainHeight = 700f, HillHeight = 70f,
            Roughness = 2.2f, RiverArea = 0.22f, Rain = 1f,
            BaseTemperature = 0.62f, LatitudeRange = 0.6f, BaseHumidity = 0.5f,
            Hazard = "mild: thunderstorms, predators", Resources = "timber, iron, copper, fresh water, titanium", Signature = DepositKind.Titanium,
        },
        new()
        {
            Archetype = Archetype.Temperate, Variant = 1, VariantName = "rainforest",
            LandMin = 0.40f, LandMax = 0.56f,
            ContinentScale = 0.4f, ContinentAmp = 220f, MountainHeight = 480f, RidgeSharpness = 1.6f,
            HillHeight = 95f, HillWavelength = 650f, Billow = 0.45f, Roughness = 2.8f,
            RiverArea = 0.12f, Rain = 1.6f,
            BaseTemperature = 0.84f, LatitudeRange = 0.4f, BaseHumidity = 0.78f,
            Hazard = "humidity, aggressive fauna, flash floods", Resources = "timber, rubber resin, medicinal plants, water", Signature = DepositKind.Resin,
        },
        new()
        {
            Archetype = Archetype.Arid, Variant = 0, VariantName = "dunes",
            LandMin = 0.86f, LandMax = 0.95f,
            ContinentScale = 0.38f, ContinentAmp = 150f, MountainHeight = 320f, RidgeSharpness = 2.6f,
            HillHeight = 30f, Roughness = 1.2f,
            DuneHeight = 22f, DuneWavelength = 170f, TerraceStep = 28f, TerraceCover = 0.3f,
            RiverArea = 1.2f, Rain = 0.3f, SeaFreezes = false,
            BaseTemperature = 0.88f, LatitudeRange = 0.35f, BaseHumidity = 0.1f, RiverbankBiome = Biome.Oasis,
            Hazard = "heat, sandstorms, dehydration", Resources = "silicon, salt, titanium sand", Signature = DepositKind.Titanium,
        },
        new()
        {
            Archetype = Archetype.Arid, Variant = 1, VariantName = "canyon",
            LandMin = 0.78f, LandMax = 0.90f,
            ContinentScale = 0.42f, ContinentAmp = 210f, MountainHeight = 220f, HillHeight = 25f,
            Roughness = 1.8f, TerraceStep = 42f, TerraceCover = 0.85f,
            CanyonDepth = 170f, CanyonWavelength = 2400f,
            RiverArea = 0.45f, Rain = 0.45f, SeaFreezes = false,
            BaseTemperature = 0.8f, LatitudeRange = 0.35f, BaseHumidity = 0.16f, RiverbankBiome = Biome.Oasis,
            Hazard = "heat, rockfalls, flash floods", Resources = "copper, iron oxide, gemstones", Signature = DepositKind.Crystal,
        },
        new()
        {
            Archetype = Archetype.Ice, Variant = 0, VariantName = "glacier",
            LandMin = 0.52f, LandMax = 0.68f,
            ContinentScale = 0.45f, ContinentAmp = 240f, MountainHeight = 760f, RidgeSharpness = 2.4f,
            HillHeight = 45f, Roughness = 0.9f, ShelfHeight = 32f,
            RiverArea = 0.9f, Rain = 0.5f,
            BaseTemperature = 0.12f, LatitudeRange = 0.35f, BaseHumidity = 0.4f,
            Cap = CapStyle.IceCliff, RiverbankBiome = Biome.IceTundra,
            Hazard = "extreme cold, crevasses, whiteouts", Resources = "ice, cobalt, frozen gases", Signature = DepositKind.Cobalt,
        },
        new()
        {
            Archetype = Archetype.Ice, Variant = 1, VariantName = "hot spring",
            LandMin = 0.56f, LandMax = 0.72f,
            ContinentScale = 0.4f, ContinentAmp = 220f, MountainHeight = 460f, HillHeight = 80f,
            Billow = 0.35f, Roughness = 1.6f, TerraceStep = 5f, TerraceCover = 0.35f,
            RiverArea = 0.4f, Rain = 0.8f,
            BaseTemperature = 0.2f, LatitudeRange = 0.35f, BaseHumidity = 0.5f,
            Cap = CapStyle.IceCliff, RiverbankBiome = Biome.IceTundra,
            Hazard = "cold, scalding geysers, steam", Resources = "geothermal energy, sulfur, minerals, ice", Signature = DepositKind.Sulfur,
        },
        new()
        {
            Archetype = Archetype.Lava, Variant = 0, VariantName = "volcanic",
            LandMin = 0.68f, LandMax = 0.82f,
            ContinentScale = 0.4f, ContinentAmp = 200f, MountainHeight = 300f, RidgeSharpness = 3f,
            HillHeight = 45f, Roughness = 3f, TerraceStep = 22f, TerraceCover = 0.35f,
            VolcanoSpacing = 3200f, VolcanoHeight = 720f,
            RiverArea = 0.6f, Rain = 0.6f, SeaFreezes = false,
            BaseTemperature = 0.95f, LatitudeRange = 0.2f, BaseHumidity = 0.1f,
            Liquid = Liquid.Lava, RiverbankBiome = Biome.Obsidian,
            Hazard = "heat, eruptions, toxic air", Resources = "iron, sulfur, obsidian, thorium", Signature = DepositKind.Thorium,
        },
        new()
        {
            Archetype = Archetype.Lava, Variant = 1, VariantName = "lava ocean",
            LandMin = 0.30f, LandMax = 0.45f,
            ContinentScale = 0.28f, ContinentAmp = 180f, MountainHeight = 200f, RidgeSharpness = 3f,
            HillHeight = 40f, Roughness = 2.6f,
            VolcanoSpacing = 2300f, VolcanoHeight = 560f,
            RiverArea = 0.8f, Rain = 0.5f, SeaFreezes = false,
            BaseTemperature = 0.98f, LatitudeRange = 0.15f, BaseHumidity = 0.08f,
            Liquid = Liquid.Lava, RiverbankBiome = Biome.Obsidian,
            Hazard = "extreme heat, lava surges", Resources = "rare metals, obsidian, heat", Signature = DepositKind.Titanium,
        },
        new()
        {
            Archetype = Archetype.Alien, Variant = 0, VariantName = "crystal",
            LandMin = 0.50f, LandMax = 0.66f,
            ContinentScale = 0.45f, ContinentAmp = 230f, MountainHeight = 520f, RidgeSharpness = 4f,
            HillHeight = 40f, Roughness = 1.4f, SpireHeight = 130f, TerraceStep = 18f, TerraceCover = 0.4f,
            RiverArea = 0.4f, Rain = 0.7f, SeaFreezes = false,
            BaseTemperature = 0.55f, LatitudeRange = 0.4f, BaseHumidity = 0.35f,
            Liquid = Liquid.Exotic, RiverbankBiome = Biome.GlowMoss,
            Hazard = "radiation, resonant shard storms", Resources = "exotic crystals, quartzite, helium-3", Signature = DepositKind.Crystal,
        },
        new()
        {
            Archetype = Archetype.Alien, Variant = 1, VariantName = "fungal",
            LandMin = 0.45f, LandMax = 0.60f,
            ContinentScale = 0.42f, ContinentAmp = 280f, MountainHeight = 280f, HillHeight = 65f,
            HillWavelength = 1100f, Billow = 0.85f, Roughness = 2f, TerraceStep = 9f, TerraceCover = 0.3f,
            RiverArea = 0.18f, Rain = 1.3f, SeaFreezes = false,
            BaseTemperature = 0.66f, LatitudeRange = 0.35f, BaseHumidity = 0.78f,
            Liquid = Liquid.Exotic, RiverbankBiome = Biome.SporeMarsh,
            Hazard = "toxic spores, hostile fauna", Resources = "biomass, luminous resin, nitrates", Signature = DepositKind.Resin,
        },
    };
}
