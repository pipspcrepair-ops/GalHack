using System;
using System.Collections.Generic;
using System.Linq;
using GalHack.Plants;
using Planets.Gen;
using Planets.Look;
using PlanetSurface.Skies;

namespace PlanetSurface.Terrain;

/// <summary>What ground cover grows in a biome: chances per candidate spot, by family.</summary>
public readonly record struct CoverMix(float Grass, float Forb, float Twig, float Reed, float Moss, float Pins);

/// <summary>
/// A planet's view of the flora module: it turns the world's seed, climate, air and star into a
/// <see cref="FloraClimate"/> and its biomes into habitats, rolls the <see cref="Herbarium"/>, and
/// answers the scatter's questions in the planet's own terms (biome, metres, flora kind). What grows
/// where changes with the region as well as the biome, along a slow field over the land.
/// Read-only once built; workers share it.
/// </summary>
public sealed class PlanetFlora
{
    public readonly Herbarium Book;

    readonly WorldMap _map;
    readonly Fractal _region, _grove, _cover, _tone;
    readonly FloraRule[][] _rules = new FloraRule[64][];

    /// <summary>A region is about this wide, m: a long walk.</summary>
    const double RegionWave = 2600;

    public const int MaxBiomes = 64;

    /// <summary>How thick this world's ground cover grows: dry worlds keep theirs thin.</summary>
    public readonly float CoverDensity;

    public static PlantFamily? FamilyOf(Flora kind) => kind switch
    {
        Flora.Conifer => PlantFamily.Conifer, Flora.Broadleaf => PlantFamily.Broadleaf, Flora.JungleTree => PlantFamily.Jungle,
        Flora.Palm => PlantFamily.Palm, Flora.Shrub => PlantFamily.Shrub, Flora.Cactus => PlantFamily.Cactus,
        Flora.Crystal => PlantFamily.Crystal, Flora.Mushroom => PlantFamily.Fungus, Flora.GiantMushroom => PlantFamily.GiantFungus,
        Flora.IceShard => PlantFamily.Ice, _ => null,
    };

    /// <summary>Whether a world has no air to speak of: the Barren of the design doc, as far as the generator has one.</summary>
    public static bool Airless(PlanetSpec spec) => spec.Has(Modifier.Cratered) && spec.Archetype is Archetype.Arid or Archetype.Lava or Archetype.Ice;

    /// <param name="vacuum">force no air at all, whatever the world says (a test's vacuum world)</param>
    public static PlanetFlora For(WorldMap map, SkySystem sky, bool vacuum = false)
    {
        PlanetSpec spec = map.Spec;
        // Each biome's own climate, from the land it covers.
        var warmth = new float[MaxBiomes];
        var wetness = new float[MaxBiomes];
        var cells = new int[MaxBiomes];
        int land = 0;
        float meanWarm = 0f, meanWet = 0f;
        for (int k = 0; k < map.Count; k++)
        {
            int b = map.Biome[k];
            warmth[b] += map.Temperature[k];
            wetness[b] += map.Humidity[k];
            cells[b]++;
            if ((map.Flags[k] & CellFlags.Water) != 0) continue;
            land++;
            meanWarm += map.Temperature[k];
            meanWet += map.Humidity[k];
        }
        var habitats = new List<Habitat>();
        for (int b = 0; b < MaxBiomes; b++)
        {
            if (cells[b] == 0) continue;
            var families = new List<PlantFamily>();
            foreach (FloraRule r in BiomeLook.FloraOf((Biome)b))
                if (FamilyOf(r.Kind) is PlantFamily f) families.Add(f);
            CoverMix mix = CoverOf((Biome)b);
            if (mix.Grass > 0f) families.Add(PlantFamily.Grass);
            if (mix.Forb > 0f) families.Add(PlantFamily.Forb);
            if (mix.Twig > 0f) families.Add(PlantFamily.Twig);
            if (mix.Reed > 0f) families.Add(PlantFamily.Reed);
            if (mix.Moss > 0f) families.Add(PlantFamily.Moss);
            if (mix.Pins > 0f) families.Add(PlantFamily.Pins);
            if (families.Count > 0) habitats.Add(new Habitat(b, warmth[b] / cells[b], wetness[b] / cells[b], cells[b] / (float)map.Count, families));
        }
        if (land > 0) { meanWarm /= land; meanWet /= land; }
        bool airless = vacuum || Airless(spec);
        float air = airless ? 0f : sky.Density;
        float life = airless ? 0f : spec.Archetype switch
        {
            Archetype.Temperate => 1f,
            Archetype.Alien => 0.85f,
            Archetype.Arid => 0.45f,
            Archetype.Ice => 0.4f,
            _ => 0.12f,
        };
        float alien = spec.Archetype switch { Archetype.Alien => 0.9f, Archetype.Lava => 0.5f, Archetype.Ice => 0.14f, Archetype.Arid => 0.14f, _ => 0.03f };
        Rgb light = sky.StarColor;
        var climate = new FloraClimate(Hash.Of(spec.Seed, 0x466C7261, (long)spec.Archetype, spec.Variant), meanWarm, meanWet, air, new Tone(light.R, light.G, light.B), alien, life);
        return new PlanetFlora(map, Herbarium.Roll(climate, habitats), meanWet);
    }

