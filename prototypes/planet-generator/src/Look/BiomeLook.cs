using Planets.Gen;

namespace Planets.Look;

/// <summary>Low-poly scatter kinds. Meshes are built in Surface.FloraMeshes.</summary>
public enum Flora { Conifer, Broadleaf, JungleTree, Palm, Shrub, Rock, Cactus, Crystal, Mushroom, GiantMushroom, LavaRock, IceShard }

/// <summary>
/// One kind of scatter in a biome: how many per 1000 m², and its colour. For plants this says only what
/// a biome wants: what each actually looks like is rolled per world by the flora module.
/// </summary>
public readonly record struct FloraRule(Flora Kind, float Density, Rgb Color);

/// <summary>
/// The flora of every biome, and the hand colours of today's look: each biome's ground and its colour on
/// the globe (every other world's natural colours are its own: <see cref="WorldPalette"/>). Scatter is
/// quieter than the land it stands on: it reads as things lying in a world, never as sweets on a lawn.
/// </summary>
public static class BiomeLook
{
    public static FloraRule[] FloraOf(Biome b) => FloraTable[(int)b];

    /// <summary>
    /// The ground under each biome, as the surface shows it: its topsoil, sand, ash, ice, snow or barren rock. Today's look and
    /// every airless body paint every biome's ground from this (<see cref="WorldPalette.Hand"/>); elsewhere only hot-spring sinter,
    /// sulfur, obsidian, blue ice and the crystal and fungal grounds come from here, whose chemistry the world palette doesn't hold.
    /// </summary>
    public static Rgb Ground(Biome b) => b switch
    {
        Biome.Beach => Rgb.Hex("c8b386"),
        Biome.Grassland => Rgb.Hex("5b7838"),
        Biome.Meadow => Rgb.Hex("76863c"),
        Biome.Forest => Rgb.Hex("40592c"),
        Biome.PineForest => Rgb.Hex("3e4e34"),
        Biome.Tundra => Rgb.Hex("7d7a5c"),
        Biome.Snow => Rgb.Hex("e4e9ee"),
        Biome.Cliff => Rgb.Hex("6e675e"),
        Biome.Riverbank => Rgb.Hex("4d6c33"),
        Biome.Jungle => Rgb.Hex("3b6829"),
        Biome.Rainforest => Rgb.Hex("345d2b"),
        Biome.Swamp => Rgb.Hex("4f5a31"),
        Biome.CloudForest => Rgb.Hex("486848"),
        Biome.Dunes => Rgb.Hex("c99860"),
        Biome.Erg => Rgb.Hex("d6ad76"),
        Biome.SaltFlat => Rgb.Hex("dcd4c2"),
        Biome.Scrub => Rgb.Hex("998a56"),
        Biome.RedRock => Rgb.Hex("9c5638"),
        Biome.Badlands => Rgb.Hex("ad6e47"),
        Biome.MesaTop => Rgb.Hex("b78a5a"),
        Biome.Oasis => Rgb.Hex("55783a"),
        Biome.Glacier => Rgb.Hex("dde7ef"),
        Biome.Snowfield => Rgb.Hex("edf1f5"),
        Biome.IceTundra => Rgb.Hex("888e96"),
        Biome.BlueIce => Rgb.Hex("9dc1da"),
        Biome.HotSpring => Rgb.Hex("6aa59c"),
        Biome.Sinter => Rgb.Hex("bf8f5e"),
        Biome.Basalt => Rgb.Hex("3a3436"),
        Biome.Ash => Rgb.Hex("5b5553"),
        Biome.LavaField => Rgb.Hex("2e2220"),
        Biome.Obsidian => Rgb.Hex("201b24"),
        Biome.Sulfur => Rgb.Hex("c2ae4a"),
        Biome.CrystalPlain => Rgb.Hex("6c5c86"),
        Biome.CrystalSpires => Rgb.Hex("8a6ca6"),
        Biome.Chalk => Rgb.Hex("d2c6d0"),
        Biome.GlowMoss => Rgb.Hex("3a8676"),
        Biome.FungalForest => Rgb.Hex("874a3c"),
        Biome.SporeMarsh => Rgb.Hex("5e6a35"),
        Biome.Mycelium => Rgb.Hex("cbbfa4"),
        // Barren ground (proto/vacuum's albedos): deep greys, lit and shaded by the palette's light.
        Biome.Regolith => Rgb.Hex("5c5c5c"),
        Biome.Mare => Rgb.Hex("3a3a3b"),
        Biome.Highland => Rgb.Hex("7c7c7c"),
        Biome.Ironstone => Rgb.Hex("4a4848"),
        Biome.Frost => Rgb.Hex("cdd1d6"),
        _ => Rgb.Hex("6a6a5a"),
    };

