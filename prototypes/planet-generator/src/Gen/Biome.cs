namespace Planets.Gen;

/// <summary>
/// Every biome any archetype can produce. Most belong to one archetype, which is part of what
/// keeps planets distinct; their colours and flora live in Look.BiomeLook.
/// </summary>
public enum Biome : byte
{
    // Liquids, shared by everyone (the liquid itself differs per archetype)
    DeepSea, ShallowSea, FrozenSea,

    // Temperate
    Beach, Grassland, Meadow, Forest, PineForest, Tundra, Snow, Cliff, Riverbank,
    Jungle, Rainforest, Swamp, CloudForest,

    // Arid
    Dunes, Erg, SaltFlat, Scrub, RedRock, Badlands, MesaTop, Oasis,

    // Ice
    Glacier, Snowfield, IceTundra, BlueIce, HotSpring, Sinter,

    // Lava
    Basalt, Ash, LavaField, Obsidian, Sulfur,

    // Alien
    CrystalPlain, CrystalSpires, Chalk, GlowMoss, FungalForest, SporeMarsh, Mycelium,

    // Barren (airless): grey regolith, dark mare basalt, bright highland, iron-rich metal ground, frost
    Regolith, Mare, Highland, Ironstone, Frost,
}

/// <summary>What a barren world's ground is made of, for its colours (the palette maps each to a colour by its star and its minerals).</summary>
public enum Mineral { Regolith, Basalt, Highland, IronRich, Icy }

public static class Minerals
{
    /// <summary>The mineral a barren biome's ground is, or null for a biome that isn't barren ground.</summary>
    public static Mineral? Of(Biome b) => b switch
    {
        Biome.Regolith => Mineral.Regolith,
        Biome.Mare => Mineral.Basalt,
        Biome.Highland => Mineral.Highland,
        Biome.Ironstone => Mineral.IronRich,
        Biome.Frost => Mineral.Icy,
        _ => null,
    };
}

/// <summary>Per-cell flags on the world map.</summary>
public static class CellFlags
{
    public const byte Water = 1 << 0;      // sea (or lava sea, or alien sea) below sea level
    public const byte River = 1 << 1;      // a river runs through this cell
    public const byte Riparian = 1 << 2;   // land on a river bank
    public const byte Coast = 1 << 4;      // land next to the sea
}