    PlanetFlora(WorldMap map, Herbarium book, float wet)
    {
        _map = map;
        Book = book;
        CoverDensity = Math.Clamp(0.38f + wet * 0.9f, 0.45f, 1f);
        ulong seed = map.Spec.Seed;
        _region = new Fractal(Hash.Of(seed, 0x72656769), RegionWave, 2);
        _grove = new Fractal(Hash.Of(seed, 0x67726F76), 64, 2);
        _cover = new Fractal(Hash.Of(seed, 0x636F7672), 26, 2);
        _tone = new Fractal(Hash.Of(seed, 0x746F6E65), 900, 2);
        // The world's own density: lush worlds fill their forests in; dry and thin ones stay sparse.
        float lush = Math.Clamp(0.65f + book.Climate.Life * (0.3f + wet * 0.7f), 0.6f, 1.45f);
        for (int b = 0; b < MaxBiomes; b++)
        {
            FloraRule[] rules = BiomeLook.FloraOf((Biome)b);
            var kept = new List<FloraRule>();
            foreach (FloraRule r in rules)
            {
                if (FamilyOf(r.Kind) is not PlantFamily family) kept.Add(r);
                else if (!book.Barren) kept.Add(r with { Density = r.Density * (family is PlantFamily.Crystal or PlantFamily.Ice ? 1f : lush) });
            }
            _rules[b] = kept.ToArray();
        }
    }

    /// <summary>The scatter rules of a biome on this world: rocks everywhere, plants only as far as the world's life allows.</summary>
    public FloraRule[] RulesOf(Biome b) => _rules[(int)b];

    /// <summary>Where a spot lies on the world's region axis, 0..1, wrapping: slow, so regions run for kilometres.</summary>
    public float RegionAt(double x, double z)
    {
        float f = _region.Fbm(_map.Model.Torus.At(x, z), 512) * 1.7f + 0.5f;
        return f - MathF.Floor(f);
    }

    /// <summary>A roll that keeps like with like: plants of one kind stand in groups, not salt and pepper.</summary>
    public float GroveRoll(double x, double z, float random)
    {
        float grove = Math.Clamp(0.5f + _grove.Fbm(_map.Model.Torus.At(x, z), 8) * 0.9f, 0f, 1f);
        return Math.Clamp(grove * 0.62f + random * 0.38f, 0f, 0.9999f);
    }

    /// <summary>The species of a kind at a spot, or null: a rock, or nothing of that kind here.</summary>
    public Plant Pick(Flora kind, Biome biome, double x, double z, float random)
    {
        if (FamilyOf(kind) is not PlantFamily family) return null;
        return Book.Pick((int)biome, family, RegionAt(x, z), GroveRoll(x, z, random));
    }

    /// <summary>Whether this kind is a plant, from the module, on this world.</summary>
    public bool Rolled(Flora kind) => FamilyOf(kind) != null;

    /// <summary>A plant's colour where it stands: each one a little lighter or darker, and each region its own shade of the species.</summary>
    public Rgb Tint(Plant p, float shade, double x, double z)
    {
        float drift = _tone.Fbm(_map.Model.Torus.At(x, z), 64);
        var c = new Rgb(p.Leaf.R, p.Leaf.G, p.Leaf.B);
        Rgb warm = c * new Rgb(1.1f, 1f, 0.84f), cool = c * new Rgb(0.88f, 1f, 1.1f);
        return Rgb.Lerp(cool, warm, Math.Clamp(0.5f + drift, 0f, 1f)) * Mathx.Lerp(0.8f, 1.06f, shade);
    }

