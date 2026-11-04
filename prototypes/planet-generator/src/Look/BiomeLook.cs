using Planets.Gen;

namespace Planets.Look;

/// <summary>Low-poly scatter kinds. Meshes are built in Surface.FloraMeshes.</summary>
public enum Flora { Conifer, Broadleaf, JungleTree, Palm, Shrub, Rock, Cactus, Crystal, Mushroom, GiantMushroom, LavaRock, IceShard }

/// <summary>One kind of scatter in a biome: how many per 1000 m², and its colour.</summary>
public readonly record struct FloraRule(Flora Kind, float Density, Rgb Color);

/// <summary>
/// The colour and flora of every biome. Palettes are bold and saturated, in the spirit of 1970s
/// paperback covers: few colours per world, strong contrast between land, liquid and sky.
/// </summary>
public static class BiomeLook
{
    public static Rgb Color(Biome b) => Colors[(int)b];
    public static FloraRule[] FloraOf(Biome b) => FloraTable[(int)b];

    /// <summary>Self-lit colour (lava cracks, glowing moss). Black for most biomes.</summary>
    public static Rgb Glow(Biome b) => b switch
    {
        Biome.LavaField => Rgb.Hex("ff4a10") * 0.55f,
        Biome.HotSpring => Rgb.Hex("40fff0") * 0.12f,
        Biome.GlowMoss => Rgb.Hex("30ffc0") * 0.18f,
        Biome.CrystalSpires => Rgb.Hex("e070ff") * 0.12f,
        _ => default,
    };

    static readonly Rgb[] Colors = Build();

    static Rgb[] Build()
    {
        var c = new Rgb[64];
        void Set(Biome b, string hex) => c[(int)b] = Rgb.Hex(hex);
        // Sea biomes are recoloured per archetype by ArchetypeLook; these are fallbacks.
        Set(Biome.DeepSea, "0d3b66"); Set(Biome.ShallowSea, "1e8fa6"); Set(Biome.FrozenSea, "d6eaf6");

        Set(Biome.Beach, "ead7a0"); Set(Biome.Grassland, "6db33f"); Set(Biome.Meadow, "b4d443");
        Set(Biome.Forest, "2e7d32"); Set(Biome.PineForest, "1e5c45"); Set(Biome.Tundra, "8f9a6a");
        Set(Biome.Snow, "f3f6fa"); Set(Biome.Cliff, "7b6a5f"); Set(Biome.Riverbank, "4caf50");
        Set(Biome.Jungle, "22a045"); Set(Biome.Rainforest, "136b3a"); Set(Biome.Swamp, "556b2f");
        Set(Biome.CloudForest, "3c8f78");

        Set(Biome.Dunes, "e8984a"); Set(Biome.Erg, "f2c27c"); Set(Biome.SaltFlat, "f2ebdc");
        Set(Biome.Scrub, "a79c4e"); Set(Biome.RedRock, "b5462c"); Set(Biome.Badlands, "d4733c");
        Set(Biome.MesaTop, "e9b27a"); Set(Biome.Oasis, "3f9e45");

        Set(Biome.Glacier, "dceffa"); Set(Biome.Snowfield, "f5f8ff"); Set(Biome.IceTundra, "8c93a8");
        Set(Biome.BlueIce, "7fc2ea"); Set(Biome.HotSpring, "2fd3cf"); Set(Biome.Sinter, "e8822e");

        Set(Biome.Basalt, "2e2a2c"); Set(Biome.Ash, "5d5659"); Set(Biome.LavaField, "4a1d14");
        Set(Biome.Obsidian, "1b1622"); Set(Biome.Sulfur, "e2c93c");

        Set(Biome.CrystalPlain, "7448b5"); Set(Biome.CrystalSpires, "c45ee6"); Set(Biome.Chalk, "ecd0e6");
        Set(Biome.GlowMoss, "34d6ad"); Set(Biome.FungalForest, "d0562c"); Set(Biome.SporeMarsh, "6e7d2c");
        Set(Biome.Mycelium, "eadfbf");
        return c;
    }

    static readonly FloraRule[][] FloraTable = BuildFlora();

