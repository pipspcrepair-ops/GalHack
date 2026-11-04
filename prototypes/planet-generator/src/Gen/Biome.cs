namespace Planets.Gen;

/// <summary>
/// Every biome any archetype can produce. Most belong to one archetype, which is part of what
/// keeps planets distinct; their colours and flora live in Look.BiomeLook.
/// </summary>
public enum Biome : byte
{
    // Liquids and caps, shared by everyone (the liquid itself differs per archetype)
    DeepSea, ShallowSea, FrozenSea, StormCap, IceCap,

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
}

/// <summary>Per-cell flags on the world map.</summary>
public static class CellFlags
{
    public const byte Water = 1 << 0;      // sea (or lava sea, or alien sea) below sea level
    public const byte River = 1 << 1;      // a river runs through this cell
    public const byte Riparian = 1 << 2;   // land on a river bank
    public const byte Cap = 1 << 3;        // polar cap: impassable
    public const byte Coast = 1 << 4;      // land next to the sea
}