    /// <summary>Each biome's colour as today's look paints the globe: bold and few, land, liquid and sky set against each other.</summary>
    public static Rgb Color(Biome b) => Colors[(int)b];

    static readonly Rgb[] Colors = Build();

    static Rgb[] Build()
    {
        var c = new Rgb[64];
        void Set(Biome b, string hex) => c[(int)b] = Rgb.Hex(hex);
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

        // Barren ground, a plain first pass in greys (the palette colours each by its mineral, Minerals.Of).
        Set(Biome.Regolith, "606060"); Set(Biome.Mare, "3e3e40"); Set(Biome.Highland, "808080");
        Set(Biome.Ironstone, "4c4a4a"); Set(Biome.Frost, "d2d6da");
        return c;
    }

    /// <summary>Self-lit colour (lava cracks, glowing moss). Black for most biomes.</summary>
    public static Rgb Glow(Biome b) => b switch
    {
        Biome.LavaField => Rgb.Hex("ff4a10") * 0.55f,
        Biome.HotSpring => Rgb.Hex("40fff0") * 0.12f,
        Biome.GlowMoss => Rgb.Hex("30ffc0") * 0.18f,
        Biome.CrystalSpires => Rgb.Hex("e070ff") * 0.12f,
        _ => default,
    };

    static readonly FloraRule[][] FloraTable = BuildFlora();

