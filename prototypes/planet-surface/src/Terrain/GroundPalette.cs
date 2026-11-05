using System;
using Planets.Gen;
using Planets.Look;
using TerrainDigging.Terrain;
using Godot;

namespace PlanetSurface.Terrain;

/// <summary>
/// The colours of the ground, shared by the voxel chunks near the player and the heightfield
/// rings beyond them so the two meet without a visible line. Soil takes its biome's colour,
/// blended across wandering biome borders exactly the way the planet generator blends its map;
/// rock, deep rock and bedrock have one colour per world; ore takes its deposit's colour.
///
/// Colours are grounded and readable (soil, sand, ash, snow, stone), each world keeping its own
/// character: red sandstone canyons, black basalt, violet crystal rock, pale mycelium flats.
/// Read-only after construction; mesh workers share it.
/// </summary>
public sealed class GroundPalette
{
    public readonly WorldMap Map;
    public readonly Rgb Rock, DeepRock, Bedrock, Seabed, RiverBed, Snow;

    /// <summary>How strongly rock faces show horizontal strata (0-1): canyons and mesas show them most.</summary>
    public readonly float Strata;

    readonly Rgb[] _cells;
    readonly float[] _glow;
    readonly Fractal _jitter, _tone;

    public GroundPalette(WorldMap map)
    {
        Map = map;
        _jitter = new Fractal(Hash.Of(map.Spec.Seed, 0x6A6974), 160, 4);
        _tone = new Fractal(Hash.Of(map.Spec.Seed, 0x746F6E), 700, 3);
        _cells = new Rgb[map.Count];
        _glow = new float[map.Count];
        for (int k = 0; k < map.Count; k++)
        {
            var biome = (Biome)map.Biome[k];
            _cells[k] = Soil(biome);
            _glow[k] = biome switch { Biome.LavaField => 1f, Biome.HotSpring => 0.12f, Biome.GlowMoss => 0.1f, _ => 0f };
        }

        (Rock, Strata) = (map.Def.Archetype, map.Def.Variant) switch
        {
            (Archetype.Temperate, _) => (Rgb.Hex("726b61"), 0.25f),
            (Archetype.Arid, 0) => (Rgb.Hex("a2725a"), 0.55f),
            (Archetype.Arid, _) => (Rgb.Hex("9c5436"), 0.9f),
            (Archetype.Ice, _) => (Rgb.Hex("5d646e"), 0.3f),
            (Archetype.Lava, _) => (Rgb.Hex("2f2a2b"), 0.35f),
            (Archetype.Alien, 0) => (Rgb.Hex("6f6185"), 0.5f),
            _ => (Rgb.Hex("6b5a52"), 0.3f),
        };
        DeepRock = Rock * 0.72f;
        Bedrock = Rgb.Hex("26262b");
        Seabed = Rgb.Lerp(Rock, Rgb.Hex("b0a48a"), 0.45f) * 0.85f;
        RiverBed = Rgb.Lerp(Rock, Rgb.Hex("4a4034"), 0.6f);
        Snow = Rgb.Hex("eef2f6");
    }

    /// <summary>A grounded colour for each biome's topsoil, sand, ash or snow.</summary>
    public static Rgb Soil(Biome b) => b switch
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
        _ => Rgb.Hex("6a6a5a"),
    };

    /// <summary>Blended soil colour at a point (canonical metres) and how much it glows (lava fields, glow moss).</summary>
    public Rgb SoilAt(double x, double z, out float glow)
    {
        Jitter(x, z, out double gx, out double gz);
        int i0 = (int)Math.Floor(gx), j0 = (int)Math.Floor(gz);
        float tx = (float)(gx - i0), tz = (float)(gz - j0);
        Rgb c = default;
        float g = 0f, total = 0f;
        Add(i0, j0, (1 - tx) * (1 - tz), ref c, ref g, ref total);
        Add(i0 + 1, j0, tx * (1 - tz), ref c, ref g, ref total);
        Add(i0, j0 + 1, (1 - tx) * tz, ref c, ref g, ref total);
        Add(i0 + 1, j0 + 1, tx * tz, ref c, ref g, ref total);
        if (total <= 0f)
        {
            glow = 0f;
            return Seabed;
        }
        glow = g / total;
        float tone = _tone.Fbm(Map.Model.Torus.At(x, z), 32);
        return c * ((1f / total) * (1f + 0.08f * tone));
    }

    /// <summary>The biome at a point, with the same wandering borders as the colours.</summary>
    public Biome BiomeAt(double x, double z)
    {
        Jitter(x, z, out double gx, out double gz);
        return (Biome)Map.Biome[Map.Index((int)Math.Round(gx), (int)Math.Round(gz))];
    }

    /// <summary>
    /// The colour a ground vertex shows. <paramref name="rock"/> is how bare the ground is (0
    /// soil, 1 rock face): the shader adds strata and rock detail by it. <paramref name="flags"/>
    /// carries glow (x) and bedrock (y).
    /// </summary>
    public Color Vertex(CellMaterial material, double x, double z, float height, out Vector2 flags, DepositKind ore = DepositKind.Iron)
    {
        flags = Vector2.Zero;
        Rgb c;
        float rock = 0f;
        switch (material)
        {
            case CellMaterial.Ore:
                c = DepositLook.Color(ore) * 0.85f;
                flags.X = DepositLook.Glow(ore) * 0.5f;
                rock = 0.6f;
                break;
            case CellMaterial.Bedrock:
                c = Bedrock;
                flags.Y = 1f;
                rock = 1f;
                break;
            case CellMaterial.DeepRock:
                c = DeepRock;
                rock = 1f;
                break;
            case CellMaterial.Rock:
                c = Rock;
                rock = 1f;
                break;
            default:
                c = SoilAt(x, z, out float glow);
                flags.X = glow;
                if (height < 0.6f) c = Rgb.Lerp(c, Seabed, Math.Clamp(0.6f - height, 0f, 1f)); // wet sand and silt at the waterline
                break;
        }
        return new Color(c.R, c.G, c.B, rock);
    }

    /// <summary>
    /// The colour of undug ground: soil over rock by <paramref name="cover"/> (0 bare rock, 1 all
    /// soil), continuous so the near voxels and the far rings blend the same way.
    /// </summary>
    public Color Natural(double x, double z, float height, float cover, out Vector2 flags)
    {
        Rgb soil = SoilAt(x, z, out float glow);
        Rgb c = Rgb.Lerp(Rock, soil, cover);
        if (height < 0.6f) c = Rgb.Lerp(c, Seabed, Math.Clamp(0.6f - height, 0f, 1f));
        flags = new Vector2(glow * cover, 0f);
        return new Color(c.R, c.G, c.B, 1f - cover);
    }

    void Jitter(double x, double z, out double gx, out double gz)
    {
        P4 p = Map.Model.Torus.At(x, z);
        gx = x / Map.CellSize - 0.5 + _jitter.Fbm(p, 4) * 0.9;
        gz = z / Map.CellSize - 0.5 + _jitter.Fbm(p.Swap, 4) * 0.9;
    }

    void Add(int i, int j, float w, ref Rgb c, ref float g, ref float total)
    {
        int k = Map.Index(i, j);
        if (Map.Has(k, CellFlags.Water)) return;
        c += _cells[k] * w;
        g += _glow[k] * w;
        total += w;
    }
}