    public static Rgb ToRgb(Tone t) => new(t.R, t.G, t.B);

    /// <summary>The colour low growth takes in a biome (weeds, a scree's stunted growth), or null where none grows.</summary>
    public Rgb? LowGrowth(Biome biome)
    {
        Plant p = Book.Pick((int)biome, PlantFamily.Shrub, 0.5f, 0.5f) ?? Book.Pick((int)biome, PlantFamily.Grass, 0.5f, 0.5f);
        return p == null ? null : ToRgb(p.Leaf);
    }

    /// <summary>The world's low growth, wherever it grows: null on a world where nothing does.</summary>
    public Rgb? AnyLowGrowth()
    {
        foreach (Plant p in Book.Plants)
            if (p.Family is PlantFamily.Shrub or PlantFamily.Grass) return ToRgb(p.Leaf);
        return null;
    }

    /// <summary>A patchy field, 0..1, for where cover gathers and thins.</summary>
    public float CoverField(double x, double z) => Math.Clamp(0.5f + _cover.Fbm(_map.Model.Torus.At(x, z), 2) * 1.1f, 0f, 1f);

    /// <summary>What low growth each biome has. The densest ground is grassland and forest floor; barrens get a scatter of tough stuff.</summary>
    public static CoverMix CoverOf(Biome b) => b switch
    {
        Biome.Grassland => new CoverMix(0.82f, 0.05f, 0f, 0f, 0f, 0f),
        Biome.Meadow => new CoverMix(0.72f, 0.14f, 0f, 0f, 0f, 0f),
        Biome.Forest => new CoverMix(0.5f, 0.05f, 0f, 0f, 0.1f, 0f),
        Biome.PineForest => new CoverMix(0.3f, 0f, 0.1f, 0f, 0.2f, 0f),
        Biome.Tundra => new CoverMix(0.25f, 0.03f, 0.1f, 0f, 0.3f, 0f),
        Biome.Riverbank => new CoverMix(0.68f, 0.07f, 0f, 0.3f, 0f, 0f),
        Biome.Jungle => new CoverMix(0.65f, 0.03f, 0f, 0f, 0f, 0f),
        Biome.Rainforest => new CoverMix(0.6f, 0.02f, 0f, 0f, 0.15f, 0f),
        Biome.Swamp => new CoverMix(0.45f, 0f, 0f, 0.4f, 0.1f, 0f),
        Biome.CloudForest => new CoverMix(0.5f, 0.04f, 0f, 0f, 0.25f, 0f),
        Biome.Scrub => new CoverMix(0.18f, 0.02f, 0.3f, 0f, 0f, 0f),
        Biome.MesaTop => new CoverMix(0.08f, 0f, 0.14f, 0f, 0f, 0f),
        Biome.Badlands => new CoverMix(0f, 0f, 0.05f, 0f, 0f, 0f),
        Biome.RedRock => new CoverMix(0f, 0f, 0.05f, 0f, 0f, 0f),
        Biome.Oasis => new CoverMix(0.7f, 0.08f, 0f, 0.25f, 0f, 0f),
        Biome.Beach => new CoverMix(0.06f, 0f, 0.02f, 0f, 0f, 0f),
        Biome.IceTundra => new CoverMix(0.08f, 0f, 0.05f, 0f, 0.3f, 0f),
        Biome.Snowfield => new CoverMix(0f, 0f, 0.04f, 0f, 0.1f, 0f),
        Biome.HotSpring => new CoverMix(0.4f, 0.04f, 0f, 0.2f, 0.2f, 0f),
        Biome.Sinter => new CoverMix(0.05f, 0f, 0f, 0f, 0.2f, 0f),
        Biome.GlowMoss => new CoverMix(0.1f, 0f, 0f, 0f, 0.65f, 0.1f),
        Biome.FungalForest => new CoverMix(0.2f, 0f, 0f, 0f, 0.1f, 0.5f),
        Biome.SporeMarsh => new CoverMix(0.3f, 0f, 0f, 0.3f, 0.1f, 0.35f),
        Biome.Mycelium => new CoverMix(0f, 0f, 0f, 0f, 0.1f, 0.4f),
        Biome.Chalk => new CoverMix(0.1f, 0f, 0.1f, 0f, 0.2f, 0f),
        Biome.CrystalPlain => new CoverMix(0.1f, 0f, 0.06f, 0f, 0.14f, 0f),
        _ => default,
    };
}