    static FloraRule[][] BuildFlora()
    {
        var t = new FloraRule[64][];
        for (int i = 0; i < t.Length; i++) t[i] = System.Array.Empty<FloraRule>();
        void Set(Biome b, params FloraRule[] rules) => t[(int)b] = rules;
        FloraRule R(Flora kind, float density, string hex) => new(kind, density, Rgb.Hex(hex));

        Set(Biome.Grassland, R(Flora.Shrub, 2.4f, "4f8f2f"), R(Flora.Broadleaf, 0.6f, "3f8a34"), R(Flora.Rock, 0.25f, "8a8078"));
        Set(Biome.Meadow, R(Flora.Shrub, 4f, "6f8a3c"), R(Flora.Shrub, 1.2f, "8c8f52"), R(Flora.Broadleaf, 0.4f, "3f8a34"));
        Set(Biome.Forest, R(Flora.Broadleaf, 9f, "2f7f2f"), R(Flora.Broadleaf, 2f, "5a9a2a"), R(Flora.Shrub, 1f, "3a7030"));
        Set(Biome.PineForest, R(Flora.Conifer, 10f, "1f5a40"), R(Flora.Rock, 0.4f, "7a7470"));
        Set(Biome.Tundra, R(Flora.Rock, 1.2f, "8a8580"), R(Flora.Shrub, 1.6f, "7a8a50"), R(Flora.Conifer, 0.4f, "2a5a48"));
        Set(Biome.Snow, R(Flora.Rock, 0.2f, "6a6670"));
        Set(Biome.Cliff, R(Flora.Rock, 1.5f, "7a6a60"));
        Set(Biome.Beach, R(Flora.Rock, 0.15f, "a89a88"), R(Flora.Palm, 0.5f, "3a8a3a"));
        Set(Biome.Riverbank, R(Flora.Shrub, 4f, "3f9a3a"), R(Flora.Broadleaf, 1.6f, "2f8a3a"));
        Set(Biome.Jungle, R(Flora.JungleTree, 9f, "1f9a40"), R(Flora.Palm, 1.5f, "2aa040"), R(Flora.Shrub, 3f, "2a8a30"));
        Set(Biome.Rainforest, R(Flora.JungleTree, 7f, "146a34"), R(Flora.Broadleaf, 5f, "1f7a3a"));
        Set(Biome.Swamp, R(Flora.JungleTree, 5f, "4a6a2a"), R(Flora.Shrub, 4f, "5a6a2a"));
        Set(Biome.CloudForest, R(Flora.Conifer, 4f, "2a6a5a"), R(Flora.Broadleaf, 4f, "3a8a6a"));

        Set(Biome.Dunes, R(Flora.Rock, 0.04f, "a0583a"), R(Flora.Cactus, 0.1f, "5a8a3a"));
        Set(Biome.SaltFlat, R(Flora.Rock, 0.05f, "d8d0c0"));
        Set(Biome.Scrub, R(Flora.Shrub, 2.6f, "8a8a3a"), R(Flora.Cactus, 1.4f, "5a8a3a"), R(Flora.Palm, 0.5f, "6a8a3a"));
        Set(Biome.RedRock, R(Flora.Rock, 0.8f, "a0442a"), R(Flora.Cactus, 0.25f, "5a8a3a"), R(Flora.Shrub, 0.2f, "8a8a3a"));
        Set(Biome.Badlands, R(Flora.Rock, 0.6f, "b8603a"), R(Flora.Cactus, 0.4f, "5a8a3a"), R(Flora.Shrub, 0.3f, "8a8a3a"));
        Set(Biome.MesaTop, R(Flora.Shrub, 1f, "9a8a4a"), R(Flora.Rock, 0.3f, "c0804a"), R(Flora.Cactus, 0.3f, "5a8a3a"));
        Set(Biome.Oasis, R(Flora.Palm, 3f, "2f9a3a"), R(Flora.Shrub, 3f, "4a9a3a"));

        Set(Biome.Glacier, R(Flora.IceShard, 0.3f, "bfe4fa"), R(Flora.Conifer, 0.3f, "3a5a58"));
        Set(Biome.Snowfield, R(Flora.IceShard, 0.2f, "cfeafa"), R(Flora.Rock, 0.15f, "5a5a6a"), R(Flora.Conifer, 2.4f, "3a5a58"), R(Flora.Shrub, 0.6f, "7a8a8a"));
        Set(Biome.IceTundra, R(Flora.Rock, 1f, "6a6a7a"), R(Flora.Shrub, 1.4f, "7a8a8a"), R(Flora.Conifer, 2.2f, "3a5a58"));
        Set(Biome.BlueIce, R(Flora.IceShard, 1.5f, "8fd0f5"));
        Set(Biome.HotSpring, R(Flora.Rock, 0.3f, "c0a080"), R(Flora.Shrub, 2.4f, "4a8a5a"), R(Flora.Palm, 2f, "3a8a5a"), R(Flora.Mushroom, 1f, "c0a060"));
        Set(Biome.Sinter, R(Flora.Rock, 0.4f, "d08040"), R(Flora.Shrub, 0.6f, "6a8a5a"));

        Set(Biome.Basalt, R(Flora.LavaRock, 1.2f, "2a2628"));
        Set(Biome.Ash, R(Flora.LavaRock, 0.5f, "4a4446"));
        Set(Biome.LavaField, R(Flora.LavaRock, 2f, "201a1a"));
        Set(Biome.Obsidian, R(Flora.Crystal, 0.8f, "151020"));
        Set(Biome.Sulfur, R(Flora.Crystal, 0.6f, "f0d040"));

        Set(Biome.CrystalPlain, R(Flora.Crystal, 2f, "a070f0"), R(Flora.Palm, 2.4f, "7a60a0"), R(Flora.Shrub, 1.6f, "7a60a0"));
        Set(Biome.CrystalSpires, R(Flora.Crystal, 6f, "e080ff"), R(Flora.Crystal, 1.5f, "60e0e0"));
        Set(Biome.Chalk, R(Flora.Crystal, 0.4f, "f0a0e0"), R(Flora.Rock, 0.4f, "d0b0c8"), R(Flora.Palm, 1.2f, "a080a0"), R(Flora.Shrub, 1.2f, "a080a0"));
        Set(Biome.GlowMoss, R(Flora.Mushroom, 1.2f, "40e0b0"), R(Flora.Crystal, 0.4f, "60f0d0"), R(Flora.JungleTree, 2.5f, "30a080"));
        Set(Biome.FungalForest, R(Flora.GiantMushroom, 5f, "e0602a"), R(Flora.GiantMushroom, 1.5f, "f0c040"), R(Flora.Mushroom, 3f, "c04a8a"));
        Set(Biome.SporeMarsh, R(Flora.Mushroom, 4f, "a0a030"), R(Flora.JungleTree, 3f, "6a7a30"), R(Flora.Shrub, 2f, "7a8a30"));
        Set(Biome.Mycelium, R(Flora.Mushroom, 1.6f, "e08a5a"), R(Flora.GiantMushroom, 1.2f, "e0602a"));

        // Nothing grows on barren ground: only the boulders impacts threw out.
        Set(Biome.Regolith, R(Flora.Rock, 0.5f, "565656"));
        Set(Biome.Mare, R(Flora.Rock, 0.3f, "363638"));
        Set(Biome.Highland, R(Flora.Rock, 0.6f, "707070"));
        Set(Biome.Ironstone, R(Flora.Rock, 0.6f, "444242"));
        Set(Biome.Frost, R(Flora.Rock, 0.15f, "8a8e94"));
        return t;
    }
}