    static FloraRule[][] BuildFlora()
    {
        var t = new FloraRule[64][];
        for (int i = 0; i < t.Length; i++) t[i] = System.Array.Empty<FloraRule>();
        void Set(Biome b, params FloraRule[] rules) => t[(int)b] = rules;
        FloraRule R(Flora kind, float density, string hex) => new(kind, density, Rgb.Hex(hex));

        Set(Biome.Grassland, R(Flora.Shrub, 1.4f, "4f8f2f"), R(Flora.Broadleaf, 0.35f, "3f8a34"), R(Flora.Rock, 0.25f, "8a8078"));
        Set(Biome.Meadow, R(Flora.Shrub, 2.4f, "e0b83a"), R(Flora.Shrub, 0.8f, "e06a8a"), R(Flora.Broadleaf, 0.25f, "3f8a34"));
        Set(Biome.Forest, R(Flora.Broadleaf, 9f, "2f7f2f"), R(Flora.Broadleaf, 2f, "5a9a2a"), R(Flora.Shrub, 1f, "3a7030"));
        Set(Biome.PineForest, R(Flora.Conifer, 10f, "1f5a40"), R(Flora.Rock, 0.4f, "7a7470"));
        Set(Biome.Tundra, R(Flora.Rock, 1.2f, "8a8580"), R(Flora.Shrub, 0.6f, "7a8a50"));
        Set(Biome.Snow, R(Flora.Rock, 0.2f, "6a6670"));
        Set(Biome.Cliff, R(Flora.Rock, 1.5f, "7a6a60"));
        Set(Biome.Beach, R(Flora.Rock, 0.15f, "a89a88"), R(Flora.Palm, 0.15f, "3a8a3a"));
        Set(Biome.Riverbank, R(Flora.Shrub, 4f, "3f9a3a"), R(Flora.Broadleaf, 1.6f, "2f8a3a"));
        Set(Biome.Jungle, R(Flora.JungleTree, 9f, "1f9a40"), R(Flora.Palm, 1.5f, "2aa040"), R(Flora.Shrub, 3f, "2a8a30"));
        Set(Biome.Rainforest, R(Flora.JungleTree, 7f, "146a34"), R(Flora.Broadleaf, 5f, "1f7a3a"));
        Set(Biome.Swamp, R(Flora.JungleTree, 1.5f, "4a6a2a"), R(Flora.Shrub, 3f, "5a6a2a"));
        Set(Biome.CloudForest, R(Flora.Conifer, 4f, "2a6a5a"), R(Flora.Broadleaf, 4f, "3a8a6a"));

        Set(Biome.Dunes, R(Flora.Rock, 0.04f, "a0583a"));
        Set(Biome.SaltFlat, R(Flora.Rock, 0.05f, "d8d0c0"));
        Set(Biome.Scrub, R(Flora.Shrub, 1.4f, "8a8a3a"), R(Flora.Cactus, 0.7f, "5a8a3a"));
        Set(Biome.RedRock, R(Flora.Rock, 0.8f, "a0442a"));
        Set(Biome.Badlands, R(Flora.Rock, 0.6f, "b8603a"), R(Flora.Cactus, 0.12f, "5a8a3a"));
        Set(Biome.MesaTop, R(Flora.Shrub, 0.4f, "9a8a4a"), R(Flora.Rock, 0.3f, "c0804a"));
        Set(Biome.Oasis, R(Flora.Palm, 3f, "2f9a3a"), R(Flora.Shrub, 3f, "4a9a3a"));

        Set(Biome.Glacier, R(Flora.IceShard, 0.3f, "bfe4fa"));
        Set(Biome.Snowfield, R(Flora.IceShard, 0.2f, "cfeafa"), R(Flora.Rock, 0.15f, "5a5a6a"));
        Set(Biome.IceTundra, R(Flora.Rock, 1f, "6a6a7a"), R(Flora.Shrub, 0.5f, "7a8a8a"));
        Set(Biome.BlueIce, R(Flora.IceShard, 1.5f, "8fd0f5"));
        Set(Biome.HotSpring, R(Flora.Rock, 0.3f, "c0a080"));
        Set(Biome.Sinter, R(Flora.Rock, 0.4f, "d08040"));

        Set(Biome.Basalt, R(Flora.LavaRock, 1.2f, "2a2628"));
        Set(Biome.Ash, R(Flora.LavaRock, 0.5f, "4a4446"));
        Set(Biome.LavaField, R(Flora.LavaRock, 2f, "201a1a"));
        Set(Biome.Obsidian, R(Flora.Crystal, 0.8f, "151020"));
        Set(Biome.Sulfur, R(Flora.Crystal, 0.6f, "f0d040"));

        Set(Biome.CrystalPlain, R(Flora.Crystal, 2f, "a070f0"));
        Set(Biome.CrystalSpires, R(Flora.Crystal, 6f, "e080ff"), R(Flora.Crystal, 1.5f, "60e0e0"));
        Set(Biome.Chalk, R(Flora.Crystal, 0.4f, "f0a0e0"), R(Flora.Rock, 0.4f, "d0b0c8"));
        Set(Biome.GlowMoss, R(Flora.Mushroom, 0.6f, "40e0b0"), R(Flora.Crystal, 0.4f, "60f0d0"));
        Set(Biome.FungalForest, R(Flora.GiantMushroom, 5f, "e0602a"), R(Flora.GiantMushroom, 1.5f, "f0c040"), R(Flora.Mushroom, 3f, "c04a8a"));
        Set(Biome.SporeMarsh, R(Flora.Mushroom, 4f, "a0a030"));
        Set(Biome.Mycelium, R(Flora.Mushroom, 0.8f, "e08a5a"), R(Flora.GiantMushroom, 0.3f, "e0602a"));
        return t;
    }
}
